﻿using System;
using System.Collections.Generic;
using Gosub.Zurfur.Lex;

/// <summary>
/// This file contains high level symbol table types.  The 'info' types in SymbolInfo
/// contain the low level class data.  It might be better to completely separate them,
/// but for now the high level has access to the low level (not the other way around)
/// </summary>


namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Utility class to find symbols.  Errors are only makred if
    /// the token doesn't already have an error.  Currently, marked
    /// with SilError.
    /// </summary>
    class SymbolTable
    {
        Dictionary<string, Symbol> mSymbols = new Dictionary<string, Symbol>();

        public Symbol this[string i]
        {
            get { return mSymbols[i]; }
            set { mSymbols[i] = value; }
        }

        public Dictionary<string, Symbol>.ValueCollection Values => mSymbols.Values;

        /// <summary>
        /// Add a new symbol to its parent, mark duplicates if necessary.
        /// Adds symbol info to token.
        /// Returns true if it was added (false for duplicate)
        /// </summary>
        public bool Add(Symbol newSymbol)
        {
            var token = newSymbol.Token;
            token.AddInfo(newSymbol);
            var parentSymbol = mSymbols[newSymbol.ParentName];
            if (!parentSymbol.Children.TryGetValue(newSymbol.Name, out var remoteSymbolName))
            {
                parentSymbol.Children[newSymbol.Name] = newSymbol.FullName;
                mSymbols[newSymbol.FullName] = newSymbol;
                return true;
            }

            // Duplicate
            var remoteSymbol = mSymbols[remoteSymbolName];
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


        /// <summary>
        /// Find the symbol in the scope, or null if it is not found.
        /// Does not search use statements.
        /// </summary>
        public Symbol FindScope(string name, Symbol parentScope)
        {
            while (parentScope.Name != "")
            {
                if (parentScope.Children.TryGetValue(name, out var symbol))
                    return mSymbols[symbol];
                parentScope = mSymbols[parentScope.ParentName];
            }
            if (parentScope.Children.TryGetValue(name, out var symbol2))
                return mSymbols[symbol2];

            return null;
        }


        /// <summary>
        /// Returns the symbol at the given path in the package.  Generates exception if not found.
        /// </summary>
        public Symbol FindPath(string[] path)
        {
            var symbol = mSymbols[""];
            foreach (var name in path)
            {
                if (!symbol.Children.TryGetValue(name, out var childName))
                    throw new Exception("Compiler error: Could not find parent symbol '" + string.Join(".", path) + "'");
                symbol = mSymbols[childName];
            }
            return symbol;
        }

        /// <summary>
        /// Returns the symbol at the given path in the package.
        /// Returns null and marks an error if not found.
        /// </summary>
        public Symbol FindPath(Token[] path)
        {
            var symbol = mSymbols[""];
            foreach (var name in path)
            {
                if (!symbol.Children.TryGetValue(name, out var childName))
                {
                    Reject(name, "Namespace or type name not found");
                    return null;
                }
                symbol = mSymbols[childName];
            }
            return symbol;
        }

        /// <summary>
        /// Find a symbol in the current scope or use statements if not found.
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        public Symbol FindUse(Token name, Symbol scope, string[] use)
        {
            var symbol = FindScope(name.Name, mSymbols[scope.ParentName]);
            if (symbol != null)
                return symbol;

            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            foreach (var useSymbol in use)
            {
                if (mSymbols[useSymbol].Children.TryGetValue(name.Name, out var newSymbol))
                {
                    symbols.Add(mSymbols[newSymbol]);
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
                token.AddError(new SilError(message));
        }



    }

}