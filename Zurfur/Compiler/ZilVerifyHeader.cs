using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class VerifyHeaderError : TokenError
    {
        public VerifyHeaderError(string message) : base(message) { }
    }

    /// <summary>
    /// Suppress the verifier error message on a token that has this class.
    /// The compiler adds this class to a symbol that failed type resolution.
    /// This way, the verifier doesn't mark a second error which would be
    /// confusing to the user.  
    /// </summary>
    public class VerifySuppressError { }

    // Some compiler error checking can be done here and reported directly
    // back to the user.  However, the compiler can generate better error
    // messages at a more exact location (e.g. the verifier can only mark
    // the symbol it has access to, such as the field or parameter, whereas
    // the compiler can show an error directly at the type name token that
    // failed).
    class ZilVerifyHeader
    {

        /// <summary>
        /// What to do when an error is detected and it has been suppressed
        /// by the compiler.  All code must be verified in strict mode before
        /// being executed.
        /// </summary>
        enum SuppressErrorMode
        {
            Strict, // Untrusted source, always make an error
            Warn,   // We are debugging, generate warning so we can see it
            Ignore  // Trusted source, ignore suppressed errors
        }

        /// <summary>
        /// This is still a WIP.  Since the compiler can generate errors
        /// closer to where the problem is, it adds VerifySuppressError
        /// to the token that the verifier would mark.  Multipler error
        /// messages is confusing, so ignore them if the compiler told
        /// us there is an error.
        /// 
        /// HOWEVER: We must still run the verifier in strict mode
        /// just in case the compiler made a mistake.
        /// </summary>
        static SuppressErrorMode sSuppressErrors = SuppressErrorMode.Ignore;

        public static void VerifyHeader(SymbolTable symbols)
        {
            //return;
            symbols.VisitAll((symbol) =>
            {
                if (symbol.Name == "")
                    return;
                if (symbol.Name == symbol.Parent.Name)
                {
                    if (symbol is SymMethodGroup group)
                    {
                        foreach (var s in group.Children)
                            Reject(s.Value.Token, "Must not be same name as parent scope");
                    }
                    else
                    {
                        Reject(symbol.Token, "Must not be same name as parent scope");
                    }
                }
                else if (symbol is SymTypeParam)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                }
                else if (symbol is SymMethodGroup methodGroup)
                {
                    RejectIllegalOverloads(methodGroup);
                }
                else if (symbol is SymMethod method)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                    if (!(symbol.Parent is SymMethodGroup))
                        Reject(symbol.Token, "Compiler error: Expecting parent symbol to be method group");
                    if (symbol.Parent.Parent is SymModule && symbol.Qualifiers.Contains("static"))
                        Reject(symbol.Token, "Methods in a module may not have the static qualifier");
                    if (symbol.Parent.Parent.Name == "$extension" && !(symbol.Parent.Parent.Parent is SymModule))
                        Reject(symbol.Token, "Extension method must be defined only at the module level");
                    if (method.IsGetter)
                    {
                        if (symbol.Parent.Name != "operator["
                                && CountMethodParams(symbol, false) != (symbol.HasQualifier("static") ? 0 : 1))
                            Reject(symbol.Token, "Getter must have no parameters");
                        if (CountMethodParams(symbol, true) == 0)
                            Reject(symbol.Token, "Getter must have a return value");
                    }
                    if (method.IsSetter)
                    {
                        if (symbol.Parent.Name != "operator["
                                && CountMethodParams(symbol, false) != (symbol.HasQualifier("static") ? 1 : 2))
                            Reject(symbol.Token, "Setter must have 1 parameter");
                        if (CountMethodParams(symbol, true) != 0)
                            Reject(symbol.Token, "Setter must have no return value");
                    }
                }
                else if (symbol is SymMethodParam methodParam)
                {
                    if (symbol.Name == symbol.Parent.Parent.Name)
                        Reject(symbol.Token, "Most not be same name as method");
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                    CheckType(methodParam.Token, methodParam.TypeName);
                }
                else if (symbol is SymField field)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent);
                    CheckType(field.Token, field.TypeName);
                }
            });

            void CheckType(Token token, string typeName)
            {
                var s = symbols.LookupType(typeName);
                if (s == null)
                {
                    if (typeName == "")
                        Reject(token, "Unresolved type");
                    else
                        Reject(token, $"Unknown type name: '{typeName}'");
                    return;
                }
                if (!(s is SymType || s is SymTypeParam || s is SymParameterizedType))
                {
                    Reject(token, $"The type '{typeName}' is not a type, it is a '{s.Kind}'");
                    return;
                }
                if (s is SymType)
                {
                    var genericParams = s.GenericParamTotal();
                    var genericArgs = 0;
                    if (genericParams != genericArgs)
                        Reject(token, $"The type '{typeName}' requires {genericParams} generic type parameters, but {genericArgs} were supplied");
                }
                if (s is SymParameterizedType ptype)
                {
                    var genericParams = s.GenericParamTotal();
                    var genericArgs = ptype.Params.Length + ptype.Returns.Length;
                    if (genericParams != genericArgs)
                        Reject(token, $"The type '{typeName}' requires {genericParams} generic type parameters, but {genericArgs} were supplied");
                    foreach (var t in ptype.Params)
                        CheckType(token, t.FullName);
                    foreach (var t in ptype.Returns)
                        CheckType(token, t.FullName);
                }
            }

            // A type parameter in any enclosing scope prevents
            // children scopes from declaring that symbol.
            void RejectDuplicateTypeParameterName(Token token, Symbol scope)
            {
                while (scope.Name != "")
                {
                    if (scope.Children.TryGetValue(token.Name, out var s)
                            && s is SymTypeParam)
                        if (!token.Error)
                            Reject(token, "Must not be same name as a type parameter in any enclosing scope");
                    scope = scope.Parent;
                }
            }

            // TBD: Need to see though alias types
            //      (e.g. reject 'fun f(a int)' with 'fun f(a MyAliasInt)'
            // TBD: Reject overloads with different return types
            // TBD: Need to reject more (extension methods with same name
            //      as members, etc.)
            void RejectIllegalOverloads(SymMethodGroup methodGroup)
            {
                bool hasNonMethod = false;
                bool hasStatic = false;
                bool hasNonStatic = false;
                bool hasFunction = false;
                bool hasProperty = false;
                foreach (var child in methodGroup.Children.Values)
                {
                    if (!(child is SymMethod method))
                    {
                        Reject(child.Token, $"Compiler error: All symbols in this group must be methods. '{child.FullName}' is a '{child.GetType()}'");
                        hasNonMethod = true;
                        continue;
                    }
                    if (child.Qualifiers.Contains("static"))
                        hasStatic = true;
                    else
                        hasNonStatic = true;
                    if (method.IsGetter || method.IsSetter)
                        hasProperty = true;
                    else if (method.IsFunc)
                        hasFunction = true;
                    else
                    {
                        Reject(child.Token, $"Compiler error: Illegal symbol name: {child.FullName}");
                        hasNonMethod = true;
                    }
                }
                if (hasNonMethod)
                    return;

                // Static/non-static may not coexist
                if (hasStatic && hasNonStatic)
                {
                    RejectChildren(methodGroup, "Illegal overload: Static and non-static methods may not be overloaded in the same scope");
                    return;
                }

                // Function/property may not coexist
                if (hasFunction && hasProperty)
                {
                    RejectChildren(methodGroup, "Illegal overload: Functions and properties may not be overloaded in the same scope");
                    return;
                }

                if (methodGroup.Parent.Name == "$extension")
                {
                    // TBD: There is a lot we need to verify here.
                    //      1. Need to separate them by concrete type
                    //      2. Need to ensure they don't cover a member function
                    //      3. Need to prevent generic function overloads (like below)
                    //              but allow generic parameter for the type it is an extension of
                }
                else
                {
                    // Generic functions may not be overloaded
                    var hasGenericParameters = false;
                    foreach (var child in methodGroup.Children.Values)
                        if (child.GenericParamCount() != 0)
                        {
                            hasGenericParameters = true;
                            break;
                        }
                    if (hasGenericParameters && methodGroup.Children.Count != 1)
                        foreach (var child in methodGroup.Children.Values)
                        {
                            if (child.GenericParamCount() == 0)
                                Reject(child.Token, "Illegal overload: Generic methods may not be overloaded.  There is a generic method with the same name in this scope.");
                            else
                                Reject(child.Token, "Illegal overload: Generic methods may not be overloaded.");
                        }
                }
            }

            void RejectChildren(Symbol s, string message)
            {
                foreach (var child in s.Children.Values)
                    Reject(child.Token, message);
            }

            // Does not reject if there is already an error there
            void Reject(Token token, string message)
            {
                if (token.Error)
                    return; // Ignore multiple errors, the first one is the msot important

                switch (sSuppressErrors)
                {
                    case SuppressErrorMode.Strict:
                        token.AddError(new VerifyHeaderError(message));
                        break;

                    case SuppressErrorMode.Warn:
                        if (token.GetInfo<VerifySuppressError>() == null)
                            token.AddError(new VerifyHeaderError(message));
                        else
                            token.AddWarning(new TokenWarn("(Error suppressed): " + message));
                        break;

                    case SuppressErrorMode.Ignore:
                        if (token.GetInfo<VerifySuppressError>() == null)
                            token.AddError(new VerifyHeaderError(message));
                        break;
                }

            }

        }

        private static int CountMethodParams(Symbol symbol, bool returns)
        {
            int paramCount = 0;
            foreach (var s in symbol.Children.Values)
                if (s is SymMethodParam p && p.IsReturn == returns)
                    paramCount++;
            return paramCount;
        }
    }
}
