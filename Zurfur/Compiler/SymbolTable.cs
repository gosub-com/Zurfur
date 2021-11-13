using System;
using System.Collections.Generic;
using Gosub.Zurfur.Lex;
using System.Text;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// The master symbol table is a tree holding modules and types
    /// at the top level, and functions, and parameters at lower levels.
    /// The top level is populated with the non-lambda intrinsic types.
    /// </summary>
    class SymbolTable
    {
        SymModule mRoot;
        public bool NoCompilerChecks;

        // Lookup table for modules and types
        Dictionary<string, Symbol> mLookup = new Dictionary<string, Symbol>();

        public SymbolTable()
        {
            var preRoot = new SymModule(null, "");
            mRoot = new SymModule(preRoot, "");

            // Add built in unary generic types
            foreach (var genericType in "* ^ [ ? ref own mut ro".Split(' '))
                AddIntrinsicType(genericType, 1);
        }

        public Symbol Root => mRoot;

        /// <summary>
        /// Add an intrinsic type, such as "*", "^", "?", "ref", "$fun3", etc
        /// </summary>
        SymType AddIntrinsicType(string type, int numGenerics)
        {
            var sym = new SymType(Root, type);
            sym.IsIntrinsic = true;
            AddOrReject(sym);
            for (int i = 0; i < numGenerics; i++)
            {
                var tn = "T" + (numGenerics == 1 ? "" : $"{i + 1}");
                var t = new SymTypeParam(sym, "", new Token(tn));
                t.IsIntrinsic = true;
                var ok = AddOrReject(t);
                Debug.Assert(ok);
            }
            return sym;
        }

        /// <summary>
        /// Retrieve (or add if it doesn't exist) a built in intrinsic, such as "$fun3"
        /// </summary>
        public SymType FindOrAddIntrinsicType(string name, int numGenerics)
        {
            if (!Root.Children.TryGetValue(name, out var genericFunType))
                genericFunType = AddIntrinsicType(name, numGenerics);
            return (SymType)genericFunType;
        }


        /// <summary>
        /// Generates a lookup table for modules and types.
        /// Must be called before using `Lookup`
        /// </summary>
        public void GenerateLookup()
        {
            mLookup.Clear();
            VisitAll((s) => 
            {
                if (s is SymModule || s.GetType() == typeof(SymType) || s is SymParameterizedType)
                {
                    var fullName = s.FullName;
                    Debug.Assert(!mLookup.ContainsKey(fullName));
                    mLookup[fullName] = s;
                }
            });
        }

        /// <summary>
        /// TBD: I am still figuring out if this is a good way to deal with specializations.
        /// For now, just dump them in the lookup table so we can look them up.
        /// Call this after calling `GenerateLookup`.
        /// </summary>
        public void AddSpecializations(Dictionary<string, SymParameterizedType> specializations)
        {
            foreach (var kv in specializations)
            {
                var symbol = kv.Value;
                var fullName = symbol.FullName;
                Debug.Assert(kv.Key == fullName);
                Debug.Assert(!mLookup.ContainsKey(fullName));
                Debug.Assert(SymbolBelongs(symbol));
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
        /// Find the module, return NULL if not found or it's not a module.
        /// The module is a path in dotted format (e.g. "Zurfur.Draw2d")
        /// </summary>
        public SymModule LookupModule(string name)
        {
            if (!mLookup.TryGetValue(name, out var symbol))
                return null;
            return symbol as SymModule;
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
        public Dictionary<string, Symbol> GetSymbols()
        {
            return GetSymbols(Root);
        }

        /// <summary>
        /// Returns dictionary of full names of all symbols without method or type parameters
        /// </summary>
        public static Dictionary<string, Symbol> GetSymbols(Symbol root)
        {
            var symbols = new Dictionary<string, Symbol>();
            VisitAll(root, (symbol) => 
            {
                Debug.Assert(!symbols.ContainsKey(symbol.FullName));
                symbols[symbol.FullName] = symbol;
            });
            return symbols;
        }

        // Recursively call visit for each symbol in the root.
        public static void VisitAll(Symbol root, Action<Symbol> visit)
        {
            if (root.FullName != "")
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
                    Reject(name, "Module or type name not found");
                    return null;
                }
                symbol = child;
            }
            return symbol;
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
