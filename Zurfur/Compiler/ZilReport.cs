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
        static public void GenerateReport(List<string> headerFile, Dictionary<string, SyntaxFile> mFiles, SymbolTable mSymbols)
        {
            ShowErrors();
            ShowCounts();
            ShowOverview();
            ShowTypes();
            return;

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
                int typesNonGeneric = 0;
                int typesGeneric = 0;
                int typeSpecializations = 0;
                int typeParams = 0;
                int methodGroups = 0;
                int methods = 0;
                int methodParams = 0;
                int fields = 0;
                foreach (var sym in mSymbols.Symbols)
                {
                    count++;
                    if (sym.GetType() == typeof(SymType))
                    {
                        types++;
                        var t = sym as SymType;
                        var numGeneric = t.GenericParamCount();
                        if (numGeneric == 0)
                            typesNonGeneric++;
                        else
                            typesGeneric++;
                    }
                    if (sym.GetType() == typeof(SymSpecializedType))
                        typeSpecializations++;
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
                };

                headerFile.Add("SYMBOLS: " + count);
                headerFile.Add($"    Types: {types} ({typesNonGeneric} non-generic, {typesGeneric} generic)");
                headerFile.Add($"    Specializations: {typeSpecializations}");
                headerFile.Add($"    Type parameters: {typeParams}");
                headerFile.Add($"    Methods: {methods} ({methodParams} parameters, {methodGroups} groups)");
                headerFile.Add($"    Fields: {fields}");
                headerFile.Add("");
            }

            void ShowOverview()
            {
                // Get namespaces and all symbols
                var namespaces = new List<string>();
                foreach (var s in mSymbols.Symbols)
                {
                    if (s is SymModule n)
                        namespaces.Add(n.FullName);
                }
                namespaces.Sort((a, b) => a.CompareTo(b));

                headerFile.Add("");
                headerFile.Add("Namespaces:");
                foreach (var ns in namespaces)
                    headerFile.Add("    " + ns);
                headerFile.Add("");

            }

            // Put fields on top, classes on bottom
            int Compare(string a, string b)
            {
                a = a.Replace("/", "~").Replace("@", " ");
                b = b.Replace("/", "~").Replace("@", " ");
                return a.CompareTo(b);
            }

            void ShowTypes()
            {
                var ds = mSymbols.GetSymbols();
                var ls = new List<string>(ds.Keys);
                ls.Sort((a, b) => Compare(a, b));

                headerFile.Add("SYMBOLS:");
                foreach (var s in ls)
                {
                    var symbol = ds[s];
                    //if (symbol is SymMethodGroup)
                    //    continue;
                    headerFile.Add($"    {symbol.Kind,16}: {s}");
                }
                return;

            }


        }

    }
}
