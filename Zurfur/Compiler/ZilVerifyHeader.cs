using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{
    // TBD: Move most compiler error checking here
    class ZilVerifyHeader
    {

        // TBD: Move from `ResolveMethod`:
        //      No overloading functions with generic type arguments
        //      Parameters may not be identical types

        public static void VerifyHeader(SymbolTable symbols)
        {
            symbols.VisitAll((symbol) =>
            {
                if (symbol.Name == "")
                    return;
                if (symbol.Name == symbol.Parent.Name)
                {
                    if (!symbol.Token.Error)
                        symbol.Token.AddError("Must not be same name as parent scope");
                }
                else if (symbol is SymTypeParam || symbol is SymMethodParam)
                {
                    var parent = symbol.Parent;
                    if (parent.Name == "" || parent is SymNamespace)
                        return;
                    parent = parent.Parent;

                    while (parent.Name != "" && !(parent is SymNamespace))
                    {
                        if (parent.Children.TryGetValue(symbol.Name, out var s)
                                && s is SymTypeParam)
                            if (!symbol.Token.Error)
                                symbol.Token.AddError("Must not be same name as a type parameter in any parent scope");
                        parent = parent.Parent;
                    }
                }
                else if (symbol is SymField)
                {
                    if (symbol.Parent is SymNamespace)
                        symbol.Token.AddError("A namespace may not directly contain fields");
                    
                }
            });


        }


    }
}
