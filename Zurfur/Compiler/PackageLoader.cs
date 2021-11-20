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
        /// Save symbol table to json transport
        /// </summary>
        static public List<PackageSymbolJson> Save(this SymbolTable table, bool onlyPublic)
        {
            var packSyms = new List<PackageSymbolJson>();
            SaveAdd(table.Root, packSyms, onlyPublic);
            return packSyms;
        }

        /// <summary>
        /// Save to json transport
        /// </summary>
        static void SaveAdd(Symbol symbol, List<PackageSymbolJson> packSyms, bool onlyPublic)
        {
            if (symbol.IsIntrinsic)
                return;

            if (symbol is SymMethodGroup || symbol.Name == "")
            {
                // Hop over method groups, which are internal to the compiler
                foreach (var child in symbol.Children.Values)
                    SaveAdd(child, packSyms, onlyPublic);
                return;
            }

            if (    onlyPublic && symbol is SymField
                || onlyPublic
                    && !symbol.Qualifiers.Contains("pub")
                    && !(symbol is SymMethodParam)
                    && !(symbol is SymTypeParam))
            {
                // Ignore private symbols for header file.
                // Fields are always private.
                // Parameters are public if the type or method is also public.
                return; 
            }

            var packSym = new PackageSymbolJson();
            packSym.Name = symbol is SymMethod ? symbol.Parent.Name : symbol.Name;
            packSym.Tags = symbol.Qualifiers.Length == 0 ? null : symbol.Qualifiers;
            packSym.Type = symbol.TypeName == "" ? null : symbol.TypeName;
            if (onlyPublic && symbol.Comments != "")
                packSym.Comments = symbol.Comments;

            packSyms.Add(packSym);

            // Save all child symbols
            if (symbol.Children.Count != 0)
            {
                packSym.Symbols = new List<PackageSymbolJson>();
                foreach (var child in symbol.Children.Values.OrderBy(s => s.Order))
                    SaveAdd(child, packSym.Symbols, onlyPublic);
                if (packSym.Symbols.Count == 0)
                    packSym.Symbols = null;  // Remove field if nothing was generated
            }
        }

        /// <summary>
        /// Load symbol table from json transport
        /// </summary>
        static public SymbolTable Load(this SymbolTable table, List<PackageSymbolJson> packSyms)
        {
            foreach (var child in packSyms)
                LoadAdd(table, table.Root, child);
            table.GenerateLookup();
            return table;
        }

        /// <summary>
        /// Load from json transport
        /// </summary>
        static void LoadAdd(SymbolTable table, Symbol parent, PackageSymbolJson packSym)
        {
            Symbol newSymbol;
            var name = packSym.Name;

            if (packSym.Tags == null)
                packSym.Tags = Array.Empty<string>();

            if (packSym.Tags.Contains("module"))
                newSymbol = new SymModule(parent, name);
            else if (packSym.Tags.Contains("type")
                        || packSym.Tags.Contains("interface")
                        || packSym.Tags.Contains("enum"))
                newSymbol = new SymType(parent, "", new Token(name));
            else if (packSym.Tags.Contains("field"))
                newSymbol = new SymField(parent, "", new Token(name));
            else if (packSym.Tags.Contains("ptype"))
                newSymbol = new SymTypeParam(parent, "", new Token(name));
            else if (packSym.Tags.Contains("param"))
                newSymbol = new SymMethodParam(parent, "", new Token(name), packSym.Tags.Contains("return"));
            else if (packSym.Tags.Contains("method"))
            {
                // Find or create group
                if (!parent.Children.TryGetValue(name, out var group))
                {
                    group = new SymMethodGroup(parent, name);
                    table.AddOrReject(group);
                }
                // Reconstruct method name
                var numTypeArgs = 0;
                var param = new List<string>();
                var returns = new List<string>();
                if (packSym.Symbols != null)
                    foreach (var s in packSym.Symbols)
                    {
                        if (s.Tags.Contains("return"))
                            returns.Add(s.Type);
                        else if (s.Tags.Contains("param"))
                            param.Add(s.Type);
                        else if (s.Tags.Contains("ptype"))
                            numTypeArgs++;
                        else
                            throw new Exception("Expecting symbol method kind to be 'param', 'return', or 'ptype'");
                    }
                var methodArgName = (numTypeArgs == 0 ? "" : $"`{numTypeArgs}")
                        + "(" + string.Join(",", param)
                        + ")(" + string.Join(",", returns) + ")";
                newSymbol = new SymMethod(group, "", new Token(name), methodArgName);
            }
            else
            {
                Debug.Assert(false);
                throw new Exception($"Invalid symbol kind '$child.kind'");
            }

            newSymbol.Qualifiers = packSym.Tags == null ? Array.Empty<string>() : packSym.Tags;
            newSymbol.TypeName = packSym.Type == null ? "" : packSym.Type;
            newSymbol.Comments = packSym.Comments == null ? "" : packSym.Comments;
            if (!table.AddOrReject(newSymbol))
                throw new Exception($"Duplicate symbol '{name}' found with parent ${parent}");

            // Add children
            if (packSym.Symbols != null)
                foreach (var s in packSym.Symbols)
                    LoadAdd(table, newSymbol, s);
        }

        /// <summary>
        /// Experiment: Save the symbol table as a dictionary of symbols.
        /// It makes the json easier to read, but then we need to parse symbols
        /// when they are loaded.  Since we need to parse them anyway, maybe
        /// we'll come back to this later. 
        /// </summary>
        static public Dictionary<string, PackageSymbolJson> SaveMapExperiment(this SymbolTable table, bool onlyPublic)
        {
            var packSyms = new Dictionary<string, PackageSymbolJson>();
            SaveAddMapExperiment(table.Root, packSyms, onlyPublic);
            return packSyms;
        }

        /// <summary>
        /// Save to json transport
        /// </summary>
        static void SaveAddMapExperiment(Symbol symbol, Dictionary<string, PackageSymbolJson> packSyms, bool onlyPublic)
        {
            if (symbol.IsIntrinsic)
                return;

            if (symbol is SymMethodGroup)
            {
                // Hop over method groups, which are internal to the compiler
                foreach (var s in symbol.Children.Values)
                    SaveAddMapExperiment(s, packSyms, onlyPublic);
                return;
            }

            if (onlyPublic && symbol is SymField
                || onlyPublic
                    && !symbol.Qualifiers.Contains("pub")
                    && !(symbol is SymModule)  // TBD: Allow private moduels
                    && !(symbol is SymMethodParam)
                    && !(symbol is SymTypeParam))
            {
                // Ignore private symbols (except parmeters).
                // Ignore fields since they are always private
                return;
            }

            var ps = new PackageSymbolJson();
            ps.Tags = symbol.Qualifiers.Length == 0 ? null : symbol.Qualifiers;
            ps.Type = symbol.TypeName == "" ? null : symbol.TypeName;
            if (onlyPublic && symbol.Comments != "")
                ps.Comments = symbol.Comments;

            if (symbol is SymModule || symbol is SymType || symbol is SymMethod)
            {
                packSyms[symbol.FullName] = ps;
            }
            else if (symbol is SymTypeParam || symbol is SymMethodParam || symbol is SymField)
            {
                if (symbol is SymMethodParam)
                    ps.Type = null;  // Reconstructed from method name
                ps.Name = symbol.Name;
                var s = packSyms[symbol.Parent.FullName];
                if (s.Symbols == null)
                    s.Symbols = new List<PackageSymbolJson>();
                s.Symbols.Add(ps);
            }
            else
                Debug.Assert(false);

            foreach (var s in symbol.Children.Values)
                SaveAddMapExperiment(s, packSyms, onlyPublic);
        }



    }
}
