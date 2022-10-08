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

            foreach (var symbol in symbols.Root.ChildrenRecurse())
            {
                if (symbol.LookupName == "")
                    return;

                // TBD: Reject illegal overload methods
                //      Need to see though alias types
                //      (e.g. reject 'fun f(a int)' with 'fun f(a MyAliasInt)'
                //      Reject overloads with different return types
                //      Eextension methods with same name as members, etc.

                if (symbol.IsTypeParam)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                }
                else if (symbol.IsMethod)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method

                    var methodParent = symbol.Parent;
                    if (symbol.IsStatic && methodParent.IsModule && !symbol.IsExtension)
                        Reject(symbol.Token, "'static' not allowed at module level");
                    if (methodParent.IsInterface && !symbol.IsImpl)
                        Reject(symbol.Token, "Method must be 'impl'");
                    if (!methodParent.IsInterface && !methodParent.IsModule)
                        Reject(symbol.Token, "Method must be scoped at the at the module level");

                    // TBD: Static may appear in extension methods
                    //if (symbol.Parent.Parent is SymModule && symbol.Qualifiers.Contains("static"))
                    //    Reject(symbol.Token, "Methods in a module may not have the static qualifier");
                }
                else if (symbol.IsMethodParam)
                {
                    if (symbol.LookupName == symbol.Parent.Token.Name)
                        Reject(symbol.Token, "Must not be same name as method");
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent); // Skip containing type or method
                    CheckTypeName(symbol.Token, symbol.TypeName);
                }
                else if (symbol.IsField)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent.Parent);
                    CheckTypeName(symbol.Token, symbol.TypeName);
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
                while (scope.LookupName != "")
                {
                    if (scope.TryGetPrimary(token.Name, out var s)
                            && s.IsTypeParam)
                        if (!token.Error)
                            Reject(token, "Must not be same name as a type parameter in any enclosing scope");
                    scope = scope.Parent;
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

    }
}
