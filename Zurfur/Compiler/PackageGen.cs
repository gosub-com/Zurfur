using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Compiler
{
    class PackageGen
    {
        public PackageHeaderJson MakeHeaderFile(Symbol root, bool includePrivate)
        {
            if (!(root is SymModule))
                throw new Exception("Root needs to be a module");

            // TBD: Replace all the defaults later
            var header = new PackageHeaderJson();
            header.BuildDate = DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            var files = new Dictionary<string, int>();
            header.Symbols = MakeSymbols(root, files, includePrivate);
            return header;
        }

        PackageSymbolJson MakeSymbols(Symbol sym, Dictionary<string, int> files, bool includePrivate)
        {
            var packSym = new PackageSymbolJson();
            packSym.Name = sym.FullNameWithoutParent;
            packSym.Qualifiers = sym.Qualifiers;
            packSym.Comments = sym.Comments;
            if (sym.HasToken && sym.File != "")
            {                
                if (!files.TryGetValue(sym.File, out var fileIndex))
                {
                    fileIndex = files.Count;
                    files[sym.File] = fileIndex;
                }
                packSym.FI = fileIndex;
                packSym.X = sym.Token.X;
                packSym.Y = sym.Token.Y;
            }

            foreach (var s in sym.Children)
            {
                // TBD: All modules are public right now.
                if (!includePrivate && !s.Value.Qualifiers.Contains("pub") && !(s.Value is SymModule))
                    continue;
                packSym.Symbols.Add(MakeSymbols(s.Value, files, includePrivate));
            }

            if (sym is SymMethodParam symMethod)
                packSym.TypeName = symMethod.TypeName;
            else if (sym is SymField symField)
                packSym.TypeName = symField.TypeName;

            return packSym;
        }


    }
}
