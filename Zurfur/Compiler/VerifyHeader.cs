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
    class VerifyHeader
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

        public static void Verify(SymbolTable symbols)
        {
            // TBD: What we really want to do is clear the lookup table
            //      and regenerate it along with SymSpecializedType's.
            //      The regeneration will have to happen when loading
            //      pre-compiled packages, so it will be done eventually.
            //      For now, we will use the compiler generated output.

            foreach (var symbol in symbols.Symbols)
            {
                if (symbol.Name == "")
                    return;
                if (symbol.Name == symbol.Parent.Name)
                {
                    if (symbol.IsMethodGroup)
                    {
                        foreach (var s in symbol.Children)
                            Reject(s.Value.Token, "Must not be same name as parent scope");
                    }
                    else
                    {
                        Reject(symbol.Token, "Must not be same name as parent scope");
                    }
                }
                else if (symbol.IsTypeParam)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                }
                else if (symbol.IsMethodGroup)
                {
                    RejectIllegalOverloads(symbol);
                }
                else if (symbol.IsMethod)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                    if (!symbol.Parent.IsMethodGroup)
                        Reject(symbol.Token, "Compiler error: Expecting parent symbol to be method group");
                    if (symbol.IsExtension && !symbol.Parent.Parent.IsModule)
                        Reject(symbol.Token, "Extension method must be defined only at the module level");

                    // TBD: Static may appear in extension methods
                    //if (symbol.Parent.Parent is SymModule && symbol.Qualifiers.Contains("static"))
                    //    Reject(symbol.Token, "Methods in a module may not have the static qualifier");
                }
                else if (symbol.IsMethodParam)
                {
                    if (symbol.Name == symbol.Parent.Parent.Name)
                        Reject(symbol.Token, "Most not be same name as method");
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                    CheckTypeName(symbol.Token, symbol.TypeName);
                }
                else if (symbol.IsField)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent);
                    CheckTypeName(symbol.Token, symbol.TypeName);
                }
                else if (symbol.IsType)
                {
                    if (symbol.FullName.Contains("$impl"))
                    {
                        // These would be compiler errors
                        if (!symbol.Parent.IsModule)
                            Reject(symbol.Token, "impl blocks must be at the module level");
                        foreach (var child in symbol.Children.Values)
                            Reject(child.Token, "impl blocks may not contain children");
                    }
                }
            }

            void CheckTypeName(Token token, string typeName)
            {
                var s = symbols.Lookup(typeName);
                if (s == null)
                {
                    if (typeName == "")
                        Reject(token, "Unresolved type");
                    else
                        Reject(token, $"Unknown type name: '{typeName}'");
                    return;
                }
                if (!s.IsAnyTypeNotModule)
                {
                    Reject(token, $"The type '{typeName}' is not a type, it is a '{s.KindName}'");
                    return;
                }
                if (s.IsType)
                {
                    var genericParams = s.GenericParamTotal();
                    var genericArgs = 0;
                    if (genericParams != genericArgs)
                        Reject(token, $"The type '{typeName}' requires {genericParams} generic type parameters, but {genericArgs} were supplied");
                }
                if (s.IsSpecializedType)
                {
                    var ptype = (SymSpecializedType)s;
                    var genericParams = s.GenericParamTotal();
                    var genericArgs = ptype.Params.Length + ptype.Returns.Length;
                    if (genericParams != genericArgs)
                        Reject(token, $"The type '{typeName}' requires {genericParams} generic type parameters, but {genericArgs} were supplied");
                    foreach (var t in ptype.Params)
                        CheckTypeName(token, t.FullName);
                    foreach (var t in ptype.Returns)
                        CheckTypeName(token, t.FullName);
                }
            }

            // A type parameter in any enclosing scope prevents
            // children scopes from declaring that symbol.
            void RejectDuplicateTypeParameterName(Token token, Symbol scope)
            {
                while (scope.Name != "")
                {
                    if (scope.Children.TryGetValue(token.Name, out var s)
                            && s.IsTypeParam)
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
            void RejectIllegalOverloads(Symbol methodGroup)
            {
                bool hasNonMethod = false;
                bool hasStatic = false;
                bool hasNonStatic = false;
                bool hasFunction = false;
                bool hasProperty = false;
                foreach (var child in methodGroup.Children.Values)
                {
                    if (!child.IsMethod)
                    {
                        Reject(child.Token, $"Compiler error: All symbols in this group must be methods. '{child.FullName}' is a '{child.GetType()}'");
                        hasNonMethod = true;
                        continue;
                    }
                    if (child.IsStatic)
                        hasStatic = true;
                    else
                        hasNonStatic = true;
                    if (child.IsGetter || child.IsSetter)
                        hasProperty = true;
                    else if (child.IsFunc)
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

    }
}
