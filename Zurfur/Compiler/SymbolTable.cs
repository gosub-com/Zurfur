using System;
using System.Collections.Generic;
using Gosub.Zurfur.Lex;
using System.Text;
using System.Diagnostics;
using Newtonsoft.Json.Serialization;
using System.Runtime.InteropServices;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// The master symbol table is a tree holding modules and types
    /// at the top level, and functions, and parameters at lower levels.
    /// </summary>
    class SymbolTable
    {
        SymModule mRoot;

        // TBD: Move to compiler options class, including pragmas and command line, etc.
        public bool NoCompilerChecks;

        /// <summary>
        /// Lookup table for concrete types
        /// </summary>
        Dictionary<string, Symbol> mLookup = new Dictionary<string, Symbol>();

        /// <summary>
        /// TBD: Still figuring out how to deal with specialzied types.
        /// </summary>
        Dictionary<string, SymSpecializedType> mSpecializedTypes = new Dictionary<string, SymSpecializedType>();

        // The generic arguments: #0, #1, #2...
        List<SymSpecializedType> mGenericArguments = new List<SymSpecializedType>();
        Symbol mGenericArgumentHolder; // Holder so they have `Order` set properly

        Symbol mAnonymousTypes;

        public Symbol Root => mRoot;
        public Symbol AnonymousTypes => mAnonymousTypes;

        
        public SymbolTable()
        {
            var preRoot = new SymModule(null, "");
            mRoot = new SymModule(preRoot, "");
            mGenericArgumentHolder = new SymModule(mRoot, "");
            mAnonymousTypes = new Symbol(SymKind.Type, mRoot, "");
            FindOrAddAnonymousType(new Symbol(SymKind.Type, mAnonymousTypes, "()"));
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
                if (s.IsTypeParam || s.IsMethodParam || s.IsLocal)
                    continue;
                Debug.Assert(!s.IsSpecializedType);
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
            if (mAnonymousTypes.TryGetPrimary(name, out var symbol3))
                return symbol3;
            return null;
        }

        /// <summary>
        /// All symbols, excluding specialized types.
        /// Call `GenerateLookup` before using this.
        /// </summary>
        public Dictionary<string, Symbol>.ValueCollection LookupSymbols
            => mLookup.Values;

        public Dictionary<string, SymSpecializedType>.ValueCollection SpecializedSymbols
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
        /// Get or create (and add to symbol table) a specialized type.
        /// </summary>
        public SymSpecializedType GetSpecializedType(Symbol concreteType, Symbol[] typeParams)
        {
            Debug.Assert(concreteType.IsType);
            var sym = new SymSpecializedType(concreteType, typeParams);
            if (mSpecializedTypes.TryGetValue(sym.FullName, out var specExists))
                return specExists;

            // Don't store partially specialized symbols since they can't end up in the symbol table
            //  "AATest.AGenericTest`2.Inner1`2<#0,#1>"   (while adding outer generic params)
            //  "AATest.AGenericTest`2.Inner1`2<Zurfur.str,Zurfur.str>" (while parsing dot operator)

            if (sym.Params.Length == concreteType.GenericParamTotal())
                mSpecializedTypes[sym.FullName] = sym;

            return sym;
        }

        /// <summary>
        /// Get or create a generic parameter: #0, #1, #2...
        /// </summary>
        public SymSpecializedType GetGenericParam(int argNum)
        {
            if (argNum < mGenericArguments.Count)
                return mGenericArguments[argNum];
            for (int i = mGenericArguments.Count; i <= argNum; i++)
            {
                var name = $"#{i}";
                var arg = new SymSpecializedType(mGenericArgumentHolder, name);
                mGenericArguments.Add(arg);
                AddOrReject(arg);
                mSpecializedTypes[name] = arg;
            }
            return mGenericArguments[argNum];
        }

        /// <summary>
        /// Find or add the given anonymous type.  There are types with
        /// names (a int, b f64) such as function parameters, and types
        /// without names ($0 int, $1 f64) such as tuples. The names are
        /// part of the symbol type, so (a int) is not the same as (b int).
        /// </summary>
        public Symbol FindOrAddAnonymousType(Symbol type)
        {
            if (mAnonymousTypes.TryGetPrimary(type.FullName, out var anonType))
                return anonType;
            Debug.Assert(type.Parent == mAnonymousTypes);
            type.Qualifiers |= SymQualifiers.Anonymous;
            AddOrReject(type);
            return type;
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
