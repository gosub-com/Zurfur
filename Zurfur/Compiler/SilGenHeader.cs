using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class SilError : TokenError
    {
        public SilError(string message) : base(message) { }
    }

    class SilGenHeader
    {
        Dictionary<string, SymFile> mFiles = new Dictionary<string, SymFile>();
        Dictionary<string, Symbol> mSymbols = new Dictionary<string, Symbol>();

        List<string> mNamespaces = new List<string>();
        List<string> mAllSymbols = new List<string>();

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        /// Step 2: ResolveTypeNames, requires type definitions from all other packages.
        /// Step 3: GenerateHeader.
        /// </summary>
        public SilGenHeader()
        {
        }


        /// <summary>
        /// Step 1: Find all the symbols in this package.
        /// Load namespaces, types, fields, and method group names.
        /// Each file is independent, requires nothing from other packages.
        /// </summary>
        public void EnumerateSymbols(Dictionary<string, SyntaxFile> syntaxFiles)
        {
            foreach (var syntaxFile in syntaxFiles)
                mFiles[syntaxFile.Key] = new SymFile(syntaxFile.Key, syntaxFile.Value);

            AddNamespaces();
            AddTypes();
            AddFields();
            AddMethodGroups();

            foreach (var symbol in mSymbols)
                VerifyNoSameParentSymbols(symbol.Value);
            return;

            void AddNamespaces()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var ns in symFile.SyntaxFile.Namespaces)
                    {
                        var symbol = AddNamespace(symFile, ns.Value.Path);
                        symbol.Comments += " " + ns.Value.Comments;
                    }
                }
            }

            Symbol AddNamespace(SymFile file, Token[] path)
            {
                var symbols = mSymbols;
                Symbol parentSymbol = null;
                Symbol nsSymbol = null;
                foreach (var token in path)
                {
                    if (symbols.TryGetValue(token.Name, out nsSymbol))
                    {
                        nsSymbol.AddLocation(file, token);
                    }
                    else
                    {
                        nsSymbol = new SymNamespace(parentSymbol, file, token);
                        symbols[token] = nsSymbol;
                    }
                    parentSymbol = nsSymbol;
                    symbols = nsSymbol.Symbols;
                }
                return nsSymbol;
            }

            void AddTypes()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var type in symFile.SyntaxFile.Types)
                    {
                        var newType = new SymType(FindSymbolPath(type.NamePath), symFile, type.Name);
                        newType.TypeKeyword = type.Keyword.Name;
                        newType.Comments = type.Comments;
                        if (AddSymbol(newType) && type.TypeParams.Count != 0)
                        {
                            // Add type arguments
                            foreach (var expr in type.TypeParams)
                            {
                                AddSymbol(new SymTypeArg(newType, symFile, expr.Token));
                            }
                        }
                    }
                }
            }

            void AddFields()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var field in symFile.SyntaxFile.Fields)
                    {
                        var newField = new SymField(FindSymbolPath(field.NamePath), symFile, field.Name);
                        newField.Syntax = field;
                        newField.Comments = field.Comments;
                        AddSymbol(newField);
                    }
                }
            }

            void AddMethodGroups()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var method in symFile.SyntaxFile.Methods)
                    {
                        var parentSymbol = FindSymbolPath(method.NamePath);
                        if (!parentSymbol.Symbols.ContainsKey(method.Name))
                            parentSymbol.Symbols[method.Name] = new SymMethods(parentSymbol, symFile, method.Name);
                    }
                }
            }

            void VerifyNoSameParentSymbols(Symbol symbol)
            {
                foreach (var childSymbol in symbol.Symbols)
                {
                    if (childSymbol.Value.Name == symbol.Name)
                    {
                        foreach (var symLoc in childSymbol.Value.Locations)
                            Reject(symLoc.Token, "Name must not be same as parent symbol");
                    }
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
            ProcessUseStatements();
            MarkDuplicateFunctions();
            ResolveFieldTypes();
            BuildTypeFieldInfo();
            return;

            void ProcessUseStatements()
            {
                foreach (var symFile in mFiles.Values)
                {
                    var useNamespaces = new List<Symbol>();
                    foreach (var use in symFile.SyntaxFile.Using)
                    {
                        var symbol = FindSymbolPath(use.NamePath);
                        if (symbol == null)
                            continue;  // Error marked by function

                        var lastToken = use.NamePath[use.NamePath.Length - 1];
                        if (!(symbol is SymNamespace))
                        {
                            Reject(lastToken, "Must be a namespace, not a " + symbol.Kind);
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
                    symFile.Use = useNamespaces.ToArray();
                }
            }


            void MarkDuplicateFunctions()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var func in symFile.SyntaxFile.Methods)
                    {
                        var group = FindSymbolPath(func.NamePath).Symbols[func.Name.Name];
                        if (!(group is SymMethods))
                        {
                            Reject(func.Name, "Duplicate symbol.  There is a " + group.Kind + " with the same name");
                            func.Name.AddInfo(group);
                        }
                    }
                }
            }

            void ResolveFieldTypes()
            {
                VisitSymbols(sym => 
                {
                    if (!(sym is SymField field))
                        return true;

                    if (field.Parent as SymType == null)
                    {
                        Warning(field.Token, "Compiler error: Parent of field must be a type, namespace not implemented yet");
                        return true;
                    }
                    // Skip enum and errors
                    if (field.Syntax.TypeName == null || field.Syntax.TypeName.Token.Name == "")
                    {
                        if (field.Syntax.ParentScope.Keyword == "enum")
                            Warning(field.Syntax.Name, "TBD: Process enum");
                        else
                            Reject(field.Syntax.Name, "Expecting symbol to have an explicitly named type");
                        return true;
                    }
                    field.Type = ResolveType(field, field.Syntax.TypeName);
                    if (field.Type == null)
                    {
                        Warning(field.Token, "Error resolving symbol type (warning for now)");
                        return true;
                    }

                    return true;
                });
            }


            void BuildTypeFieldInfo()
            {
                var fields = new List<SymFieldInfo>();
                var constFields = new List<SymConstFieldInfo>();
                var staticFields = new List<SymFieldInfo>();

                VisitSymbols(sym =>
                {
                    if (!(sym is SymType type))
                        return true;

                    fields.Clear();
                    constFields.Clear();
                    staticFields.Clear();
                    foreach (var sym2 in sym.Symbols.Values)
                    {
                        if (!(sym2 is SymField symField))
                            continue;

                        if (symField.Type == null)
                        {
                            Warning(symField.Token, "Unresolved type name, compiler error should already have an error");
                            continue;
                        }

                        var syntax = symField.Syntax;
                        if (Array.Exists(syntax.Qualifiers, t => t.Name == "const"))
                        {
                            var constField = new SymConstFieldInfo();
                            constField.Type = symField.Type;
                            constField.Name = symField.Token;
                            constFields.Add(constField);
                        }
                        else
                        {
                            var field = new SymFieldInfo();
                            field.Type = symField.Type;
                            field.Name = symField.Token;
                            if (Array.Exists(syntax.Qualifiers, t => t.Name == "static"))
                                staticFields.Add(field);
                            else
                                fields.Add(field);
                        }
                    }

                    var typeInfo = new SymTypeInfo();
                    typeInfo.ConstFields = constFields.ToArray();
                    typeInfo.StaticFields = staticFields.ToArray();
                    typeInfo.Fields = fields.ToArray();
                    type.Info = typeInfo;


                    return true;
                });
            }

        }

        void Visit(Dictionary<string, Symbol> symbols, Func<Symbol, bool> f)
        {
            foreach (var symbol in symbols.Values)
            {
                if (f(symbol) && !symbol.IsEmpty)
                    Visit(symbol.Symbols, f);
            }
        }

        void VisitSymbols(Func<Symbol, bool> f)
        {
            foreach (var symbol in mSymbols.Values)
            {
                if (f(symbol) && !symbol.IsEmpty)
                    Visit(symbol.Symbols, f);
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

            var headerFile = new List<string>();
            VisitSymbols(sym =>
            {
                if (!(sym is SymType type))
                    return true;

                headerFile.Add("");
                headerFile.Add("");
                headerFile.Add("TYPE NAME: " + type.FullName);
                if (type.Info == null)
                {
                    headerFile.Add("    ERROR, unresolved info");
                    return true;
                }

                foreach (var field in type.Info.ConstFields)
                    headerFile.Add("    const " + field.Name + " " + field.Type);
                foreach (var field in type.Info.StaticFields)
                    headerFile.Add("    static " + field.Name + " " + field.Type);
                foreach (var field in type.Info.Fields)
                    headerFile.Add("    field " + field.Name + " " + field.Type + ", address=" + field.Address);
                return true;
            });

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
                    mAllSymbols.Add(symbol.Value.ToString() + ", " + symbol.Value.Kind);
                }
                GenSymbols(symbol.Value.Symbols);
            }
        }

        static WordSet sTypeSymbols = new WordSet("? * ^ [ mut ref in out ro");

        SymType ResolveType(Symbol scope, SyntaxExpr typeExpr)
        {
            Debug.Assert(typeExpr != null && typeExpr.Token.Name != "");

            // For now skip unary type qualifiers
            while (sTypeSymbols.Contains(typeExpr.Token) && typeExpr.Count >= 1)
                typeExpr = typeExpr[0];
            if (typeExpr.Token == "fun" || typeExpr.Token == "afun" || typeExpr.Token == "type")
            {
                Warning(typeExpr.Token, "Function types not resolved yet");
                return null; // For now skip these
            }

            if (typeExpr.Token == ParseZurf.VT_TYPE_ARG)
            {
                if (typeExpr.Count == 0)
                {
                    Reject(typeExpr.Token, "Unexpected empty type argument list");
                    return null;
                }
                // Resolve sub-types, TBD: Track errors
                for (int i = 1; i < typeExpr.Count; i++)
                    ResolveType(scope, typeExpr[i]);
                return ResolveType(scope, typeExpr[0]);
            }


            var symbol = FindSymbolUse(scope, typeExpr.Token);
            if (symbol == null)
                return null; // Error already marked
            typeExpr.Token.AddInfo(symbol);
            var typeSymbol = symbol as SymType;
            if (typeSymbol == null)
            {
                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
                return null;
            }
            return typeSymbol;
        }

        /// <summary>
        /// Find a symbol in the current scope or use statements if not found.
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        Symbol FindSymbolUse(Symbol scope, Token name)
        {
            var symbol = FindSymbolScope(scope.Parent, name.Name);
            if (symbol != null)
                return symbol;

            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            foreach (var useSymbol in scope.File.Use)
            {
                if (useSymbol.Symbols.TryGetValue(name.Name, out var newSymbol))
                    symbols.Add(newSymbol);
            }
            if (symbols.Count == 0)
            {
                Reject(name, "Undefined symbol");
                return null;
            }
            if (symbols.Count > 1)
            {
                Reject(name, "Multiple symbols defined.  Found in '" + symbols[0].Locations[0].File.Path
                    + "' and '" + symbols[1].Locations[0].File.Path + "'");
                return null;
            }
            return symbols[0];
        }

        /// <summary>
        /// Find the symbol in the scope, or null if it is not found.
        /// Does not search use statements.
        /// </summary>
        Symbol FindSymbolScope(Symbol parentScope, string name)
        {
            while (parentScope != null)
            {
                if (parentScope.Symbols.TryGetValue(name, out var symbol))
                    return symbol;
                parentScope = parentScope.Parent;
            }
            return null;
        }


        /// <summary>
        /// Returns the symbol at the given path in the package.  Generates exception if not found.
        /// </summary>
        Symbol FindSymbolPath(string []path)
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
        /// Returns the symbol at the given path in the package.  Returns null if not found.
        /// Error tokens are marked if there is an error.
        /// </summary>
        Symbol FindSymbolPath(Token []path)
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
            var token = newSymbol.Token;
            token.AddInfo(newSymbol);

            var parentSymbol = newSymbol.Parent;
            if (!parentSymbol.Symbols.TryGetValue(newSymbol.Name, out var remoteSymbol))
            {
                parentSymbol.Symbols[newSymbol.Name] = newSymbol;
                return true;
            }

            // Duplicate
            Reject(token, "Duplicate symbol. There is a " + remoteSymbol.Kind + " with the same name");
            remoteSymbol.AddDuplicate(newSymbol);
            if (!(remoteSymbol is SymNamespace))
            {
                foreach (var symLoc in remoteSymbol.Locations)
                    if (!symLoc.Token.Error)
                    {
                        symLoc.Token.AddInfo(newSymbol);
                        Reject(symLoc.Token, "Duplicate symbol.  There is a " + newSymbol.Kind + " with the same name");
                    }
            }
            return false;
        }

        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (!token.Error)
                token.AddError(new SilError(message));
        }

        // Does not add a warning if there is already an error there
        void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(message);
        }

    }
}
