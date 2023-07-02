using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Zurfur.Lex;

namespace Zurfur.Jit
{
    public class VerifyError : TokenError
    {
        public VerifyError(string message) : base(message) { }
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
    public static class VerifyHeader
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
                // TBD: Reject illegal overload methods
                //      Need to see though alias types
                //      (e.g. reject 'fun f(a int)' with 'fun f(a MyAliasInt)'
                //      Reject overloads with different return types
                //      Eextension methods with same name as members, etc.

                if (symbol.IsTypeParam)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent!.Parent!); // Skip containing type or method
                }
                else if (symbol.IsFun)
                {
                    CheckFunction(symbol);
                }
                else if (symbol.IsFunParam)
                {
                    // NOTE: The parameter type is checked by `CheckFunction`.
                    //       Storing parameters as children symbols instead of
                    //       named tuples is redundant, and should probably be
                    //       removed. Since we have them, do the redundant type
                    //       check here just in case (ignore module, since it's
                    //       already checked by `CheckFunction`)
                    CheckTypeName(symbol.Token, symbol.TypeName, true);
                    if (symbol.SimpleName == symbol.Parent!.Token.Name)
                        Reject(symbol.Token, "Must not be same name as method");
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent!.Parent!); // Skip containing type or method
                }
                else if (symbol.IsField)
                {
                    RejectDuplicateTypeParameterName(symbol.Token, symbol.Parent!.Parent!);
                    CheckTypeName(symbol.Token, symbol.TypeName, false);
                }
            }

            void CheckFunction(Symbol func)
            {
                if (func.Type == null
                    || !func.Type.IsTuple
                    || func.Type.TypeArgs.Length != 2)
                {
                    Reject(func.Token, "Malformed function parameters (must be tuple with two parameters)");
                    return;
                }


                // Check parameter and return types
                var p = func.FunParamTypes;
                foreach (var r in func.FunParamTypes)
                    CheckTypeName(func.Token, r.FullName, false);
                foreach (var r in func.FunReturnTypes)
                    CheckTypeName(func.Token, r.FullName, false);

                var funParent = func.Parent!;
                RejectDuplicateTypeParameterName(func.Token, funParent.Parent!); // Skip containing type or method

                if (!funParent.IsInterface && !funParent.IsModule)
                    Reject(func.Token, "Method must be scoped at the at the module level");
            }



            void CheckTypeName(Token token, string typeName, bool allowModule)
            {
                var s = symbols.Lookup(typeName);
                if (s == null)
                {
                    if (typeName.Contains('#'))
                    {
                        // TBD: Need to verify generic types
                    }
                    else if (typeName == "")
                        Reject(token, "Unresolved type");
                    else
                        Reject(token, $"Unknown type name: '{typeName}'");
                    return;
                }
                if (!(s.IsAnyType || allowModule && s.IsModule))
                    Reject(token, $"The type '{typeName}' is not a type, it is a '{s.KindName}'");
                if (s.IsModule && s.GenericParamCount() != 0)
                    Reject(token, "Module must not have generic types");

                if (s.IsType)
                {
                    var genericParams = s.GenericParamCount();
                    var genericArgsCount = s.IsSpecialized ? s.TypeArgs.Length : 0;
                    if (genericParams != genericArgsCount && !s.IsTuple)
                        Reject(token, $"The type '{typeName}' requires {genericParams} generic "
                            + $"type parameters, but {genericArgsCount} were supplied");
                    if (s.IsSpecialized)
                    {
                        foreach (var t in s.TypeArgs)
                            CheckTypeName(token, t.FullName, false);
                    }
                }
            }

            // A type parameter in any enclosing scope prevents
            // children scopes from declaring that symbol.
            void RejectDuplicateTypeParameterName(Token token, Symbol scope)
            {
                while (scope.FullName != "")
                {
                    if (scope.TryGetPrimary(token.Name, out var s)
                            && s!.IsTypeParam)
                        if (!token.Error)
                            Reject(token, "Must not be same name as a type parameter in any enclosing scope");
                    scope = scope.Parent!;
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
                        token.AddError(new VerifyError(message));
                        break;

                    case SuppressErrorMode.Warn:
                        if (token.GetInfo<VerifySuppressError>() == null)
                            token.AddError(new VerifyError(message));
                        else
                            token.AddWarning(new TokenWarn("(Error suppressed): " + message));
                        break;

                    case SuppressErrorMode.Ignore:
                        if (token.GetInfo<VerifySuppressError>() == null)
                            token.AddError(new VerifyError(message));
                        break;
                }

            }

        }

    }
}
