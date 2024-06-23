using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Gosub.Lex;
using Zurfur.Vm;

namespace Zurfur.Compiler;

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
                var errorList = new List<Token>();
                foreach (var token in lexer)
                    if (token.Error)
                        errorList.Add(token);
                foreach (var token in lexer.MetaTokens)
                    if (token.Error)
                        errorList.Add(token);
                errorList.Sort((a,b) => a.Location.CompareTo(b.Location));

                foreach (var token in errorList)
                {
                    totalErrors++;
                    if (errorMessages.Count > MAX_ERROR_MESSAGES)
                        continue;

                    var errors = token.GetInfos<TokenError>();
                    var errorMessage = Path.GetFileName(token.Path)
                        + $" ({token.Y + 1}:{token.X + 1})";

                    if (errors.Length == 0)
                        errorMessage += "Unknown!";
                    else
                        foreach (var error in errors)
                            errorMessage += ": " + error.Message;
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
            int methods = 0;
            int fields = 0;
            foreach (var sym in symbols.LookupSymbols)
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
                if (sym.IsFun)
                    methods++;
                if (sym.IsField)
                    fields++;
            };

            headerFile.Add("SYMBOLS: " + count);
            headerFile.Add($"    Types: {types} ({typesNonGeneric} non-generic, {typesGeneric} generic)");
            headerFile.Add($"    Specializations: {symbols.SpecializedSymbols.Count} (generated from generics)");
            headerFile.Add($"    Methods: {methods}");
            headerFile.Add($"    Fields: {fields}");
            headerFile.Add("");
        }

        void ShowOverview()
        {
            // Get modules and all symbols
            var modules = new List<string>();
            foreach (var s in symbols.LookupSymbols)
            {
                if (s.IsModule)
                    modules.Add(s.FullName);
            }
            modules.Sort((a, b) => a.CompareTo(b));

            headerFile.Add("");
            headerFile.Add("Modules:");
            foreach (var ns in modules)
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
            var syms = new List<Symbol>(symbols.LookupSymbols);
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
