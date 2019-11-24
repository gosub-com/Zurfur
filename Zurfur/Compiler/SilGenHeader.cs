using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{

    class SilGenHeader
    {
        SyntaxUnit mUnit;
        SymPackage mPackage = new SymPackage();
        SymFile mFile = new SymFile();
        bool mError;

        public bool GenError => mError;
        public SymPackage Package => mPackage;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        ///         a) Per file, b) Merge all files to a package
        /// Step 2: GenerateHeader, requires type definitions from all other packages.
        /// Step 3: GenerateCode, requires header of all other packages.
        /// </summary>
        public SilGenHeader(string fileName, SyntaxUnit unit)
        {
            mUnit = unit;
            mFile.FileName = fileName;
        }

        /// <summary>
        /// Step 1a: Get the type definitions contained in this file.
        /// Load namespaces, types, fields, and func group names.
        /// Requires nothing from any other file or package.
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
                foreach (var func in mUnit.Funcs)
                    AddFuncGroup(func);

            }
            catch (Exception ex)
            {
                foreach (var ns in mUnit.Namespaces)
                    Reject(ns.Name, "Exception while generating definitions: " + ex.Message);
            }
        }

        /// <summary>
        /// Step 1b: Merge type definitions from all files in the package.
        /// This is a TBD place holder.
        /// Requires step 1a output from all files in the package, but
        /// no definitions from any other package.
        /// </summary>
        public void MergeTypeDefinitions()
        {

        }

        /// <summary>
        /// Step 2: Generate header.
        /// Requires step 1b output from all files in the package plus
        /// header files (step 2 output) from all other packages.
        /// </summary>
        public void GenerateHeader()
        {
        }

        /// <summary>
        /// TBD: Step 3: Generate the code.
        /// </summary>
        public void GenerateCode()
        {
            // Test link
            /*foreach (var aClass in mUnit.Types)
            {
                var newSymbol = FindSymbol(aClass);

                foreach (var cc in mUnit.Funcs)
                    if (cc.ClassName != null && cc.ClassName.Token.Name == newSymbol.Name)
                        cc.ClassName.Token.SetInfo(newSymbol);
            }*/


        }

        Symbol AddNamespace(SyntaxScope ns)
        {
            var parentSymbol = ns.ParentScope == null ? mPackage.Symbols : AddNamespace(ns.ParentScope);
            if (!parentSymbol.Symbols.TryGetValue(ns.Name, out var newSymbol))
            {
                newSymbol = new Symbol(SymbolTypeEnum.Namespace, ns.Name, parentSymbol);
                parentSymbol.Symbols[ns.Name] = newSymbol;
                newSymbol.File = mFile;
            }
            ns.Name.SetInfo(newSymbol);
            if (newSymbol.Type != SymbolTypeEnum.Namespace)
                throw new Exception("Expecting '" + ns.FullName + "' to be a namespace");
            newSymbol.Comments += ns.Comments;
            return newSymbol;
        }

        void AddClass(SyntaxType aClass)
        {
            var parentSymbol = FindSymbol(aClass.ParentScope);
            var newClass = new SymType(aClass.Name, parentSymbol);
            newClass.Comments = aClass.Comments;
            AddSymbol(parentSymbol, newClass);

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
            var parentSymbol = FindSymbol(field.ParentScope);
            if (parentSymbol.IsDuplicte)
            {
                // Duplicate symbol, skip for now, TBD perhaps find
                // the correct one and process anyways
                return;  
            }

            var newField = new SymField(field.Name, parentSymbol);
            newField.Comments = field.Comments;
            AddSymbol(parentSymbol, newField);
        }

        private void AddFuncGroup(SyntaxFunc func)
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
            SymFuncs newFunc = null;
            if (parentSymbol.Symbols.TryGetValue(func.Name, out var remoteSymbol))
                newFunc = remoteSymbol as SymFuncs;
            if (newFunc == null)
            {
                newFunc = new SymFuncs(func.Name, parentSymbol);
                newFunc.File = mFile;
                newFunc.Name.SetInfo(newFunc);
                newFunc.Comments = func.Comments;
                newFunc.Symbols[func.Name] = newFunc;
            }

            if (remoteSymbol != null && remoteSymbol.Type != SymbolTypeEnum.Funcs)
            {
                Reject(func.Name, "Duplicate of " + remoteSymbol);
                if (remoteSymbol.Type != SymbolTypeEnum.Namespace && !remoteSymbol.Name.Error)
                    Reject(remoteSymbol.Name, "Duplicate of " + newFunc);
            }
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
