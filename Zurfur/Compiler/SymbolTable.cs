using System;
using System.Collections.Generic;
using Gosub.Zurfur.Lex;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// The master symbol table is a tree holding modules and types
    /// at the top level, and functions, and parameters at lower levels.
    /// </summary>
    class SymbolTable
    {
        Symbol mRoot;

        // TBD: Move to compiler options class, including pragmas and command line, etc.
        public bool NoCompilerChecks;

        /// <summary>
        /// Lookup table for concrete types
        /// </summary>
        Dictionary<string, Symbol> mLookup = new Dictionary<string, Symbol>();

        /// <summary>
        /// Specialized types, excluding tuples which can be named
        /// </summary>
        Dictionary<string, Symbol> mSpecializedTypes = new Dictionary<string, Symbol>();

        // Internal types: Generic arguments, Tuples, wild card
        List<Symbol> mGenericArguments = new List<Symbol>();
        Symbol mGenericArgumentHolder; // Holder so they have `Order` set properly
        List<Symbol> mGenericTuples = new List<Symbol>();
        Symbol mGenericTupleHolder;
        Symbol mWildCard;

        public Symbol Root => mRoot;
        public Symbol EmptyTuple => GetTupleBaseType(0);
        public Symbol WildCard => mWildCard;


        public SymbolTable()
        {
            var preRoot = new Symbol(SymKind.Module, null, null, "");
            mRoot = new Symbol(SymKind.Module, preRoot, null, "");
            mGenericArgumentHolder = new Symbol(SymKind.Type, mRoot, null, "");
            mGenericTupleHolder = new Symbol(SymKind.Module, mRoot, null, "");
            mWildCard = new Symbol(SymKind.Type, mRoot, null, "_");
        }


        /// <summary>
        /// Generates a lookup table, excluding specialized types,
        /// parameters, and locals.  Must be called before using `Lookup`
        /// </summary>
        public void GenerateLookup()
        {
            mLookup.Clear();
            foreach (var s in mRoot.ChildrenRecurse())
            {
                if (s.IsTypeParam || s.IsFunParam || s.IsLocal)
                    continue;
                Debug.Assert(!s.IsSpecialized);
                var fullName = s.FullName;
                Debug.Assert(!mLookup.ContainsKey(fullName));
                mLookup[fullName] = s;
            }
        }

        /// <summary>
        /// Lookup a symbol, including modules, types, specialized types,
        /// fields, and methods.  Excluding parameters and locals.
        /// Call `GenerateLookup` before calling this.
        /// Returns NULL if symbol doesn't exist.
        /// </summary>
        public Symbol Lookup(string name)
        {
            if (name == "")
                return null;
            if (mSpecializedTypes.TryGetValue(name, out var symbol1))
                return symbol1;
            if (mLookup.TryGetValue(name, out var symbol2))
                return symbol2;
            return null;
        }

        /// <summary>
        /// All symbols, excluding specialized types.
        /// Call `GenerateLookup` before using this.
        /// </summary>
        public Dictionary<string, Symbol>.ValueCollection LookupSymbols
            => mLookup.Values;

        public Dictionary<string, Symbol>.ValueCollection SpecializedSymbols
            => mSpecializedTypes.Values;

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
            if (parentSymbol.SetChildInternal(newSymbol, out var remoteSymbol))
                return true;
            Reject(newSymbol.Token, $"Duplicate symbol. There is already a {remoteSymbol.KindName} in this scope with the same name.");
            Reject(remoteSymbol.Token, $"Duplicate symbol. There is already a {newSymbol.KindName} in this scope with the same name.");
            return false;
        }

        /// <summary>
        /// Add a new symbol to its parent.  Returns true if it was added, or
        /// false if it was ignored because the symbol already exists.
        /// </summary>
        public bool AddOrIgnore(Symbol newSymbol)
        {
            Debug.Assert(SymbolBelongs(newSymbol));
            var parentSymbol = newSymbol.Parent;
            return parentSymbol.SetChildInternal(newSymbol, out var remoteSymbol);
        }

        /// <summary>
        /// Create a specialized type.
        /// </summary>
        public Symbol CreateSpecializedType(
            Symbol concreteType, 
            Symbol[] typeArgs,
            string[] tupleNames = null,
            Symbol returnType = null)
        {
            Debug.Assert(concreteType.IsType || concreteType.IsFun || concreteType.IsField);
            if (!NoCompilerChecks)
                Debug.Assert(concreteType.GenericParamTotal() == typeArgs.Length);
            Debug.Assert(!concreteType.IsSpecialized);
            Debug.Assert(tupleNames == null || tupleNames.Length == 0 || tupleNames.Length == typeArgs.Length);

            if (typeArgs.Length == 0)
                return concreteType;

            var symSpec = new Symbol(concreteType.Kind, concreteType,
                concreteType.HasToken ? concreteType.Token : null, concreteType.SimpleName)
            {
                TupleNames = tupleNames,
                TypeArgs = typeArgs
            };
            symSpec.Type = returnType == null ?  concreteType.Type : returnType;
            symSpec.Qualifiers = concreteType.Qualifiers | SymQualifiers.Specialized;

            symSpec.FinalizeFullName();

            // Tuples are not stored in the specialized types
            if (symSpec.IsTuple)
                return symSpec;

            // Store and re-use matching symbols
            if (mSpecializedTypes.TryGetValue(symSpec.FullName, out var specExists))
                return specExists;

            mSpecializedTypes[symSpec.FullName] = symSpec;
            return symSpec;
        }

        /// <summary>
        /// Get or create a generic parameter: #0, #1, #2...
        /// </summary>
        public Symbol GetGenericParam(int argNum)
        {
            if (argNum < mGenericArguments.Count)
                return mGenericArguments[argNum];
            for (int i = mGenericArguments.Count; i <= argNum; i++)
            {
                var name = $"#{i}";
                var arg = new Symbol(SymKind.Type, mGenericArgumentHolder, null, name);
                mGenericArguments.Add(arg);
                AddOrReject(arg);
                mSpecializedTypes[name] = arg;
            }
            return mGenericArguments[argNum];
        }

        /// <summary>
        /// Get or create a generic tuple type.  Tuple #0 is concrete.
        /// </summary>
        public Symbol GetTupleBaseType(int numGenerics)
        {
            if (numGenerics < mGenericTuples.Count)
                return mGenericTuples[numGenerics];
            for (int i = mGenericTuples.Count; i <= numGenerics; i++)
            {
                var name = i == 0 ? "()" :  $"()`{i}";
                var arg = new Symbol(SymKind.Type, mGenericTupleHolder, null, name);
                mGenericTuples.Add(arg);
                AddOrReject(arg);

                for (int j = 0;  j < i; j++)
                {
                    var tp = new Symbol(SymKind.TypeParam, arg, null, $"T{j}");
                    AddOrReject(tp);
                }
            }
            return mGenericTuples[numGenerics];
        }


        /// <summary>
        /// Returns the symbol at the given path in the package.
        /// Returns null and marks an error if not found.
        /// </summary>
        public Symbol FindTypeInPathOrReject(Token[] path)
        {
            var symbol = (Symbol)mRoot;
            foreach (var name in path)
            {
                if (!symbol.TryGetPrimary(name, out var child))
                {
                    Reject(name, "Module or type name not found");
                    return null;
                }
                symbol = child;
            }
            return symbol;
        }

        // Does not reject if there is already an error there
        public void Reject(Token token, string message)
        {
            if (NoCompilerChecks)
            {
                if (!token.Warn)
                    token.AddWarning(new ZilWarn("NoCompilerChecks: " + message));
            }
            else
            {
                if (!token.Error)
                    token.AddError(new ZilCompileError(message));
            }
        }



    }

}
