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

        // Any symbol containing DUP_STR is a duplicate.
        const string DUP_SYMBOL = "@dup";
        string mDupSymbol = "";
        int mDups;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other file.
        /// Step 2: MergeTypeDefinitions, requires step 1 output from all files in package.
        /// Step 3: GenerateHeader, requires definitions from all other files and packages.
        /// Step 4: GenerateCode
        /// </summary>
        public SilGen(string fileName, SyntaxUnit unit)
        {
            mUnit = unit;
            mFile.FileName = fileName;
            mDupSymbol = DUP_SYMBOL + ((DateTime.Now.Ticks + fileName.GetHashCode()) % 997) + "_";
        }

        /// <summary>
        /// Step 1: Get the type definitions contained in this file.
        /// Load namespaces, classes, structs, etc.
        /// </summary>
        public void GenerateTypeDefinitions()
        {
            var unit = mUnit;
            if (mUnit.Namespaces.Count == 0)
                return;

            // Find namespaces
            foreach (var ns in unit.Namespaces)
            {
                if (!mPackage.Symbols.ContainsKey(ns.FullName))
                {
                    var scope = new SymScope(SymTypeEnum.Namespace);
                    scope.NameToken = ns.Name;
                    scope.File = mFile;
                    scope.Name = ns.FullName;
                    mPackage.Symbols[ns.FullName] = scope;
                }
            }

            // Find classes
            foreach (var aClass in unit.Classes)
            {
                AddSymbol(aClass, new SymClass());
            }


        }

        /// <summary>
        /// Add a new symbol, mark duplicates with an error
        /// </summary>
        private void AddSymbol(SyntaxScope scope, SymScope newSymbol)
        {
            newSymbol.NameToken = scope.Name;
            newSymbol.File = mFile;
            newSymbol.Name = scope.FullName;
            newSymbol.Comments = scope.Comments;

            if (mPackage.Symbols.TryGetValue(scope.FullName, out var remoteSymbol))
            {
                DuplicateError(newSymbol, remoteSymbol, "Duplicate smbol");

                // Store this symbol with a different name so it can be generated
                mPackage.Symbols[scope.FullName + mDupSymbol + ++mDups] = newSymbol;
            }
            else
            {
                mPackage.Symbols[scope.FullName] = newSymbol;
                newSymbol.NameToken.ReplaceInfo(newSymbol);
            }
        }

        private void DuplicateError(SymScope localSymbol, SymScope remoteSymbol, string message)
        {
            Reject(localSymbol.NameToken, message);
            Reject(remoteSymbol.NameToken, message);
            localSymbol.NameToken.ReplaceInfo(remoteSymbol);
            remoteSymbol.NameToken.ReplaceInfo(localSymbol);
        }

        void Reject(Token token, string errorMessage)
        {
            mError = true;
            token.AddError(errorMessage);
        }

        /// <summary>
        /// TBD: Step 2: Merge the type definitions from all files.
        /// </summary>
        public void MergeTypeDefinitions(IEnumerable<SilGen> package)
        {
            
        }

        /// <summary>
        /// TBD: Step 3: Generate the header file.
        /// </summary>
        public void GenerateHeader()
        {
            var unit = mUnit;
            foreach (var field in unit.Fields)
            {
                AddSymbol(field, new SymField());
            }

            // TBD: Need to add parameter type names and bunches of other info
            foreach (var func in unit.Funcs)
            {
                AddSymbol(func, new SymFunc());
            }
        }

        /// <summary>
        /// TBD: Step 4: Generate the code.
        /// </summary>
        public void GenerateCode()
        {

        }

    }
}
