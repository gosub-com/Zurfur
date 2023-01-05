using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
 
    /// <summary>
    /// Translation from internal compiler strctures to public package format.
    /// </summary>
    static class PackageLoader
    {


        /// <summary>
        /// Save the symbol table as a list of modules, types, and
        /// methods at the top level.  Fields, parameters, etc. are children.
        /// </summary>
        static public List<PackageSymbolJson> Save(this SymbolTable table, bool onlyPublic)
        {
            var packSyms = new List<PackageSymbolJson>();
            Save(table.Root, null, packSyms, onlyPublic);

            // TBD: After `Load` is working, verify the symbol table is OK
            //if (!onlyPublic)
            //    DebugVerifySymbolTables(table, packSyms);

            return packSyms;
        }

        /// <summary>
        /// Save to json transport
        /// </summary>
        static void Save(Symbol symbol, PackageSymbolJson parent, List<PackageSymbolJson> packSyms, bool onlyPublic)
        {
            if (onlyPublic && symbol.IsField
                || onlyPublic
                    && !symbol.Qualifiers.HasFlag(SymQualifiers.Pub)
                        // TBD: Add "pub" to bits, and remove this
                    && !symbol.IsModule
                    && !symbol.IsFunParam
                    && !symbol.IsTypeParam)
            {
                // Ignore private symbols (except parmeters).
                // Ignore fields since they are always private
                return;
            }

            var ps = new PackageSymbolJson();
            ps.Tags = symbol.QualifiersStr();
            ps.Type = symbol.TypeName == "" ? null : symbol.TypeName;
            ps.Constraints = symbol.Constraints;
            if (onlyPublic && symbol.Comments != "")
                ps.Comments = symbol.Comments;

            if (symbol.IsModule || symbol.IsType)
            {
                ps.Name = symbol.FullName;
                packSyms.Add(ps);
                foreach (var s in symbol.Children)
                    Save(s, ps, packSyms, onlyPublic);
            }
            else if (symbol.IsFun)
            {
                ps.Name = symbol.FullName;
                packSyms.Add(ps);
                foreach (var s in symbol.Children)
                    Save(s, ps, packSyms, onlyPublic);

                CheckMethodName(ps, symbol);
            }
            else if (symbol.IsTypeParam || symbol.IsFunParam || symbol.IsField)
            {
                ps.Name = symbol.LookupName;
                Debug.Assert(parent != null);
                //if (symbol.IsMethodParam)
                //    ps.Type = null;  // Reconstructed from method name
                if (parent.Symbols == null)
                    parent.Symbols = new List<PackageSymbolJson>();
                parent.Symbols.Add(ps);
                Debug.Assert(symbol.ChildrenCount == 0);
            }
            else
                Debug.Assert(false);

        }

        [Conditional("DEBUG")]
        static void CheckMethodName(PackageSymbolJson ps, Symbol sym)
        {
            var paramIn = new List<string>();
            var paramOut = new List<string>();
            if (ps.Symbols != null)
            {
                foreach (var p in ps.Symbols)
                {
                    if (p.Tags.Contains("method_param"))
                    {
                        if (p.Tags.Contains("out"))
                            paramOut.Add(p.Type);
                        else
                            paramIn.Add(p.Type);
                    }
                }
            }
        }

        /// <summary>
        /// Load symbol table from json transport
        /// </summary>
        static public SymbolTable Load(this SymbolTable table, List<PackageSymbolJson> packSyms)
        {
            throw new Exception("Not implemented yet");
        }


        /// <summary>
        /// Given a symbol table and its serialization,
        /// verify that re-loading it works properly.
        /// </summary>
        [Conditional("DEBUG")]
        private static void DebugVerifySymbolTables(SymbolTable table, List<PackageSymbolJson> symbols)
        {
            foreach (var s in table.LookupSymbols)
                if (s.HasToken && s.Token.Error)
                {
                    Console.WriteLine("Not verifying symbols because of errors in source");
                    return; // Only run this when there are no errors
                }

            var reloadSymbols = new SymbolTable().Load(symbols);

            foreach (var savedSym in table.LookupSymbols)
            {
                var loadedSym = reloadSymbols.Lookup(savedSym.FullName);
                if (loadedSym == null)
                {
                    // TBD: Missing symbols when there are compilation errors are normal
                    Console.WriteLine($"Internal consistency check: '{savedSym.FullName}' not found");
                    Debug.Assert(false);
                    continue;
                }
                if (loadedSym.TypeName != savedSym.TypeName)
                {
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}', but loaded '{loadedSym.FullName}'");
                    Debug.Assert(false);
                }
                if (loadedSym.GetType() != savedSym.GetType())
                {
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' type doesn't match");
                    Debug.Assert(false);
                }
                if (loadedSym.Order != savedSym.Order)
                {
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' order doesn't match");
                    Debug.Assert(false);
                }
                if (loadedSym.Qualifiers != savedSym.Qualifiers)
                {
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' tags don't match");
                    Debug.Assert(false);
                }
                if (loadedSym.Kind != savedSym.Kind)
                {
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' kind doesn't match");
                    Debug.Assert(false);
                }
                if (loadedSym.ChildrenCount != savedSym.ChildrenCount)
                {
                    // NOTE: Certain errors in the source code trigger this,
                    //       but it's OK since we won't be saving the table in that case.
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' children count doesn't match");
                    Debug.Assert(false);
                }
            }
        }



    }
}
