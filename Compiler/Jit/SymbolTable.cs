using System;
using System.Collections.Generic;
using System.Diagnostics;

using Zurfur.Lex;

namespace Zurfur.Jit
{
    public class ZilCompileError : TokenError
    {
        public ZilCompileError(string message) : base(message) { }
    }
    public class ZilWarn : TokenWarn
    {
        public ZilWarn(string message) : base(message) { }
    }

    /// <summary>
    /// The master symbol table is a tree holding modules and types
    /// at the top level, and functions, and parameters at lower levels.
    /// </summary>
    public class SymbolTable
    {
        public readonly Symbol Root;

        // TBD: Move to compiler options class, including pragmas and command line, etc.
        public bool NoCompilerChecks;

        /// <summary>
        /// Lookup table for concrete types
        /// </summary>
        Dictionary<string, Symbol> mLookup = new();

        /// <summary>
        /// Lookup table for specialized types, excluding tuples
        /// </summary>
        Dictionary<string, Symbol> mSpecializedTypes = new();

        // Internal types: Generic arguments, Tuples
        List<Symbol> mGenericArguments = new();
        Symbol mGenericArgumentHolder; // Holder so they have `Order` set properly
        Symbol mGenericTupleHolder;

        /// <summary>
        /// The generic lambda type: $lambda<T> where T is the function tuple.
        /// e.g. $lambda<((params),(returns))> is fun(params)(returns)
        /// </summary>
        public readonly Symbol LambdaType;

        /// <summary>
        /// The concrete generic type from which all tuple types are
        /// specialized.  The name is `()`, and is the only type that
        /// supports a variable number of type arguments.
        /// </summary>
        public Symbol EmptyTuple { get; private set; }


        public SymbolTable()
        {
            var preRoot = new Symbol(SymKind.Module, null, null, "");
            Root = new Symbol(SymKind.Module, preRoot, null, "");
            mGenericArgumentHolder = new Symbol(SymKind.Type, Root, null, "");
            mGenericTupleHolder = new Symbol(SymKind.Module, Root, null, "");
            EmptyTuple = new Symbol(SymKind.Type, mGenericTupleHolder, null, "()");
            mSpecializedTypes["()"] = EmptyTuple;

            // The lambda<T> has a type of T, which is a function tuple
            LambdaType = new Symbol(SymKind.Type, mGenericTupleHolder, null, "$lambda");
            LambdaType.Type = GetGenericParam(0);
            LambdaType.GenericParamNames = new string[] { "T" };
        }


        /// <summary>
        /// Generates a lookup table, excluding specialized types,
        /// parameters, and locals.  Must be called before using `Lookup`
        /// </summary>
        public void GenerateLookup()
        {
            mLookup.Clear();
            foreach (var s in Root.ChildrenRecurse())
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
            if (mLookup.TryGetValue(name, out var sym))
                return sym;
            if (mSpecializedTypes.TryGetValue(name, out sym))
                return sym;
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
                if (symbol == Root)
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
        /// Create a tuple with the given types (optionally with names)
        /// </summary>
        public Symbol CreateTuple(Symbol[] typeArgs, string[] tupleNames = null)
        {
            return CreateSpecializedType(EmptyTuple, typeArgs, tupleNames);
        }

        /// <summary>
        /// Create a lambda from parameter and return tuple.  A lambda is a
        /// specialization of `$lambda` so type args are the parameter/return
        /// tuple.  It's return type is also the parameter/return tuple,
        /// same as a function.
        /// </summary>
        public Symbol CreateLambda(Symbol paramTuple, Symbol returnTuple)
        {
            var funType = CreateTuple(new Symbol[] { paramTuple, returnTuple });
            var lambda = CreateSpecializedType(LambdaType, new Symbol[] { funType });
            lambda.Type = funType;
            return lambda;
        }

        /// <summary>
        /// Create a specialized type.
        /// </summary>
        public Symbol CreateSpecializedType(
            Symbol concreteType,
            Symbol[] typeArgs,
            string[] tupleNames = null)
        {
            Debug.Assert(concreteType.IsType || concreteType.IsFun || concreteType.IsField);
            Debug.Assert(!concreteType.IsSpecialized);
            Debug.Assert(tupleNames == null || tupleNames.Length == 0 || tupleNames.Length == typeArgs.Length);
            if (!NoCompilerChecks && concreteType.SimpleName != "()")
                Debug.Assert(concreteType.GenericParamCount() == typeArgs.Length);

            if (typeArgs.Length == 0)
                return concreteType;

            var symSpec = new Symbol(concreteType.Kind, concreteType,
                concreteType.HasToken ? concreteType.Token : null, concreteType.SimpleName)
            {
                TupleNames = tupleNames == null ? Array.Empty<string>() : tupleNames,
                TypeArgs = typeArgs
            };
            symSpec.Type = ReplaceGenericTypeParams(concreteType.Type, typeArgs);
            symSpec.ReceiverType = ReplaceGenericTypeParams(concreteType.ReceiverType, typeArgs);

            symSpec.Qualifiers = concreteType.Qualifiers | SymQualifiers.Specialized;

            symSpec.FinalizeFullName();

            // Store one copy of specialized symbol or tuple
            if (!mSpecializedTypes.ContainsKey(symSpec.FullName))
                mSpecializedTypes[symSpec.FullName] = symSpec;
            return symSpec;
        }

        // Replace the generic type argument with the given argument,
        // return the result, but don't change the original.
        Symbol ReplaceGenericTypeParams(Symbol type, Symbol[] args)
        {
            if (type == null || args.Length == 0)
                return type;

            if (type.IsGenericArg)
            {
                if (type.Order >= 0 && type.Order < args.Length)
                    return args[type.Order];
                throw new Exception("Compiler error: ReplaceGenericTypeParams, index out of range");
            }

            if (type.IsSpecialized)
                return CreateSpecializedType(type.Parent,
                    ReplaceGenericTypeParamsArray(type.TypeArgs, args), type.TupleNames);

            return type;
        }

        // Replace the generic type argument with the given argument,
        // return the result, but don't change the original.
        Symbol[] ReplaceGenericTypeParamsArray(Symbol[] types, Symbol[] args)
        {
            if (types == null || types.Length == 0)
                return types;
            var newTypes = new Symbol[types.Length];
            for (int i = 0; i < types.Length; i++)
                newTypes[i] = ReplaceGenericTypeParams(types[i], args);
            return newTypes;
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
        /// Returns the symbol at the given path in the package.
        /// Returns null and marks an error if not found.
        /// </summary>
        public Symbol FindTypeInPathOrReject(Token[] path)
        {
            var symbol = Root;
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
