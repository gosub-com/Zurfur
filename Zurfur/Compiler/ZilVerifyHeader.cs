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

        public static void VerifyHeader(SymbolTable symbols)
        {
            //return;
            symbols.VisitAll((symbol) =>
            {
                if (symbol.Name == "")
                    return;
                if (symbol.Name == symbol.Parent.Name)
                {
                    if (!symbol.Token.Error)
                        Reject(symbol.Token, "Must not be same name as parent scope");
                }
                else if (symbol is SymTypeParam)
                {
                    RejectDuplicateTypeParameterName(symbol, symbol.Parent.Parent); // Skip containing type or method
                }
                else if (symbol is SymMethodParam methodParam)
                {
                    if (symbol.Name == symbol.Parent.Parent.Name)
                        Reject(symbol.Token, "Most not be same name as method");
                    RejectDuplicateTypeParameterName(symbol, symbol.Parent.Parent); // Skip containing type or method
                    CheckType(methodParam.Token, methodParam.TypeName);
                }
                else if (symbol is SymMethodGroup methodGroup)
                {
                    RejectDuplicateTypeParameterName(symbol, symbol.Parent.Parent); // Skip containing type or method
                    RejectIllegalOverloads(methodGroup);
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
                            Reject(symbol.Token, "Must not be same name as a type parameter in any parent scope");
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

                var hasGenericParameters = false;
                foreach (var child in methodGroup.Children.Values)
                    if (child.GenericParamCount() != 0)
                    {
                        hasGenericParameters = true;
                        break;
                    }
                if (!hasGenericParameters)
                    return;

                foreach (var child in methodGroup.Children.Values)
                {
                    if (child.GenericParamCount() == 0)
                        Reject(child.Token, "There is a generic method with the same name.  Generic methods may not be overloaded.");
                    else
                        Reject(child.Token, "There is a method with the same name.  Generic methods may not be overloaded.");
                }
            }

            // Does not reject if there is already an error there
            void Reject(Token token, string message)
            {
                if (!token.Error)
                {
                    if (token.GetInfo<VerifySuppressError>() == null)
                    {
                        token.AddError(new VerifyHeaderError(message));
                    }
                    else
                    {
                        token.AddWarning(new TokenWarn("(Error suppressed): " + message));
                    }
                }
            }

        }


    }
}
