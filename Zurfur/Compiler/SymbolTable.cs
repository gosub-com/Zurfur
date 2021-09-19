using System;
using System.Collections.Generic;
using Gosub.Zurfur.Lex;
using System.Text;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// The master symbol table is a tree holding namespaces at the top
    /// level, and types, functions, and parameters at lower levels.
    /// </summary>
    class SymbolTable
    {
        SymNamespace mRoot;
        public bool NoCompilerChecks;

        // Lookup table for namespaces and types
        Dictionary<string, Symbol> mLookup = new Dictionary<string, Symbol>();

        public SymbolTable()
        {
            var preRoot = new SymNamespace(null, "", new Token(""));
            mRoot = new SymNamespace(preRoot, "", new Token(""));
        }

        public Symbol Root => mRoot;


        /// <summary>
        /// Generates a lookup table for namespaces and types.
        /// Must be called before using `Lookup`
        /// </summary>
        public void GenerateLookup()
        {
            mLookup.Clear();
            VisitAll((s) => 
            {
                if (s is SymNamespace || s.GetType() == typeof(SymType) || s is SymParameterizedType)
                {
                    var fullName = s.GetFullName();
                    Debug.Assert(!mLookup.ContainsKey(fullName));
                    mLookup[fullName] = s;
                }
            });
        }

        /// <summary>
        /// TBD: I am still figuring out if this is a good way to deal with specializations.
        /// For now, just dump them in the master symbol table so we can look them up.
        /// Call this after calling `GenerateLookup`.
        /// </summary>
        public void AddSpecializations(Dictionary<string, SymParameterizedType> specializations)
        {
            foreach (var kv in specializations)
            {
                var symbol = kv.Value;
                var fullName = symbol.GetFullName();
                Debug.Assert(kv.Key == fullName);
                Debug.Assert(!mLookup.ContainsKey(fullName));
                Debug.Assert(SymbolBelongs(symbol));
                symbol.Parent.SetChildInternal(symbol);
                mLookup[fullName] = kv.Value;
            }
        }

        /// <summary>
        /// Lookup a type.
        /// Call `GenerateLookup` before calling this.
        /// Returns NULL if name doesn't exist or it's not a type of some sort.
        /// </summary>
        public Symbol LookupType(string name)
        {
            if (!mLookup.TryGetValue(name, out var symbol))
                return null;
            if (symbol is SymType || symbol is SymParameterizedType || symbol is SymTypeParam)
                return symbol;
            return null;
        }

        /// <summary>
        /// Find the namespace, return NULL if not found or it's not a namespace.
        /// The namespace is a path in dotted format (e.g. "Zurfur.SecialNamespace")
        /// </summary>
        public SymNamespace LookupNamespace(string name)
        {
            if (!mLookup.TryGetValue(name, out var symbol))
                return null;
            return symbol as SymNamespace;
        }


        /// <summary>
        /// Visit all symbols
        /// </summary>
        public void VisitAll(Action<Symbol> visit)
        {
            VisitAll(mRoot, visit);
        }

        /// <summary>
        /// Returns dictionary of full names of all symbols without method or type parameters
        /// </summary>
        public static Dictionary<string, Symbol> GetSymbolsNoParams(Symbol root)
        {
            var symbols = new Dictionary<string, Symbol>();
            VisitAll(root, (symbol) => 
            {
                if (symbol is SymTypeParam || symbol is SymMethodParam)
                    return;
                Debug.Assert(!symbols.ContainsKey(symbol.GetFullName()));
                symbols[symbol.GetFullName()] = symbol;
            });
            return symbols;
        }

        // Recursively call visit for each symbol in the root.
        public static void VisitAll(Symbol root, Action<Symbol> visit)
        {
            if (root.GetFullName() != "")
                visit(root);
            foreach (var sym in root.Children)
            {
                Debug.Assert(sym.Key == sym.Value.Name);
                VisitAll(sym.Value, visit);
            }
        }

        /// <summary>
        /// Check to make sure the given symbol belongs to this symbol table
        /// </summary>
        bool SymbolBelongs(Symbol symbol)
        {
            while (symbol != null)
            {
                if (symbol == mRoot)
                    return true;
                symbol = symbol.Parent;
            }
            return false;
        }

        /// <summary>
        /// Add a new symbol to its parent, mark duplicates if there is a collision.
        /// Returns true if it was added (false for duplicate).
        /// </summary>
        public bool AddOrReject(Symbol newSymbol)
        {
            Debug.Assert(SymbolBelongs(newSymbol));
            var parentSymbol = newSymbol.Parent;
            if (!parentSymbol.Children.TryGetValue(newSymbol.Name, out var remoteSymbol))
            {
                parentSymbol.SetChildInternal(newSymbol);
                return true;
            }
            Reject(newSymbol.Token, $"Duplicate symbol. There is a {remoteSymbol.Kind} in this scope with the same name.");
            return false;
        }

        /// <summary>
        /// Returns the symbol at the given path in the package.  Generates exception if not found.
        /// </summary>
        public Symbol FindPath(Token[] path)
        {
            var symbol = (Symbol)mRoot;
            foreach (var name in path)
            {
                if (!symbol.Children.TryGetValue(name.Name, out var child))
                    throw new Exception("Compiler error: Could not find parent symbol '" 
                        + string.Join(".", Array.ConvertAll(path, t=>t.Name)) + "'");
                symbol = child;
            }
            return symbol;
        }


        /// <summary>
        /// Returns the symbol at the given path in the package.
        /// Returns null and marks an error if not found.
        /// </summary>
        public Symbol FindPathOrReject(Token[] path)
        {
            var symbol = (Symbol)mRoot;
            foreach (var name in path)
            {
                if (!symbol.Children.TryGetValue(name, out var child))
                {
                    Reject(name, "Namespace or type name not found");
                    return null;
                }
                symbol = child;
            }
            return symbol;
        }


        /// <summary>
        /// Find the symbol in the scope, or null if it is not found.
        /// Does not search use statements.
        /// </summary>
        public Symbol FindInScope(string name, Symbol scope)
        {
            while (scope.Name != "")
            {
                if (scope.Children.TryGetValue(name, out var symbol))
                    return symbol;
                scope = scope.Parent;
            }
            if (scope.Children.TryGetValue(name, out var symbol2))
                return symbol2;

            return null;
        }

        public Symbol FindInScopeOrReject(Token name, Symbol scope)
        {
            var sym = FindInScope(name.Name, scope);
            if (sym == null)
                Reject(name, "Undefined symbol");
            return sym;
        }

        /// <summary>
        /// Find a symbol in the current scope.  If it's not found, scan
        /// use statements for all occurences. 
        /// Must call GenerateLookup to enter all namespaces before calling this.
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// 
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        public Symbol FindInScopeOrUseOrReject(Token name, Symbol scope, string[] use)
        {
            var symbol = FindInScope(name.Name, scope);
            if (symbol != null)
                return symbol;

            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            foreach (var u in use)
            {
                var ns = LookupNamespace(u);
                Debug.Assert(ns != null);
                if (ns != null 
                    && ns.Children.TryGetValue(name.Name, out var newSymbol))
                {
                    symbols.Add(newSymbol);
                }
            }

            if (symbols.Count == 0)
            {
                Reject(name, "Undefined symbol");
                return null;
            }
            if (symbols.Count > 1)
            {
                Reject(name, "Multiple symbols defined.  Found in '" + symbols[0].Locations[0].File
                    + "' and '" + symbols[1].Locations[0].File + "'");
                return null;
            }
            return symbols[0];
        }

        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (NoCompilerChecks)
            {
                if (!token.Warn)
                    token.AddWarning(new ZilWarn("NoCompilerChecks: " + message));
            }
            else
            {
                if (!token.Error)
                    token.AddError(new ZilHeaderError(message));
            }
        }



    }

}
