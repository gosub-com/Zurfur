using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class ZilReport
    {
        /// <summary>
        /// This may be called after all steps have been completed.
        /// </summary>
        static public List<string> GenerateReport(Dictionary<string, SyntaxFile> mFiles, SymbolTable mSymbols)
        {
            var headerFile = new List<string>();

            ShowErrors();
            ShowCounts();
            ShowOverview();
            ShowTypes();
            return headerFile;

            void ShowErrors()
            {
                // Count errors
                var errors = new Dictionary<string, int>();
                int unknownErrors = 0;
                int totalErrors = 0;
                foreach (var file in mFiles.Values)
                {
                    foreach (var token in file.Lexer)
                    {
                        if (token.Error)
                        {
                            var foundError = false;
                            foreach (var error in token.GetInfos<TokenError>())
                            {
                                foundError = true;
                                var name = error.GetType().Name.ToString();
                                if (errors.ContainsKey(name))
                                    errors[name] += 1;
                                else
                                    errors[name] = 1;
                                totalErrors++;
                            }
                            if (!foundError)
                            {
                                unknownErrors++;
                                totalErrors++;
                            }
                        }
                    }
                    if (unknownErrors != 0)
                        errors["Unknown"] = unknownErrors;
                }

                // Report errors
                if (totalErrors == 0)
                {
                    headerFile.Add("SUCCESS!  No Errors found");
                }
                else
                {
                    headerFile.Add("FAIL!  " + totalErrors + " errors found!");
                    foreach (var error in errors)
                    {
                        headerFile.Add("    " + error.Key + ": " + error.Value);
                    }
                }
                headerFile.Add("");
            }

            void ShowCounts()
            {
                // Count symbols
                int count = 0;
                int types = 0;
                int simpleTypes = 0;
                int genericTypes = 0;
                int typeParams = 0;
                int methodGroups = 0;
                int methods = 0;
                int methodParams = 0;
                int fields = 0;
                mSymbols.VisitAll((sym) =>
                {
                    count++;
                    if (sym.GetType() == typeof(SymType))
                    {
                        types++;
                        var t = sym as SymType;
                        var numGeneric = t.FindChildren<SymTypeParam>().Count;
                        if (numGeneric == 0)
                            simpleTypes++;
                        else
                            genericTypes++;
                    }
                    if (sym.GetType() == typeof(SymTypeParam))
                        typeParams++;
                    if (sym.GetType() == typeof(SymMethod))
                        methods++;
                    if (sym.GetType() == typeof(SymMethodGroup))
                        methodGroups++;
                    if (sym.GetType() == typeof(SymMethodParam))
                        methodParams++;
                    if (sym.GetType() == typeof(SymField))
                        fields++;
                });

                headerFile.Add("SYMBOLS: " + count);
                headerFile.Add($"    Types: {types} ({simpleTypes} non-generic, {genericTypes} generic)");
                headerFile.Add($"    Type parameters: {typeParams}");
                headerFile.Add($"    Methods: {methods} ({methodParams} parameters, {methodGroups} groups)");
                headerFile.Add($"    Fields: {fields}");
                headerFile.Add("");
            }

            void ShowOverview()
            {
                // Get namespaces and all symbols
                var namespaces = new List<string>();
                mSymbols.VisitAll((s) =>
                {
                    if (s is SymNamespace n)
                        namespaces.Add(n.FullName);
                });
                namespaces.Sort((a, b) => a.CompareTo(b));

                headerFile.Add("");
                headerFile.Add("Namespaces:");
                foreach (var ns in namespaces)
                    headerFile.Add("    " + ns);
                headerFile.Add("");

            }

            void ShowTypes()
            {
                var ds = SymbolTable.GetSymbols(mSymbols.Root);
                var ls = new List<string>(ds.Keys);
                ls.Sort((a, b) => a.CompareTo(b));

                headerFile.Add("SYMBOLS:");
                foreach (var s in ls)
                {
                    var symbol = ds[s];

                    if (symbol is SymMethodGroup)
                        continue;
                    if (symbol is SymMethodParam)
                        continue;
                    if (symbol is SymTypeParam)
                        continue;
                    if (symbol is SymNamespace)
                        continue;
                    if (symbol is SymField)
                        continue;

                    headerFile.Add($"    {symbol.Kind}: {s}");

                    // Show method parameters under the method
                    if (symbol is SymMethod)
                    {
                        if (symbol.Qualifiers.Length != 0)
                            headerFile.Add($"        QUAL: {string.Join(",", symbol.Qualifiers)}");
                        foreach (var param in symbol.Children.Values)
                        {
                            if (param is SymTypeParam stp)
                                headerFile.Add($"        TYPE PARAM: {stp.Name}");
                            if (param is SymMethodParam smp)
                                headerFile.Add($"        {(smp.IsReturn ? " OUT" : "  IN")}: {smp.Name} {smp.TypeName}");
                        }
                    }

                    // Show type fields under the type
                    if (symbol is SymType)
                    {
                        foreach (var f in symbol.Children.Values)
                        {
                            if (f is SymField sf)
                            {
                                var qual = "";
                                if (sf.Qualifiers.Contains("const"))
                                    qual = "CONST ";
                                else if (sf.Qualifiers.Contains("static"))
                                    qual = "STATIC ";
                                headerFile.Add($"         @{sf.Name} {qual} {sf.TypeName}");
                            }
                        }

                    }

                }
            }


        }

    }
}
