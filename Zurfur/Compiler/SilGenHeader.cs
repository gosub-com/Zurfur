using System;
using System.CodeDom;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class SilGenHeader
    {
        SymPackage mPackage = new SymPackage();
        SymFile mFileSym;
        bool mError;


        Dictionary<string, SymFile> mFiles = new Dictionary<string, SymFile>();
        List<string> mNamespaces = new List<string>();
        List<string> mAllSymbols = new List<string>();
        Dictionary<string, Symbol> mSymbols = new Dictionary<string, Symbol>();



        public bool GenError => mError;
        public SymPackage Package => mPackage;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        /// Step 2: GenerateHeader, requires type definitions from all other packages.
        /// Step 3: GenerateCode, requires header of all other packages.
        /// </summary>
        public SilGenHeader()
        {
        }


        /// <summary>
        /// Step 1: Get the type definitions contained in this file.
        /// Load namespaces, types, fields, and func group names.
        /// Each file is independent, requires nothing from other packages.
        /// </summary>
        public void GenerateTypeDefinitions(Dictionary<string, SyntaxFile> syntaxFiles)
        {
            // Add namespaces
            foreach (var syntaxFile in syntaxFiles)
            {
                mFiles[syntaxFile.Key] = new SymFile(syntaxFile.Key);
                foreach (var ns in syntaxFile.Value.Namesapces)
                {
                    if (!mNamespaces.Contains(ns.Key))
                        mNamespaces.Add(ns.Key);
                    var symbol = AddNamespace(ns.Key.Split('.'));
                    symbol.Comments += " " + ns.Value.Comments;

                    // TBD: Keep track of duplicate namespace tokens
                    if (symbol.Token.Name == "" && ns.Value.Tokens.Count != 0)
                        symbol.Token = ns.Value.Tokens[0];
                }
            }

            mNamespaces.Sort((a, b) => a.CompareTo(b));
            foreach (var ns in mNamespaces)
                mAllSymbols.Add(ns);

            // Add types
            foreach (var syntaxFile in syntaxFiles)
            {
                mFileSym = mFiles[syntaxFile.Key];
                foreach (var type in syntaxFile.Value.Types)
                    AddClass(type);
            }

            // Add fields
            foreach (var syntaxFile in syntaxFiles)
            {
                mFileSym = mFiles[syntaxFile.Key];
                foreach (var field in syntaxFile.Value.Fields)
                    AddField(field);
            }

            // Add functions
            foreach (var syntaxFile in syntaxFiles)
            {
                mFileSym = mFiles[syntaxFile.Key];
                foreach (var func in syntaxFile.Value.Funcs)
                    AddFunc(func);
            }
        }

        public void SaveHeader(string fileName)
        {
        }

        Symbol AddNamespace(string []path)
        {
            var symbols = mSymbols;
            Symbol parentSymbol = null;
            Symbol nsSymbol = null;
            foreach (var name in path)
            {
                if (!symbols.TryGetValue(name, out nsSymbol))
                {
                    nsSymbol = new Symbol(SymbolTypeEnum.Namespace, name, parentSymbol);
                    symbols[name] = nsSymbol;
                }
                parentSymbol = nsSymbol;
                symbols = nsSymbol.Symbols;
            }
            return nsSymbol;
        }

        void AddClass(SyntaxType aClass)
        {
            var parentSymbol = FindSymbol(aClass.NamePath);
            if (parentSymbol == null)
            {
                aClass.Name.AddError("Compiler error: Symbol path not found '" + aClass.FullName + "'");
                return;
            }
            var newClass = new SymType(aClass.Name, parentSymbol);
            newClass.Comments = aClass.Comments;
            AddSymbol(parentSymbol, newClass);
            mAllSymbols.Add(newClass.FullName);

            // Add type parameters
            if (aClass.TypeParams != null)
            {
                newClass.TypeArgCount = aClass.TypeParams.Count;
                int index = 0;
                foreach (var param in aClass.TypeParams)
                    AddSymbol(newClass, new SymTypeArg(param.Token, index++, newClass));
            }

            // TBD: Base class, type constraints, etc.
        }


        void AddField(SyntaxField field)
        {
            var parentSymbol = FindSymbol(field.NamePath);
            if (parentSymbol == null)
            {
                field.Name.AddError("Compiler error: Symbol path not found '" + field.FullName + "'");
                return;
            }

            var newField = new SymField(field.Name, parentSymbol);
            newField.Comments = field.Comments;
            AddSymbol(parentSymbol, newField);
            mAllSymbols.Add(newField.FullName);
        }


        private void AddFunc(SyntaxFunc func)
        {
            if (func.ClassName != null)
            {
                func.Name.AddWarning("Not processing extension methods or interface type names yet");
                return;
            }
            var parentSymbol = FindSymbol(func.NamePath);
            if (parentSymbol == null)
            {
                func.Name.AddError("Compiler error: Symbol path not found '" + func.FullName + "'");
                return;
            }

            // Get or create function group
            if (parentSymbol.Symbols.TryGetValue(func.Name, out var functionGroup))
            {
                if (functionGroup as SymFuncs == null)
                    Reject(func.Name, "There is already a namespace, class/struct, or field with the same name");
            }
            else
            {
                functionGroup = new SymFuncs(func.Name, parentSymbol);
                parentSymbol.Symbols[func.Name] = functionGroup;
            }

            // Make name, TBD: Fix
            var name = "(";
            if (func.Params != null)
            {
                for (int i = 0; i < func.Params.Count; i++)
                {
                    name += func.Params[i].ToString();
                    if (i != func.Params.Count - 1)
                        name += ",";
                }
            }
            name += ")";
            if (func.ReturnType != null)
                name += func.ReturnType.ToString();

            var newFunc = new SymFunc(name, func.Name, functionGroup);
            newFunc.Comments = func.Comments;
            AddSymbol(functionGroup, newFunc);
            mAllSymbols.Add(newFunc.FullName);
        }

        /// <summary>
        /// Returns the symbol at the given path, or null if not found
        /// </summary>
        Symbol FindSymbol(string []path)
        {
            var symbols = mSymbols;
            Symbol symbol = null;
            foreach (var name in path)
            {
                if (!symbols.TryGetValue(name, out symbol))
                    return null;
                symbols = symbol.Symbols;
            }
            return symbol;
        }

        /// <summary>
        /// Add a new symbol to its parent, mark duplicates if necessary.
        /// Returns true if it was added (false for duplicate)
        /// </summary>
        private bool AddSymbol(Symbol parentSymbol, Symbol newSymbol)
        {
            newSymbol.File = mFileSym;
            newSymbol.Token.SetInfo(newSymbol);

            if (!parentSymbol.Symbols.TryGetValue(newSymbol.Name, out var remoteSymbol))
            {
                parentSymbol.Symbols[newSymbol.Name] = newSymbol;
                return true;
            }
            else
            {
                // Duplicate
                Reject(newSymbol.Token, "Duplicate of " + remoteSymbol);
                remoteSymbol.AddDuplicate(newSymbol);
                if (remoteSymbol.Type != SymbolTypeEnum.Namespace && !remoteSymbol.Token.Error)
                    Reject(remoteSymbol.Token, "Duplicate of " + newSymbol);
                return false;
            }

        }

        void Reject(Token token, string errorMessage)
        {
            mError = true;
            token.AddError(errorMessage);
        }

    }
}
