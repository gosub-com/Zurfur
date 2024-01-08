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
    /// Concrete functions used to implement the interface (pass=true),
    /// or a list of failed interface functions (pass=false)
    /// </summary>
    public record InterfaceInfo(
        Symbol Concrete, 
        Symbol Interface, 
        List<Symbol> Functions, 
        CallCompatible Compatibility,
        Symbol[]? TypeArgs = null)
    {
        public string Name { get; private set; } = GetName(Concrete, Interface);
        public override string ToString() => Name;
        public static string GetName(Symbol concrete, Symbol iface)
            => concrete.FullName + " ---> " + iface.FullName;
        public bool Pass => Compatibility == CallCompatible.Compatible;
    }

    public enum CallCompatible
    {
        Compatible,
        GeneratingNow,
        NotAFunction,
        StaticCallToNonStaticMethod,
        NonStaticCallToStaticMethod,
        IncompatibleParameterTypes,
        ExpectingSomeTypeArgs,
        ExpectingNoTypeArgs,
        WrongNumberOfTypeArgs,
        WrongNumberOfParameters,
        TypeArgsSuppliedByConstraint,
        InterfaceGenerating,
        InterfaceToInterfaceConversionNotSupportedYet,
        InterfaceNotImplementedByType,
        TypeArgsNotInferrableOnInterface,
        TypeArgsNotInferrable,
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
        List<Symbol> mGenericConstructors = new();
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

        /// <summary>
        /// The unresolved type '?'
        /// </summary>
        public Symbol Unresolved { get; private set; }
        public Symbol []CreateUnresolvedArray(int count)
        {
            var unresolved = new Symbol[count];
            Array.Fill(unresolved, Unresolved);
            return unresolved;
        }

        public SymbolTable()
        {
            var preRoot = new Symbol(SymKind.Module, null, null, "");
            Root = new Symbol(SymKind.Module, preRoot, null, "");
            Unresolved = new Symbol(SymKind.Module, Root, null, "??");
            mGenericArgumentHolder = new Symbol(SymKind.Type, Root, null, "");
            mGenericTupleHolder = new Symbol(SymKind.Module, Root, null, "");
            EmptyTuple = new Symbol(SymKind.Type, mGenericTupleHolder, null, "()");
            mSpecializedTypes["()"] = EmptyTuple;

            // The lambda<T> has a type of T, which is a function tuple
            LambdaType = new Symbol(SymKind.Type, mGenericTupleHolder, null, "$lambda");
            LambdaType.Type = GetGenericParam(0);
            LambdaType.GenericParamSymbols = [new Symbol(SymKind.TypeParam, LambdaType, null, "T")];
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
        public Symbol? Lookup(string name)
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
        bool SymbolBelongs(Symbol? symbol)
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
            if (newSymbol.Parent!.SetChildInternal(newSymbol, out var remoteSymbol))
                return true;
            Reject(newSymbol.Token, $"Duplicate symbol. There is already a {remoteSymbol!.KindName} in this scope with the same name.");
            Reject(remoteSymbol.Token, $"Duplicate symbol. There is already a {newSymbol.KindName} in this scope with the same name.");
            return false;
        }

        /// <summary>
        /// Create a tuple with the given types (optionally with symbols).
        /// NOTE: A tuple is a specialization of EmptyTuple (i.e. `()`),
        /// so the TypeArgs contains an array of the tuple types.
        /// When supplied, TupleSymbols has the symbols.
        /// TBD: Refactor to make tuples store the types as fields?
        ///      The current method is a bit messy, but works.
        /// </summary>
        public Symbol CreateTuple(Symbol[] tupleTypes, Symbol[]? tupleSymbols = null)
        {
            // Verify no repeated tuple names
            // TBD: Move this to verifier and also check for
            //      duplicate function parameter/returns.
            if (tupleSymbols != null)
                for (var i = 1; i < tupleSymbols.Length; i++)
                    for (var j = 0; j < i; j++)
                        if (tupleSymbols[j].SimpleName == tupleSymbols[i].SimpleName)
                            Reject(tupleSymbols[i].Token, "Duplicate tuple name");

            return CreateSpecializedType(EmptyTuple, tupleTypes, tupleSymbols);
        }

        /// <summary>
        /// Create a lambda from parameter and return tuple.  A lambda is a
        /// specialization of `$lambda` so type args are the parameter/return
        /// tuple.  It's return type is also the parameter/return tuple,
        /// same as a function.
        /// </summary>
        public Symbol CreateLambda(Symbol paramTuple, Symbol returnTuple)
        {
            var funType = CreateTuple([paramTuple, returnTuple]);
            var lambda = CreateSpecializedType(LambdaType, [funType]);
            lambda.Type = funType;
            return lambda;
        }

        public Symbol CreateRef(Symbol type, bool rawPointer = false)
        {
            var refTypeName = rawPointer ? SymTypes.RawPointer : SymTypes.Ref;
            var refType = Lookup(refTypeName);
            if (refType == null)
                throw new Exception($"Compiler error: '{refTypeName}' is undefined in the base library");
            return CreateSpecializedType(refType, [type]);
        }

        /// <summary>
        /// Create a specialized type from a concrete type.
        /// </summary>
        public Symbol CreateSpecializedType(
            Symbol concreteType,
            Symbol[] typeArgs,
            Symbol[]? tupleSymbols = null
            )
        {
            Debug.Assert(!concreteType.IsSpecialized);
            Debug.Assert(concreteType.IsType || concreteType.IsFun || concreteType.IsField);
            Debug.Assert(tupleSymbols == null || tupleSymbols.Length == 0 || tupleSymbols.Length == typeArgs.Length);
            if (!NoCompilerChecks && concreteType.SimpleName != "()")
                Debug.Assert(concreteType.GenericParamCount() == typeArgs.Length);

            if (typeArgs.Length == 0)
                return concreteType;

            var symSpec = new Symbol(concreteType.Kind, concreteType,
                concreteType.HasToken ? concreteType.Token : null, concreteType.SimpleName)
            {
                TypeArgs = typeArgs,
                TupleSymbols = tupleSymbols == null ? Array.Empty<Symbol>() : tupleSymbols
            };

            if (concreteType.Type != null)
                symSpec.Type = ReplaceGenericTypeParams(concreteType.Type, typeArgs);
            symSpec.Qualifiers = concreteType.Qualifiers | SymQualifiers.Specialized;

            // Store only one copy of specialized symbol unless
            // it contains a tuple name which makes it unique
            // in the source code and we have to keep it separate.
            // Probably not a problem for most things like
            // parameter lists, but things like MyBigInterface<(a int, b str)>.
            // TBD: Separate the tuple symbol names from the type definition
            symSpec.FinalizeFullName();
            if (mSpecializedTypes.TryGetValue(symSpec.FullName, out var dupSym))
            {
                // Use space to determine if symbol has a tuple name
                if (!dupSym.FullName.Contains(" "))
                    symSpec = dupSym;   // No space = no tuple = consolidate with previous
            }
            else
            {
                mSpecializedTypes[symSpec.FullName] = symSpec;
            }

            return symSpec;
        }

        // Replace the generic type argument with the given argument,
        // return the result, but don't change the original.
        public Symbol ReplaceGenericTypeParams(Symbol type, Symbol[] args)
        {
            if (args.Length == 0 || !type.HasGenericArg)
            {
                Debug.Assert(type.Type == null);
                return type;
            }

            if (type.IsGenericArg)
            {
                var paramNum = type.GenericParamNum();
                if (paramNum >= 0 && paramNum < args.Length)
                    return args[paramNum];
                throw new Exception("Compiler error: ReplaceGenericTypeParams, index out of range");
            }
            Debug.Assert(type.IsSpecialized);
            return CreateSpecializedType(type.Parent!,
                ReplaceGenericTypeParamsArray(type.TypeArgs, args), type.TupleSymbols);
        }

        // Replace the generic type argument with the given argument,
        // return the result, but don't change the original.
        Symbol[] ReplaceGenericTypeParamsArray(Symbol[] types, Symbol[] args)
        {
            if (types.Length == 0)
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
                var arg = new Symbol(SymKind.TypeParam, mGenericArgumentHolder, null, name);
                mGenericArguments.Add(arg);
                AddOrReject(arg);
                mSpecializedTypes[name] = arg;
            }
            return mGenericArguments[argNum];
        }

        public Symbol GetGenericParamConstructor(int argNum)
        {
            if (argNum < mGenericConstructors.Count)
                return mGenericConstructors[argNum];

            for (int i = mGenericConstructors.Count;  i <= argNum; i++)
            {
                // Create a generic constructor for generic type
                var type = GetGenericParam(i);
                var constructor = new Symbol(SymKind.Fun, type, null, "new");
                constructor.Qualifiers |= SymQualifiers.Static | SymQualifiers.My | SymQualifiers.Method | SymQualifiers.Extern;
                constructor.Type = CreateTuple([
                        CreateTuple([type]),
                        CreateTuple([type]) ]);
                AddOrReject(constructor);
                mGenericConstructors.Add(constructor);

            }
            return mGenericConstructors[argNum];
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
