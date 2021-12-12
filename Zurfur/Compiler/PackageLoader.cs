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

            if (!onlyPublic)
                DebugVerifySymbolTables(table, packSyms);

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
            if (Debugger.IsAttached)
                packSym.NameDebugFull = symbol.FullName;
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
        static void LoadAdd(SymbolTable table, Symbol symbol, PackageSymbolJson packSym)
        {
            Symbol newSymbol;
            var name = packSym.Name;

            if (packSym.Tags == null)
                packSym.Tags = Array.Empty<string>();

            if (packSym.Tags.Contains("module"))
                newSymbol = new SymModule(symbol, name);
            else if (packSym.Tags.Contains("type")
                        || packSym.Tags.Contains("trait")
                        || packSym.Tags.Contains("trait_impl")
                        || packSym.Tags.Contains("enum"))
                newSymbol = new SymType(symbol, "", new Token(name));
            else if (packSym.Tags.Contains("field"))
                newSymbol = new SymField(symbol, "", new Token(name));
            else if (packSym.Tags.Contains("type_param") || packSym.Tags.Contains("type_param_associated"))
                newSymbol = new SymTypeParam(symbol, "", new Token(name));
            else if (packSym.Tags.Contains("param"))
                newSymbol = new SymMethodParam(symbol, "", new Token(name), false);
            else if (packSym.Tags.Contains("param_return"))
                newSymbol = new SymMethodParam(symbol, "", new Token(name), true);
            else if (packSym.Tags.Contains("method"))
            {
                // Find or create group
                if (!symbol.Children.TryGetValue(name, out var group))
                {
                    group = new SymMethodGroup(symbol, name);
                    table.AddOrReject(group);
                }
                // Reconstruct method name
                var numTypeArgs = 0;
                var param = new List<string>();
                var returns = new List<string>();
                if (packSym.Symbols != null)
                    foreach (var s in packSym.Symbols)
                    {
                        if (s.Tags.Contains("param_return"))
                            returns.Add(s.Type);
                        else if (s.Tags.Contains("param"))
                            param.Add(s.Type);
                        else if (s.Tags.Contains("type_param"))
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
                throw new Exception($"Duplicate symbol '{name}' found with parent ${symbol}");

            // Add children
            if (packSym.Symbols != null)
                foreach (var s in packSym.Symbols)
                    LoadAdd(table, newSymbol, s);
        }

        [Conditional("DEBUG")]
        private static void DebugVerifySymbolTables(SymbolTable table, List<PackageSymbolJson> symbols)
        {
            foreach (var s in table.Symbols)
                if (s.HasToken && s.Token.Error)
                {
                    Console.WriteLine("Not verifying symbols because of errors in source");
                    return; // Only run this when there are no errors
                }

            var reloadSymbols = new SymbolTable().Load(symbols);
            var savedTable = table.GetSymbols();
            var loadedTable = reloadSymbols.GetSymbols();

            foreach (var savedSym in savedTable.Values)
            {
                if (savedSym.IsIntrinsic)
                    continue;
                if (!loadedTable.TryGetValue(savedSym.FullName, out var loadedSym))
                {
                    // Missing symbols when there are compilation errors are normal
                    if (savedSym is SymMethodGroup 
                            && (savedSym.Parent.Name == "$extension" || savedSym.Parent.Token.Error))
                        continue;
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
                if (!loadedSym.Qualifiers.SequenceEqual(savedSym.Qualifiers))
                {
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' tags don't match");
                    Debug.Assert(false);
                }
                if (loadedSym.Kind != savedSym.Kind)
                {
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' kind doesn't match");
                    Debug.Assert(false);
                }
                if (loadedSym.Children.Count != savedSym.Children.Count)
                {
                    // NOTE: Certain errors in the source code trigger this,
                    //       but it's OK since we won't be saving the table in that case.
                    Console.WriteLine($"Internal consistency check: Saved '{savedSym.FullName}' children count doesn't match");
                    Debug.Assert(false);
                }
            }
        }

        /// <summary>
        /// Experiment: Save the symbol table as a list of modules, types, and
        /// methods at the top level.  Fields, parameters, etc. are children.
        /// It makes the json easier to read, but then we need to parse symbols
        /// when they are loaded.  Since we need to parse them anyway, maybe
        /// we'll come back to this later. 
        /// </summary>
        static public List<PackageSymbolJson> SaveFlattenedExperiment(this SymbolTable table, bool onlyPublic)
        {
            var packSyms = new List<PackageSymbolJson>();
            SaveAddFlattenedExperiment(table.Root, null, packSyms, onlyPublic);
            return packSyms;
        }

        /// <summary>
        /// Save to json transport
        /// </summary>
        static void SaveAddFlattenedExperiment(Symbol symbol, PackageSymbolJson parent, List<PackageSymbolJson> packSyms, bool onlyPublic)
        {
            if (symbol.IsIntrinsic)
                return;

            if (symbol is SymMethodGroup)
            {
                // Hop over method groups, which are internal to the compiler
                // NOTE: Nothing directly under the group should be using the parent
                foreach (var s in symbol.Children.Values)
                    SaveAddFlattenedExperiment(s, null, packSyms, onlyPublic);
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
            ps.Constraints = symbol.Constraints.Count == 0 ? null : symbol.Constraints;
            if (onlyPublic && symbol.Comments != "")
                ps.Comments = symbol.Comments;

            if (symbol is SymModule || symbol is SymType || symbol is SymMethod)
            {
                ps.Name = symbol.FullName;
                packSyms.Add(ps);
                foreach (var s in symbol.Children.Values)
                    SaveAddFlattenedExperiment(s, ps, packSyms, onlyPublic);
            }
            else if (symbol is SymTypeParam || symbol is SymMethodParam || symbol is SymField)
            {
                Debug.Assert(parent != null);
                if (symbol is SymMethodParam)
                    ps.Type = null;  // Reconstructed from method name
                ps.Name = symbol.Name;
                if (parent.Symbols == null)
                    parent.Symbols = new List<PackageSymbolJson>();
                parent.Symbols.Add(ps);
                Debug.Assert(symbol.Children.Count == 0);
            }
            else
                Debug.Assert(false);

        }


    }
}
