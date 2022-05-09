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

        /// <summary>
        /// Lookup table for concrete types
        /// </summary>
        Dictionary<string, Symbol> mLookup = new Dictionary<string, Symbol>();

        /// <summary>
        /// TBD: Still figuring out how to deal with specialzied types.
        /// </summary>
        Dictionary<string, SymSpecializedType> mSpecializedTypes = new Dictionary<string, SymSpecializedType>();

        public Symbol Root => mRoot;

        public SymbolTable()
        {
            var preRoot = new SymModule(null, "");
            mRoot = new SymModule(preRoot, "");

            // Add built in unary generic types
            foreach (var genericType in "* ^ [ ? ref own mut ro".Split(' '))
                AddIntrinsicType(genericType, 1);
            AddIntrinsicType("This", 0);
        }        


        /// <summary>
        /// Add an intrinsic type, such as "*", "^", "?", "ref", "$fun3", etc.
        /// </summary>
        Symbol AddIntrinsicType(string type, int numGenerics)
        {
            var sym = new SymType(Root, type);
            sym.IsIntrinsic = true;
            AddOrReject(sym);
            for (int i = 0; i < numGenerics; i++)
            {
                var tn = "T" + (numGenerics == 1 ? "" : $"{i + 1}");
                var t = new SymTypeParam(sym, new Token(tn));
                t.IsIntrinsic = true;
                var ok = AddOrReject(t);
                Debug.Assert(ok);
            }
            return sym;
        }

        /// <summary>
        /// Retrieve (or add if it doesn't exist) a built in intrinsic, such as "$fun3"
        /// </summary>
        public Symbol FindOrAddIntrinsicType(string name, int numGenerics)
        {
            if (!Root.TryGetPrimary(name, out var genericFunType))
                genericFunType = AddIntrinsicType(name, numGenerics);
            return genericFunType;
        }

        /// <summary>
        /// Generates a lookup table for all symbols, excluding specialized types.
        /// Must be called before using `Lookup`
        /// </summary>
        public void GenerateLookup()
        {
            mLookup.Clear();
            foreach (var s in mRoot.ChildrenRecurse())
            {
                Debug.Assert(!s.IsSpecializedType);
                var fullName = s.FullName;
                Debug.Assert(!mLookup.ContainsKey(fullName));
                mLookup[fullName] = s;
            }
        }

        /// <summary>
        /// Lookup a symbol, including specialized types
        /// Call `GenerateLookup` before calling this.
        /// Returns NULL if symbol doesn't exist.
        /// </summary>
        public Symbol Lookup(string name)
        {
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
        public Dictionary<string, Symbol>.ValueCollection Symbols
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
        public SymSpecializedType GetSpecializedType(Symbol concreteType, Symbol[] typeParams, Symbol[] typeReturns = null)
        {
            Debug.Assert(concreteType.IsType);
            var sym = new SymSpecializedType(concreteType, typeParams, typeReturns);
            if (mSpecializedTypes.TryGetValue(sym.FullName, out var specExists))
                return specExists;

            // Don't store partially specialized symbols since they can't end up in the symbol table
            //  "AATest.AGenericTest`2.Inner1`2<#0,#1>"   (while adding outer generic params)
            //  "AATest.AGenericTest`2.Inner1`2<Zurfur.str,Zurfur.str>" (while parsing dot operator)

            if (sym.Params.Length + sym.Returns.Length == concreteType.GenericParamTotal())
                mSpecializedTypes[sym.FullName] = sym;

            return sym;
        }

        /// <summary>
        /// Get or create a generic parameter ('#0', '#1', etc.)
        /// </summary>
        public SymSpecializedType GetGenericParam(int argNum)
        {
            var name = "#" + argNum;
            if (mSpecializedTypes.ContainsKey(name))
                return mSpecializedTypes[name];
            var spec = new SymSpecializedType(Root, name);
            mSpecializedTypes[name] = spec;
            return spec;
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
