using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Gosub.Zurfur.Lex;


namespace Gosub.Zurfur.Compiler
{
    class ZilReport
    {
        /// <summary>
        /// This may be called after all steps have been completed.
        /// </summary>
        static public void GenerateReport(List<string> headerFile, SymbolTable symbols, Lexer []files)
        {
            ShowErrors();
            ShowCounts();
            ShowOverview();
            ShowTypes();
            return;

            void ShowErrors()
            {
                // Count errors and show first 10
                int MAX_ERROR_MESSAGES = 1000;
                int totalErrors = 0;
                List<string> errorMessages = new List<string>();
                foreach (var lexer in files)
                {
                    foreach (var token in lexer)
                    {
                        if (!token.Error)
                            continue;
                        totalErrors++;
                        if (errorMessages.Count > MAX_ERROR_MESSAGES)
                            continue;

                        var errors = token.GetInfos<TokenError>();
                        var errorMessage = Path.GetFileName(token.Path)
                            + $" [{token.Y + 1}, {token.X + 1}] \"{token}\"";

                        if (errors.Length == 0)
                            errorMessage += "Unknown!";
                        else
                            foreach (var error in errors)
                                errorMessage += ": " + error.GetType().Name + " - " + error.Message;
                        errorMessages.Add(errorMessage);
                    }
                }

                // Report errors
                if (totalErrors == 0)
                {
                    headerFile.Add("SUCCESS!  No Errors found.");
                }
                else
                {
                    headerFile.Add($"FAIL!  {totalErrors} errors found.");
                    foreach (var error in errorMessages)
                        headerFile.Add("    " + error);
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
                int typeParams = 0;
                int methods = 0;
                int methodParams = 0;
                int fields = 0;
                foreach (var sym in symbols.Symbols)
                {
                    count++;
                    if (sym.IsType)
                    {
                        types++;
                        var numGeneric = sym.GenericParamCount();
                        if (numGeneric == 0)
                            typesNonGeneric++;
                        else
                            typesGeneric++;
                    }
                    if (sym.IsTypeParam)
                        typeParams++;
                    if (sym.IsMethod)
                        methods++;
                    if (sym.IsMethodParam)
                        methodParams++;
                    if (sym.IsField)
                        fields++;
                };

                headerFile.Add("SYMBOLS: " + count);
                headerFile.Add($"    Types: {types} ({typesNonGeneric} non-generic, {typesGeneric} generic)");
                headerFile.Add($"    Specializations: {symbols.SpecializedSymbols.Count} (generated from generics)");
                headerFile.Add($"    Type parameters: {typeParams}");
                headerFile.Add($"    Methods: {methods} ({methodParams} parameters)");
                headerFile.Add($"    Fields: {fields}");
                headerFile.Add("");
            }

            void ShowOverview()
            {
                // Get namespaces and all symbols
                var namespaces = new List<string>();
                foreach (var s in symbols.Symbols)
                {
                    if (s.IsModule)
                        namespaces.Add(s.FullName);
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
                headerFile.Add("SYMBOLS:");
                var syms = new List<Symbol>(symbols.Symbols);
                syms.Sort((a, b) => Compare(a.FullName, b.FullName));
                foreach (var s in syms)
                    headerFile.Add($"{s.KindName,16}: {s.FullName}");

                headerFile.Add("");
                headerFile.Add("");
                headerFile.Add("SPECIALIZED:");
                var special = new List<Symbol>(symbols.SpecializedSymbols);
                special.Sort((a, b) => Compare(a.FullName, b.FullName));
                foreach (var s in special)
                    headerFile.Add($"    {s.FullName}");

            }


        }

    }
}
