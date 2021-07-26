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
    /// Symbol names must never include the "#" which is used as a separator.
    /// (e.g. "Zurfur.List" is stored in the table as "Zurfur#List").  This
    /// allows "." to be used in a symbol name if desired.
    /// </summary>
    class SymbolTable
    {
        SymNamespace mRoot;

        public SymbolTable()
        {
            var preRoot = new SymNamespace(null, new SymFile("", new SyntaxFile()), new Token(""));
            mRoot = new SymNamespace(preRoot, new SymFile("", new SyntaxFile()), new Token(""));
        }

        public Symbol Root => mRoot;

        /// <summary>
        /// Visit all symbols
        /// </summary>
        public void VisitAll(Action<string, Symbol> visit)
        {
            VisitAll(mRoot, "", visit);
        }

        public static Dictionary<string, Symbol> GetSymbols(Symbol root)
        {
            var symbols = new Dictionary<string, Symbol>();
            VisitAll(root, "", (name, symbol) => 
            {
                //Debug.Assert(!symbols.ContainsKey(name));
                if (symbols.ContainsKey(name))
                    symbols[" --->" + name] = symbol;
                symbols[name] = symbol;
            });
            return symbols;
        }

        // Recursively call visit(fullName, Symbol) for each symbol in the root.
        public static void VisitAll(Symbol root, string prefix, Action<string, Symbol> visit)
        {
            var fullName = prefix + root.Name;
            if (fullName != "")
                visit(fullName, root);
            var childPrefix = fullName == "" ? "" :  fullName + "#";
            foreach (var sym in root.Children)
            {
                Debug.Assert(sym.Key == sym.Value.Name);
                VisitAll(sym.Value, childPrefix, visit);
            }
        }


        /// <summary>
        /// Add a new symbol to its parent, mark duplicates if necessary.
        /// Adds symbol info to token.
        /// Returns true if it was added (false for duplicate).
        /// </summary>
        public bool Add(Symbol newSymbol)
        {
            var token = newSymbol.Token;
            token.AddInfo(newSymbol);
            var parentSymbol = newSymbol.Parent;
            if (!parentSymbol.Children.TryGetValue(newSymbol.Name, out var remoteSymbol))
            {
                parentSymbol.Children[newSymbol.Name] = newSymbol;
                return true;
            }

            DuplicateSymbol(newSymbol, remoteSymbol);
            return false;
        }

        /// <summary>
        /// Add a new symbol to its parent, mark duplicates if necessary.
        /// Walk up the tree and mark duplicate if there are other type args with the same name.
        /// Adds symbol info to token.
        /// Returns true if it was added (false for duplicate)
        /// </summary>
        public bool AddTypeArg(SymType newSymbol)
        {
            bool isAdded = Add(newSymbol);
            if (isAdded)
            {
                // TBD: Finish
            }
            return isAdded;
        }


        public void DuplicateSymbol(Symbol newSymbol, Symbol remoteSymbol, bool isMethod = false)
        {
            // Duplicate
            var postfix = $" with the same name {(isMethod ? "and type " : "")}as '{(isMethod?remoteSymbol.Parent:remoteSymbol)}'";
            Reject(newSymbol.Token, "Duplicate symbol. There is a " + remoteSymbol.Kind + postfix);
            remoteSymbol.AddDuplicate(newSymbol);
            if (!(remoteSymbol is SymNamespace))
            {
                foreach (var symLoc in remoteSymbol.Locations)
                    if (!symLoc.Token.Error)
                    {
                        symLoc.Token.AddInfo(newSymbol);
                        Reject(symLoc.Token, "Duplicate symbol.  There is a " + newSymbol.Kind + postfix);
                    }
            }
        }

        /// <summary>
        /// Find the namespace, return NULL if not found or it's not a namespace.
        /// The namespace is a path in dotted format (e.g. "Zurfur.SecialNamespace")
        /// </summary>
        public SymNamespace FindNamespace(string name)
        {
            var sym = (Symbol)mRoot;
            foreach (var n in name.Split('.'))
            {
                if (!sym.Children.TryGetValue(n, out sym))
                    return null;
                if (sym as SymNamespace == null)
                    return null;
            }
            return sym as SymNamespace;
        }

        /// <summary>
        /// Find the symbol in the scope, or null if it is not found.
        /// Does not search use statements.
        /// </summary>
        public Symbol FindScope(string name, Symbol scope)
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


        /// <summary>
        /// Returns the symbol at the given path in the package.  Generates exception if not found.
        /// </summary>
        public Symbol FindPath(string[] path)
        {
            var symbol = (Symbol)mRoot;
            foreach (var name in path)
            {
                if (!symbol.Children.TryGetValue(name, out var child))
                    throw new Exception("Compiler error: Could not find parent symbol '" + string.Join(".", path) + "'");
                symbol = child;
            }
            return symbol;
        }

        /// <summary>
        /// Returns the symbol at the given path in the package.
        /// Returns null and marks an error if not found.
        /// </summary>
        public Symbol FindPath(Token[] path)
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
        /// Find a symbol in the current scope.  If it's not found, scan
        /// use statements for all occurences. 
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// 
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        public Symbol FindUse(Token name, Symbol scope, string[] use)
        {
            var symbol = FindScope(name.Name, scope);
            if (symbol != null)
                return symbol;

            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            foreach (var u in use)
            {
                var ns = FindNamespace(u);
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
                Reject(name, "Multiple symbols defined.  Found in '" + symbols[0].Locations[0].File.Path
                    + "' and '" + symbols[1].Locations[0].File.Path + "'");
                return null;
            }
            return symbols[0];
        }

        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (!token.Error)
                token.AddError(new ZilError(message));
        }



    }

}
