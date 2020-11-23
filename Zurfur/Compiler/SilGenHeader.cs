using System;
using System.CodeDom;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class SilError : TokenError
    {
        public SilError(string message) : base(message) { }
    }

    class SilGenHeader
    {
        SymPackage mPackage = new SymPackage();
        SymFile mFileSym;
        bool mError;


        Dictionary<string, SymFile> mFiles = new Dictionary<string, SymFile>();
        Dictionary<string, Symbol> mSymbols = new Dictionary<string, Symbol>();

        List<string> mNamespaces = new List<string>();
        List<string> mAllSymbols = new List<string>();


        public bool GenError => mError;
        public SymPackage Package => mPackage;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        /// Step 2: ResolveTypeNames, requires type definitions from all other packages.
        /// Step 3: GenerateHeader.
        /// </summary>
        public SilGenHeader()
        {
            // Unnamed namespace, used only when there are errors in the parse phase (missing namespace)
            mSymbols["_"] = new SymNamespace("_", null);
        }


        /// <summary>
        /// Step 1: Find all the symbols in this package.
        /// Load namespaces, types, fields, and method group names.
        /// Each file is independent, requires nothing from other packages.
        /// </summary>
        public void EnumerateSymbols(Dictionary<string, SyntaxFile> syntaxFiles)
        {
            AddNamespaces();
            AddTypes();
            AddFields();
            AddMethodGroups();
            foreach (var symbol in mSymbols)
                VerifyNoSameParentSymbols(symbol.Value);
            return;

            void AddNamespaces()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    mFiles[syntaxFile.Key] = new SymFile(syntaxFile.Key, syntaxFile.Value);
                    foreach (var ns in syntaxFile.Value.Namespaces)
                    {
                        var symbol = AddNamespace(ns.Key.Split('.'));
                        symbol.Comments += " " + ns.Value.Comments;

                        // TBD: Keep track of duplicate namespace tokens
                        if (symbol.Token.Name == "" && ns.Value.Tokens.Count != 0)
                            symbol.Token = ns.Value.Tokens[0];
                    }
                }
            }

            Symbol AddNamespace(string[] path)
            {
                var symbols = mSymbols;
                Symbol parentSymbol = null;
                Symbol nsSymbol = null;
                foreach (var name in path)
                {
                    if (!symbols.TryGetValue(name, out nsSymbol))
                    {
                        nsSymbol = new SymNamespace(name, parentSymbol);
                        symbols[name] = nsSymbol;
                    }
                    parentSymbol = nsSymbol;
                    symbols = nsSymbol.Symbols;
                }
                return nsSymbol;
            }

            void AddTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    mFileSym = mFiles[syntaxFile.Key];
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        var newClass = new SymType(type.Name, FindSymbol(type.NamePath));
                        newClass.TypeKeyword = type.Keyword.Name;
                        newClass.Comments = type.Comments;
                        AddSymbol(newClass);
                    }
                }
            }

            void AddFields()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    mFileSym = mFiles[syntaxFile.Key];
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        var newField = new SymField(field.Name, FindSymbol(field.NamePath));
                        newField.Comments = field.Comments;
                        AddSymbol(newField);
                    }
                }
            }

            void AddMethodGroups()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    mFileSym = mFiles[syntaxFile.Key];
                    foreach (var method in syntaxFile.Value.Methods)
                    {
                        var parentSymbol = FindSymbol(method.NamePath);
                        if (!parentSymbol.Symbols.ContainsKey(method.Name))
                            parentSymbol.Symbols[method.Name] = new SymMethods(method.Name, parentSymbol);
                    }
                }
            }

            void VerifyNoSameParentSymbols(Symbol symbol)
            {
                foreach (var childSymbol in symbol.Symbols)
                {
                    if (childSymbol.Value.Name == symbol.Name)
                        Reject(childSymbol.Value.Token, "Name must not be same as parent symbol");
                    VerifyNoSameParentSymbols(childSymbol.Value);
                }
            }

        }

        /// <summary>
        /// Step 2: Resolve type names.  Requires symbols from external packages.
        /// TBD: Add way to include external package headers
        /// </summary>
        public void ResolveTypeNames()
        {
            // Process use statements
            foreach (var syntaxFile in mFiles)
            {
                var useNamespaces = new List<Symbol>();
                foreach (var use in syntaxFile.Value.SyntaxFile.Using)
                {
                    var symbol = FindSymbol(use.NamePath);
                    if (symbol == null)
                        continue;  // Error marked by function

                    var lastToken = use.NamePath[use.NamePath.Length - 1];
                    if (!(symbol is SymNamespace))
                    {
                        Reject(lastToken, "Must be a namespace, not a " + symbol.TypeName);
                        continue;
                    }

                    if (useNamespaces.Contains(symbol))
                    {
                        Reject(lastToken, "Already included in previous use statement");
                        continue;
                    }
                    useNamespaces.Add(symbol);
                }
                // TBD: Sort namespaces so packages are searched first
                syntaxFile.Value.Use = useNamespaces.ToArray();
            }

            foreach (var syntaxFile in mFiles)
            {

            }

            foreach (var syntaxFile in mFiles)
            {
                mFileSym = syntaxFile.Value;
                foreach (var func in syntaxFile.Value.SyntaxFile.Methods)
                {
                    var group = FindSymbol(func.NamePath).Symbols[func.Name.Name];
                    if (!(group is SymMethods))
                    {
                        Reject(func.Name, "Duplicate symbol.  There is a " + group.TypeName + " with the same name");
                        func.Name.AddInfo(group);
                    }

                }
            }
        }

        /// <summary>
        /// Step 3: Generate header file
        /// </summary>
        public void GenerateHeader(string file)
        {
            GenSymbols(mSymbols);
            mNamespaces.Sort((a, b) => a.CompareTo(b));
            mAllSymbols.Sort((a, b) => a.CompareTo(b));
        }

        void GenSymbols(Dictionary<string, Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (symbol.Value is SymNamespace)
                {
                    if (!mNamespaces.Contains(symbol.Value.ToString()))
                        mNamespaces.Add(symbol.Value.ToString());
                }
                else
                {
                    mAllSymbols.Add(symbol.Value.ToString() + ", " + symbol.Value.TypeName);
                }
                GenSymbols(symbol.Value.Symbols);
            }
        }


        /// <summary>
        /// Returns the symbol at the given path.  Generates exception if not found.
        /// </summary>
        Symbol FindSymbol(string []path)
        {
            var symbols = mSymbols;
            Symbol symbol = null;
            foreach (var name in path)
            {
                if (!symbols.TryGetValue(name, out symbol))
                {
                    symbol = null;
                    break;
                }
                symbols = symbol.Symbols;
            }
            if (symbol == null)
                throw new Exception("Compiler error: Could not find parent symbol '" + string.Join(".", path) + "'");
            return symbol;
        }

        /// <summary>
        /// Returns the symbol at the given path or null if not found.
        /// Error tokens are marked if there is an error.
        /// </summary>
        Symbol FindSymbol(Token []path)
        {
            var symbols = mSymbols;
            Symbol symbol = null;
            foreach (var name in path)
            {
                if (!symbols.TryGetValue(name, out symbol))
                {
                    Reject(name, "Namespace or type name not found");
                    return null;
                }
                symbols = symbol.Symbols;
            }
            return symbol;
        }

        /// <summary>
        /// Add a new symbol to its parent, mark duplicates if necessary.
        /// Parent must already be set.  Sets File and Token
        /// Returns true if it was added (false for duplicate)
        /// </summary>
        private bool AddSymbol(Symbol newSymbol)
        {
            newSymbol.File = mFileSym;
            newSymbol.Token.AddInfo(newSymbol);

            var parentSymbol = newSymbol.Parent;
            if (!parentSymbol.Symbols.TryGetValue(newSymbol.Name, out var remoteSymbol))
            {
                parentSymbol.Symbols[newSymbol.Name] = newSymbol;
                return true;
            }

            // Duplicate
            Reject(newSymbol.Token, "Duplicate symbol. There is a " + remoteSymbol.TypeName + " with the same name");
            remoteSymbol.AddDuplicate(newSymbol);
            if (!(remoteSymbol is SymNamespace) && !remoteSymbol.Token.Error)
            {
                remoteSymbol.Token.AddInfo(newSymbol);
                Reject(remoteSymbol.Token, "Duplicate symbol.  There is a " + newSymbol.TypeName + " with the same name");
            }
            return false;
        }

        void Reject(Token token, string message)
        {
            mError = true;
            token.AddError(new SilError(message));
        }

    }
}
