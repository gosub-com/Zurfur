using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class VerifyError : TokenError
    {
        public VerifyError(string message) : base(message) { }
    }

    // Some compiler error checking can be done here and reported directly
    // back to the user.  However, the compiler can generate better error
    // messages at a more exact location (e.g. the verifier can only mark
    // the symbol it has access to, such as the field or parameter, whereas
    // the compiler can show an error directly at the type name token that
    // failed).
    //
    // Therefore, much of this may be redundant but all of it is necessary
    // to ensure packages are correct.
    class ZilVerifyHeader
    {

        public static void VerifyHeader(SymbolTable symbols)
        {
            symbols.VisitAll((symbol) =>
            {
                if (symbol.Name == "")
                    return;
                if (symbol.Name == symbol.Parent.Name)
                {
                    if (!symbol.Token.Error)
                        Reject(symbol.Token, "Must not be same name as parent scope");
                }
                else if (symbol is SymTypeParam || symbol is SymMethodParam)
                {
                    // Verify no enclosing scope has the same type name
                    var parent = symbol.Parent.Parent; // Skip containing type or method
                    while (!(parent is SymNamespace))
                    {
                        if (parent.Children.TryGetValue(symbol.Name, out var s)
                                && s is SymTypeParam)
                            if (!symbol.Token.Error)
                                Reject(symbol.Token, "Must not be same name as a type parameter in any parent scope");
                        parent = parent.Parent;
                    }
                }
                else if (symbol is SymField field)
                {
                    if (symbol.Parent is SymNamespace)
                        Reject(symbol.Token, "A namespace may not directly contain fields");
                    CheckType(field.Token, field.TypeName);
                }
            });

            void CheckType(Token token, string typeName)
            {
                var s = symbols.LookupType(typeName);
                if (s == null)
                {
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
                    var genericParams = s.GenericParamCount();
                    var genericArgs = 0;
                    if (genericParams != genericArgs)
                        Reject(token, $"The type '{typeName}' requires {genericParams} generic type parameters, but {genericArgs} were supplied");
                }
                if (s is SymParameterizedType ptype)
                {
                    var genericParams = s.GenericParamCount();
                    var genericArgs = ptype.Params.Length + ptype.Returns.Length;
                    if (genericParams != genericArgs)
                        Reject(token, $"The type '{typeName}' requires {genericParams} generic type parameters, but {genericArgs} were supplied");
                    foreach (var t in ptype.Params)
                        CheckType(token, t.GetFullName());
                    foreach (var t in ptype.Returns)
                        CheckType(token, t.GetFullName());
                }

            }

            // Does not reject if there is already an error there
            void Reject(Token token, string message)
            {
                if (!token.Error)
                    token.AddError(new VerifyError(message));
            }



        }


    }
}
