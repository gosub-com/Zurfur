using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{
    class PackageGen
    {
        public PackageHeaderJson MakeHeaderFile(Symbol root, bool isPublic)
        {
            if (!(root is SymModule))
                throw new Exception("Root needs to be a module");

            // TBD: Replace all the defaults later
            var header = new PackageHeaderJson();
            header.BuildDate = DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            AddPackageSymbols(header.Symbols, root, isPublic);

            return header;
        }

        void Require(bool condition, string message)
        {
            Debug.Assert(condition);
            if (!condition)
                throw new Exception(message);
        }

        void AddPackageSymbols(List<PackageSymbolJson> packSyms,  Symbol symbol, bool isPublic)
        {
            if (symbol is SymMethodGroup || symbol.Name == "")
            {
                // Hop over method groups, which are internal to the compiler
                foreach (var child in symbol.Children.Values)
                    AddPackageSymbols(packSyms, child, isPublic);
                return;
            }
            if (isPublic && symbol is SymField)
                return; // Don't include fields in public header file

            if (isPublic 
                    && !symbol.Qualifiers.Contains("pub") 
                    //&& !(symbol is SymModule)
                    && !(symbol is SymMethodParam)
                    && !(symbol is SymTypeParam))
                return; // Ignore private symbole (except moduels, and parameters)


            var packSym = new PackageSymbolJson();

            if (symbol is SymMethod)
            {
                // Use group name (the arguments contain the parameter type names)
                packSym.Name = symbol.Parent.FullNameWithoutParent;
            }
            else
            {
                packSym.Name = symbol.FullNameWithoutParent;
            }

            packSym.Qualifiers = symbol.Qualifiers.Length == 0 ? null : symbol.Qualifiers;

            if (isPublic && symbol.Comments != "")
                packSym.Comments = symbol.Comments;

            if (symbol is SymMethodParam symParam)
            {
                Debug.Assert(packSym.Qualifiers == null);
                packSym.Qualifiers = symParam.IsReturn ? new string[] {"returns" } : new string[] { "param"};
                packSym.Type = symParam.TypeName;
            }
            if (symbol is SymTypeParam)
            {
                Debug.Assert(packSym.Qualifiers == null);
                packSym.Qualifiers = new string[] { "type_param" };
            }
            if (symbol is SymField symField)
            {
                packSym.Type = symField.TypeName;
            }

            packSyms.Add(packSym);

            // Save all child symbols
            if (symbol.Children.Count != 0)
            {
                packSym.Symbols = new List<PackageSymbolJson>();
                foreach (var child in symbol.Children.Values.OrderBy(s => s.Order))
                    AddPackageSymbols(packSym.Symbols, child, isPublic);
                if (packSym.Symbols.Count == 0)
                    packSym.Symbols = null;  // Remove field if nothing was generated
            }

        }



    }
}
