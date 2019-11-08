using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{


    class SilGen
    {
        SyntaxUnit mUnit;
        SymFile mSymbols = new SymFile();
        bool mError;
        public bool GenError => mError;

        /// <summary>
        /// Step 1: GenerateDefinitions (requires nothing from any other file)
        /// Step 2: GenerateHeader (requires definitions from all other files)
        /// Step 3: GenerateCode (requires headers from all other files)
        /// </summary>
        /// <param name="unit"></param>
        public SilGen(string fileName, SyntaxUnit unit)
        {
            mUnit = unit;
            mSymbols.FileName = fileName;
        }

        /// <summary>
        /// Step 1: Get the definitions contained in this file.
        /// </summary>
        public void GenerateDefinitions()
        {
            var unit = mUnit;
            if (mUnit.Namespaces.Count == 0)
                return;

            // Find namespaces
            mSymbols.TopNamespace = mUnit.Namespaces[0].FullName;
            foreach (var ns in unit.Namespaces)
            {
                if (!mSymbols.Symbols.ContainsKey(ns.FullName))
                {
                    var scope = new SymScope(SymTypeEnum.Namespace);
                    scope.KeywordToken = ns.Keyword;
                    scope.NameToken = ns.Name;
                    scope.File = mSymbols;
                    scope.Name = ns.FullName;
                    mSymbols.Symbols[ns.FullName] = scope;
                }
            }

            // Find classes
            foreach (var aClass in unit.Classes)
            {
                var newClass = new SymScope(SymTypeEnum.Class);
                newClass.KeywordToken = aClass.Keyword;
                newClass.NameToken = aClass.Name;
                newClass.File = mSymbols;
                newClass.Name = aClass.FullName;

                if (mSymbols.Symbols.TryGetValue(aClass.FullName, out var remoteClass))
                {
                    DuplicateError(aClass.Name, mSymbols.FileName, 
                                   remoteClass.NameToken, remoteClass.File.FileName);

                    // TBD: We probably want to store this in the symbol table so it can be parsed later
                    mSymbols.Symbols[aClass.FullName + "@duplicate"] = newClass;
                }
                else
                {
                    mSymbols.Symbols[aClass.FullName] = newClass;
                }
            }

        }

        private void DuplicateError(Token localToken, string localFile, Token remoteToken, string remoteFile)
        {
            Reject(remoteToken, "Duplicate name");
            Reject(localToken, "Duplicate name");
            Link(remoteToken, localFile, localToken);
            Link(localToken, remoteFile, remoteToken);
        }

        void Reject(Token token, string errorMessage)
        {
            mError = true;
            token.AddError(errorMessage);
        }

        void Link(Token token, string fileName, Token remote)
        {
            token.SetUrl(fileName, remote);
        }

        /// <summary>
        /// Step 2: Generate the header file.
        /// </summary>
        public void GenerateHeader()
        {

        }

        /// <summary>
        /// Step 3: Generate the code.
        /// </summary>
        public void GenerateCode()
        {

        }

    }
}
