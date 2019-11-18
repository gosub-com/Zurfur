using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{

    class SilGen
    {
        SyntaxUnit mUnit;
        SymPackage mPackage = new SymPackage();
        SymFile mFile = new SymFile();
        bool mError;
        public bool GenError => mError;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        ///         a) Per file, b) Merge all files
        /// Step 2: GenerateHeader, requires type definitions from all other packages.
        /// Step 3: GenerateCode, requires header of all other packages.
        /// </summary>
        public SilGen(string fileName, SyntaxUnit unit)
        {
            mUnit = unit;
            mFile.FileName = fileName;
        }

        /// <summary>
        /// Step 1: Get the type definitions contained in this file.
        /// Load namespaces, types, and fields
        /// </summary>
        public void GenerateTypeDefinitions()
        {
            try
            {
                // Namespaces first, then types, then fields
                foreach (var ns in mUnit.Namespaces)
                    AddNamespace(ns);
                foreach (var type in mUnit.Types)
                    AddClass(type);
                foreach (var field in mUnit.Fields)
                    AddField(field);
            }
            catch (Exception ex)
            {
                foreach (var ns in mUnit.Namespaces)
                    Reject(ns.Name, "Exception while generating definitions: " + ex.Message);
            }
        }

        /// <summary>
        /// Step 1a: Merge type definitions from all files.  This is a TBD place holder.
        /// </summary>
        public void MergeTypeDefinitions()
        {

        }

        /// <summary>
        /// TBD: Step 2: Generate the header file.
        /// </summary>
        public void GenerateHeader()
        {
            foreach (var func in mUnit.Funcs)
                AddFunc(func);


        }

        /// <summary>
        /// TBD: Step 3: Generate the code.
        /// </summary>
        public void GenerateCode()
        {
            // Test link
            foreach (var aClass in mUnit.Types)
            {
                var newSymbol = FindSymbol(aClass);

                foreach (var cc in mUnit.Funcs)
                    if (cc.ClassName != null && cc.ClassName.Token.Name == newSymbol.Name)
                        cc.ClassName.Token.SetInfo(newSymbol);
            }


        }

        Symbol AddNamespace(SyntaxScope ns)
        {
            var parentSymbol = ns.ParentScope == null ? mPackage.Symbols : AddNamespace(ns.ParentScope);
            if (!parentSymbol.Symbols.TryGetValue(ns.Name, out var symbol))
            {
                symbol = new Symbol(SymbolTypeEnum.Namespace, ns.Name);
                parentSymbol.Symbols[ns.Name] = symbol;
                symbol.Parent = parentSymbol;
                symbol.File = mFile;
            }
            ns.Name.SetInfo(symbol);
            if (symbol.Type != SymbolTypeEnum.Namespace)
                throw new Exception("Expecting '" + ns.FullName + "' to be a namespace");
            symbol.Comments += ns.Comments;
            return symbol;
        }

        void AddClass(SyntaxType aClass)
        {
            var newSymbol = new SymType(aClass.Name);
            var parentSymbol = FindSymbol(aClass.ParentScope);
            newSymbol.Comments = aClass.Comments;
            AddSymbol(parentSymbol, newSymbol);

            // Add type parameters
            if (aClass.TypeParams != null)
            {
                newSymbol.TypeArgCount = aClass.TypeParams.Count;
                int index = 0;
                foreach (var param in aClass.TypeParams)
                    AddSymbol(newSymbol, new SymTypeArg(param.Token, index++));
            }

            // TBD: Base class, type constraints, etc.
        }

        void AddField(SyntaxField field)
        {
            var parentSymbol = FindSymbol(field.ParentScope);
            if (parentSymbol.IsDuplicte)
            {
                // Duplicate symbol, skip for now, TBD perhaps find
                // the correct one and process anyways
                return;  
            }

            var newSymbol = new SymField(field.Name);
            newSymbol.Comments = field.Comments;
            AddSymbol(parentSymbol, newSymbol);
        }

        private void AddFunc(SyntaxFunc func)
        {
            if (func.ClassName != null)
            {
                func.Name.AddWarning("Not processing extension methods or interface type names yet");
                return;
            }
            var parentSymbol = FindSymbol(func.ParentScope);
            if (parentSymbol.IsDuplicte)
            {
                return; // Skip processing in duplicate processes (for now)
            }
            // Retrieve or create the funcs symbol
            SymFuncs newSymbol = null;
            if (parentSymbol.Symbols.TryGetValue(func.Name, out var remoteSymbol))
                newSymbol = remoteSymbol as SymFuncs;
            if (newSymbol == null)
            {
                newSymbol = new SymFuncs(func.Name);
                newSymbol.File = mFile;
                newSymbol.Parent = parentSymbol;
                newSymbol.Name.SetInfo(newSymbol);
                newSymbol.Comments = func.Comments;
                newSymbol.Symbols[func.Name] = newSymbol;
            }

            if (remoteSymbol != null && remoteSymbol.Type != SymbolTypeEnum.Funcs)
            {
                Reject(func.Name, "Duplicate of " + remoteSymbol);
                if (remoteSymbol.Type != SymbolTypeEnum.Namespace && !remoteSymbol.Name.Error)
                    Reject(remoteSymbol.Name, "Duplicate of " + newSymbol);
                return;
            }

            // TBD: Link them up here


        }


        Symbol FindSymbol(SyntaxScope scope)
        {
            var parentSymbol = scope.ParentScope == null ? mPackage.Symbols : FindSymbol(scope.ParentScope);
            if (!parentSymbol.Symbols.TryGetValue(scope.Name, out var symbol))
                throw new Exception("Symbol not found: '" + scope.FullName + "'");
            return symbol;
        }

        /// <summary>
        /// Add a new symbol to its parent, mark duplicates if necessary.
        /// Returns true if it was added (false for duplicate)
        /// </summary>
        private bool AddSymbol(Symbol parentSymbol, Symbol newSymbol)
        {
            newSymbol.File = mFile;
            newSymbol.Parent = parentSymbol;
            newSymbol.Name.SetInfo(newSymbol);

            if (!parentSymbol.Symbols.TryGetValue(newSymbol.Name, out var remoteSymbol))
            {
                parentSymbol.Symbols[newSymbol.Name] = newSymbol;
                return true;
            }
            else
            {
                // Duplicate
                Reject(newSymbol.Name, "Duplicate of " + remoteSymbol);
                remoteSymbol.AddDuplicate(newSymbol);
                if (remoteSymbol.Type != SymbolTypeEnum.Namespace && !remoteSymbol.Name.Error)
                    Reject(remoteSymbol.Name, "Duplicate of " + newSymbol);
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
