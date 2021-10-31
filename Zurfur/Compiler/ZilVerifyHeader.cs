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
                    Reject(symbol.Token, "Must not be same name as parent scope");
                }
                else if (symbol is SymTypeParam)
                {
                    RejectDuplicateTypeParameterName(symbol, symbol.Parent.Parent); // Skip containing type or method
                }
                else if (symbol is SymMethodGroup methodGroup)
                {
                    RejectDuplicateTypeParameterName(symbol, symbol.Parent.Parent); // Skip containing type or method
                    RejectIllegalOverloads(methodGroup);
                }
                else if (symbol is SymMethod method)
                {
                    if (!(symbol.Parent is SymMethodGroup))
                        Reject(symbol.Token, "Compiler error: Expecting parent symbol to be method group");
                    if (symbol.Parent.Parent is SymNamespace && symbol.Qualifiers.Contains("static"))
                        Reject(symbol.Token, "Methods in a namespace may not be static");
                    if (symbol.Parent.Parent.Name == "$ext" && !(symbol.Parent.Parent.Parent is SymNamespace))
                        Reject(symbol.Token, "Extension method must be defined only at the namespace level");
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
                    RejectDuplicateTypeParameterName(symbol, symbol.Parent.Parent); // Skip containing type or method
                    CheckType(methodParam.Token, methodParam.TypeName);
                }
                else if (symbol is SymField field)
                {
                    if (symbol.Parent is SymNamespace)
                        Reject(symbol.Token, "A namespace may not directly contain fields");
                    RejectDuplicateTypeParameterName(symbol, symbol.Parent.Parent);
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
                        CheckType(token, t.GetFullName());
                    foreach (var t in ptype.Returns)
                        CheckType(token, t.GetFullName());
                }
            }

            // A type parameter in any enclosing scope prevents
            // children scopes from declaring that symbol.
            void RejectDuplicateTypeParameterName(Symbol symbol, Symbol parent)
            {
                while (!(parent is SymNamespace))
                {
                    if (parent.Children.TryGetValue(symbol.Name, out var s)
                            && s is SymTypeParam)
                        if (!symbol.Token.Error)
                            Reject(symbol.Token, "Must not be same name as a type parameter in any enclosing scope");
                    parent = parent.Parent;
                }
            }

            // TBD: Need to see though alias types
            //      (e.g. reject 'fun f(a int)' with 'fun f(a MyAliasInt)'
            // TBD: Reject overloads with different return types
            // TBD: Need to reject more (extension methods with same name
            //      as members, etc.)
            void RejectIllegalOverloads(SymMethodGroup methodGroup)
            {
                if (methodGroup.Children.Count <= 1)
                    return;

                // Static and non-static may not coexist
                bool hasStatic = false;
                bool hasNonStatic = false;
                foreach (var child in methodGroup.Children.Values)
                    if (child.Qualifiers.Contains("static"))
                        hasStatic = true;
                    else
                        hasNonStatic = true;
                if (hasStatic && hasNonStatic)
                {
                    foreach (var child in methodGroup.Children.Values)
                        Reject(child.Token, "Illegal overload: Static and non-static methods may not be overloaded in the same scope");
                }

                // Generic and non generic may not coexist
                var hasGenericParameters = false;
                foreach (var child in methodGroup.Children.Values)
                    if (child.GenericParamCount() != 0)
                    {
                        hasGenericParameters = true;
                        break;
                    }
                if (hasGenericParameters)
                    foreach (var child in methodGroup.Children.Values)
                    {
                        if (child.GenericParamCount() == 0)
                            Reject(child.Token, "Illegal overload: Generic methods may not be overloaded.  There is a generic method with the same name in this scope.");
                        else
                            Reject(child.Token, "Illegal overload: Generic methods may not be overloaded.");
                    }
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
