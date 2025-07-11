﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel.DataAnnotations;

using Gosub.Lex;
using System.Diagnostics.CodeAnalysis;
namespace Zurfur.Vm;

/// <summary>
/// Symbols in the source code known to the compiler.
/// </summary>
public static class SymTypes
{
    public const string RawPointer = "Zurfur.Unsafe.RawPointer`1";
    public const string Ref = "Zurfur.Unsafe.Ref`1";
    public const string Pointer = "Zurfur.Pointer`1";
    public const string Nil = "Zurfur.Nil";
    public const string Maybe = "Zurfur.Maybe`1";
    public const string Result = "Zurfur.Result`1";
    public const string Void = "Zurfur.Void";
    public const string Int = "Zurfur.Int";
    public const string U64 = "Zurfur.U64";
    public const string I32 = "Zurfur.I32";
    public const string U32 = "Zurfur.U32";
    public const string Str = "Zurfur.Str";
    public const string Bool = "Zurfur.Bool";
    public const string Byte = "Zurfur.Byte";
    public const string Float = "Zurfur.Float";
    public const string F32 = "Zurfur.F32";
    public const string Span = "Zurfur.Span`1";

    public static readonly WordMap<string> FriendlyNames = new WordMap<string>
        { { RawPointer, "*" }, { Pointer, "^" }, { Maybe, "?" }, { Ref, "&"} };

    public static readonly WordMap<string> UnaryTypeSymbols = new WordMap<string>()
    {
        {"*", RawPointer },
        {"^", Pointer },
        {"&", Ref },
        {"?", Maybe},
        {"[", Span },
        {"!", Result }
    };
}

/// <summary>
/// Fundamental types
/// </summary>
public enum SymTypeId
{
    Empty = 0,
    Nil = 1,
    Bool = 2,
    Int = 3,
    Float = 4,
    Str = 5
}

public enum SymKind
{
    Module = 1,
    Type = 2,
    TypeParam = 3,
    Field = 5,
    Fun = 6,
    FunParam = 7,
    TupleParam = 8,
    Local = 9,
}

public enum SymQualifiers
{
    None = 0,
    Interface = 0x2,
    Const = 0x4,
    Static = 0x8,
    Async = 0x10,
    Get = 0x20,
    Set = 0x40,
    Pub = 0x80,
    Ro = 0x100,
    Mut = 0x200,
    Ref = 0x400,
    Unsafe = 0x800,
    Enum = 0x1000,
    Init = 0x2000,
    Extern = 0x4000,
    Own = 0x8000,
    Copy = 0x1_0000,
    Union = 0x2_0000,
    NoClone = 0x4_0000,
    Specialized = 0x8_0000,
    Todo = 0x10_0000,
    Struct = 0x20_00000,
    Afun = 0x40_00000,
    Implicit = 0x80_0000,
}

/// <summary>
/// NOTE: This data structure is all internal to the compiler.
/// The public definitions are contained in PackageDefinitions.cs.
/// 
/// TBD: Storing parameters and returns as children in the function is
///      redundant since they are are stored as named tuples.  Refactor
///      to remove the redundant child parameters from functions.
/// 
/// Symbol symbols:
///     .   Module, type, or function separator
///     `   Number of generic arguments, suffix for type name
///     #   Generic argument (followed by argument number)
///     ()  Function parameters
///     <>  Generic parameters
///     $   Special symbol, e.g. $0, $fun, etc.
/// </summary>
public class Symbol
{
    static readonly Dictionary<string, Symbol> s_emptyDict = new();
    static readonly Dictionary<int, string> s_tags = new();

    // Qualifier names (lower case)
    static readonly Dictionary<string, SymQualifiers> s_qualifierNames = new(
        ((SymQualifiers[])Enum.GetValues(typeof(SymQualifiers)))
            .Where(s => s != SymQualifiers.None)
            .Select(s => new KeyValuePair<string, SymQualifiers>(s.ToString().ToLower(), s)));

    static readonly Dictionary<SymKind, string> s_kindNames = new Dictionary<SymKind, string>()
    {
        { SymKind.Module, "module" },
        { SymKind.Type, "type" },
        { SymKind.TypeParam, "type parameter" },
        { SymKind.Field, "field" },
        { SymKind.Fun, "function" },
        { SymKind.FunParam, "function parameter" },
        { SymKind.TupleParam, "tuple parameter" },
        { SymKind.Local, "local variable" },
    };

    public Symbol? Parent { get; }
    public SymKind Kind { get; protected set; }
    public SymQualifiers Qualifiers;
    Token? _token; // TBD: Should make this non-nullable, require a token for all symbols
    public string? Comments;

    // The symbols in this scope
    Dictionary<string, List<Symbol>>? _childrenNamed;
    int _childrenNamedCount;

    // Set by `SetChildInternal`.  Type parameters are always first.
    // Currently, this is only used for storing the generic parameter
    // order.  TBD: Refactor to remove this.
    public int Order { get; private set; } = -1;

    /// <summary>
    /// The symbol's full type name, excluding tuple names.
    /// </summary>
    public string FullName { get; private set; } = "";

    /// <summary>
    /// The simple name is often the same as the source code symbol, but
    /// can also be `my` or another symbol generated by the compiler
    /// (e.g. `_myField` could become `myField` for a public getter)
    /// </summary>
    public readonly string SimpleName;

    /// <summary>
    /// Field, local, parameter, lambda, or function type.
    /// A function or lambda's return type is a tuple containing two 
    /// tuples: ((ParameterTypes),(ReturnTypes)).
    /// </summary>
    public Symbol? Type;

    /// <summary>
    /// Type arguments for a specialized type or function.  The parent is
    /// always the concrete type or function.  e.g, Map`2 is the concrete
    /// type without supplied TypeArgs, and Map<int,str> is the specialized
    /// type with <int,str> in this array.
    /// concrete
    /// </summary>
    public Symbol[] TypeArgs { get; init; } = Array.Empty<Symbol>();

    /// <summary>
    /// When supplied, the length of this array always matches the length
    /// of TypeArgs.  Or this array is empty when type args are not named.
    /// Tuples from function parameters and returns have names, but other
    /// type args don't, e.g. Map<int,str> has unnamed type args, but
    /// fun f(a int, b str) does.
    /// </summary>
    public Symbol[] TupleSymbols { get; init; } = Array.Empty<Symbol>();

    /// <summary>
    /// Generic type parameter symbols
    /// </summary>
    public Symbol[] GenericParamSymbols = Array.Empty<Symbol>();

    /// <summary>
    /// Applicable to Types and Functions
    /// </summary>
    public Dictionary<string, Symbol[]>? Constraints;

    /// <summary>
    /// Path or URL of file containing symbol
    /// </summary>
    public readonly string Path;

    /// <summary>
    /// Set to true when a type or function has been specialized and
    /// `TypeArgs` have been supplied.  The parent is always the concrete
    /// type or function.
    /// </summary>
    public bool IsSpecialized => Qualifiers.HasFlag(SymQualifiers.Specialized);

    /// <summary>
    /// Create a symbol that is unique in the soruce code (e.g. SymFun,
    /// SymType, SymField, etc.) and can be marked with token information.
    /// </summary>
    public Symbol(SymKind kind,
        Symbol? parent,
        string path,
        Token ?token,
        string? name = null)
    {
        Kind = kind;
        Parent = parent;
        Path = path;

        if (name != null)
            SimpleName = name;
        else if (token != null)
            SimpleName = token.Name;
        else
            throw new Exception("Symbol must have token or name");

        _token = token;
        FullName = SimpleName;
    }

    private Symbol() 
    {
        SimpleName = "";
        Path = "";
    }

    public int ChildrenCount => _childrenNamedCount;
    public string KindName => s_kindNames[Kind];

    public string TypeName => Type == null ? "" : Type.FullName;
    public bool HasToken => _token != null;

    public bool IsResolved => FullName != "??";
    public bool IsUnresolved => FullName == "??";
    public bool IsGenericArg => FullName.Length != 0 && FullName[0] == '#';
    public bool HasGenericArg => FullName.Contains('#');
    public bool IsTuple => SimpleName.Length != 0 && SimpleName[0] == '(';
    public bool IsLambda => Concrete.SimpleName == "$lambda";
    public bool IsInterface
        => IsType && Concrete.Qualifiers.HasFlag(SymQualifiers.Interface);
    public bool IsEnum
        => Qualifiers.HasFlag(SymQualifiers.Enum);

    public bool IsModule => Kind == SymKind.Module;
    public bool IsType => Kind == SymKind.Type;
    public bool IsAnyTypeOrModule => IsModule || IsType || IsTypeParam;
    public bool IsAnyType => IsType || IsTypeParam;
    public bool IsField => Kind == SymKind.Field;
    public bool IsFun => Kind == SymKind.Fun;
    public bool IsTypeParam => Kind == SymKind.TypeParam;
    public bool IsFunParam => Kind == SymKind.FunParam;
    public bool IsLocal => Kind == SymKind.Local;
    public bool IsConst => Qualifiers.HasFlag(SymQualifiers.Const);
    public bool IsStatic => Qualifiers.HasFlag(SymQualifiers.Static);
    public bool IsGetter => Qualifiers.HasFlag(SymQualifiers.Get);
    public bool IsSetter => Qualifiers.HasFlag(SymQualifiers.Set);
    public bool IsImplicit => Qualifiers.HasFlag(SymQualifiers.Implicit);

    /// <summary>
    /// Get the parent module
    /// </summary>
    public Symbol ParentModule
    {
        get
        {
            var inModule = this;
            while (inModule != null && !inModule.IsModule)
                inModule = inModule.Parent;
            ArgumentNullException.ThrowIfNull(inModule);
            return inModule;
        }
    }

    /// <summary>
    /// Get the number of expected generic parameters from the concrete type.
    /// </summary>
    public int GenericParamCount()
    {
        if (IsTypeParam)
            return 0;

        var sym = Concrete;
        int count = 0;
        while (sym != null && !sym.IsModule)
        {
            count += sym.GenericParamSymbols.Length;
            sym = sym.Parent;
        }
        return count;
    }


    /// <summary>
    /// Generate the symbol's full name and parameter type list. Must be
    /// called after updating any symbol property that could change the name.
    /// </summary>
    public void FinalizeFullName()
    {
        // TBD: This is still ugly.  Would be nice to not need to call
        // this function from external code (i.e. don't create the
        // symbol until all stuff for naming is known) but that would
        // require more refactoring.

        // Either we have generic parameters or generic args, but not both
        Debug.Assert(GenericParamSymbols.Length == 0 || TypeArgs.Length == 0);

        if (IsLocal || IsFunParam || IsTypeParam || Parent == null || Parent.FullName == "")
        {
            FullName = SimpleName;
            return;
        }

        // Tuples: (name1 Type1, name2 Type2, ...) or (Type1, Type2, ...)
        if (IsTuple)
        {
            if (TupleSymbols.Length == 0
                    || TupleSymbols.Length == 1 && TupleSymbols[0].SimpleName == "")
                FullName = "(" + string.Join(",", TypeArgs.Select(s => s.FullName)) + ")";
            else
                FullName = "(" + string.Join(",",
                    TupleSymbols.Zip(TypeArgs, (ts, ta) => ts.SimpleName + " " + ta.FullName)) + ")";
            return;
        }

        // Friendly name for common types
        if (TypeArgs.Length == 1
            && SymTypes.FriendlyNames.TryGetValue(Parent.FullName, out var name))
        {
            FullName = name + string.Join<Symbol>(",", TypeArgs);
            return;
        }

        // Generic args <type1,type2...>
        Debug.Assert(TupleSymbols.Length == 0);
        var typeArgs = "";
        if (TypeArgs.Length != 0)
            typeArgs = "<" + string.Join<Symbol>(",", TypeArgs) + ">";

        if (IsLambda)
        {
            FullName = Parent.SimpleName + typeArgs;
            return;
        }

        var funParams = "";
        if (IsFun && Type != null)
            funParams = FunParamTuple.FullName + FunReturnTuple.FullName;

        // Postfix types and functions (not lambda) with generic argument count
        var genericArgsCount = "";
        if ((IsType || IsFun) && !IsLambda && GenericParamSymbols.Length != 0)
            genericArgsCount = $"`{GenericParamSymbols.Length}";

        // Specialized functions get the parents functions parant
        var parentFullName = Concrete.Parent!.FullName;
        FullName = parentFullName + "." + SimpleName + genericArgsCount + typeArgs + funParams;
    }

    /// <summary>
    /// Generate the symbol's friendly name. 
    /// </summary>
    public string FriendlyName()
    {
        var name = FriendlyNameInternal(false);

        // Replace generic arguments in function
        for (int i = 0; i < GenericParamSymbols.Length; i++)
            name = name.Replace($"#{i}", GenericParamSymbols[i].SimpleName);

        if (IsFun)
            return "fun " + name;
        else if (IsInterface)
            return "interface " + name;
        else if (IsEnum)
            return "enum " + name;
        else if (IsModule)
            return "module " + name;
        else if (IsTuple)
            return name;
        else if (IsType)
            return name;
        return name;
    }

    /// <summary>
    /// Generate the symbol's friendly name without putting the kind in
    /// front or generic parameter substitution.
    /// </summary>
    string FriendlyNameInternal(bool dropFirstTupleElement)
    {
        if (IsLocal || IsFunParam || IsTypeParam || Parent == null || Parent.FullName == "")
            return SimpleName;

        if (IsField)
            return Parent.FriendlyNameInternal(false) + "." + SimpleName;

        // Symbol types: *, ^, ?, [], &
        if (TypeArgs.Length == 1
                && SymTypes.FriendlyNames.TryGetValue(Parent.FullName, out var friendlyName))
            return friendlyName + TypeArgs[0].FriendlyNameInternal(false);

        // Tuples: (name1 Type1, name2 Type2, ...) or (Type1, Type2, ...)
        if (IsTuple)
        {
            var name = new StringBuilder();
            name.Append("(");
            bool first = true;
            for (var i = dropFirstTupleElement ? 1 : 0; i < TypeArgs.Length; i++)
            {
                if (!first)
                    name.Append(", ");
                first = false;
                if (TupleSymbols.Length != 0 && TupleSymbols[i].SimpleName != "")
                {
                    name.Append(TupleSymbols[i].SimpleName);
                    name.Append(" ");
                }
                name.Append(TypeArgs[i].FriendlyNameInternal(false));
            }
            name.Append(")");
            return name.ToString();
        }

        // Generic args <type1,type2...>. 
        Debug.Assert(TupleSymbols.Length == 0);
        var genericArgs = "";
        if (TypeArgs.Length != 0)
            genericArgs = "<" + string.Join(",", TypeArgs.Select(s => s.FriendlyNameInternal(false))) + ">";
        if (GenericParamSymbols.Length != 0)
            genericArgs += "<" + string.Join(",", GenericParamSymbols.Select(s => s.SimpleName) ) + ">";

        // Function parameters and `my` type
        var myParam = "";
        var funParams = "";
        if (IsFun && Type != null)
        {
            // Non-methods show parameters only
            funParams = FunParamTuple.FriendlyNameInternal(false) + FunReturnTuple.FriendlyNameInternal(false);
            myParam = "";
        }

        return myParam + SimpleName + genericArgs + funParams;
    }


    // Find anything that is not a function.
    // There can only be one primary symbol per simple name,
    // and it is always at the beginning of the list.
    public bool TryGetPrimary(string key, [MaybeNullWhen(false)] out Symbol sym)
    {
        sym = null;
        if (_childrenNamed == null)
            return false;
        if (!_childrenNamed.TryGetValue(key, out var symList))
            return false;
        if (!symList[0].IsFun)
        {
            sym = symList[0];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the list of children
    /// </summary>
    public IEnumerable<Symbol> Children
    {
        get 
        {
            if (_childrenNamed == null)
                yield break;
            foreach (var symList in _childrenNamed.Values)
                foreach (var sym in symList)
                    yield return sym;
        }
    }

    /// <summary>
    /// Returns a list of children with the given simple name
    /// </summary>
    public IEnumerable<Symbol> ChildrenNamed(string simpleName)
    {
        if (_childrenNamed == null)
            return s_emptyDict.Values;
        if (!_childrenNamed.TryGetValue(simpleName, out var children))
            return s_emptyDict.Values;
        return children;
    }

    /// <summary>
    /// Returns all immediate children (types, fields, funs, parameters) of this symbol
    /// </summary>
    public IEnumerable<Symbol> ChildrenRecurse()
    {
        if (_childrenNamed == null)
            yield break;

        foreach (var symList in _childrenNamed.Values)
        {
            foreach (var sym in symList)
            {
                yield return sym;
                foreach (var child in sym.ChildrenRecurse())
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Return the top level generic type (e.g. List<int> and List<str> return List<T>).
    /// Non generic types just return the type.
    /// </summary>
    /// <returns></returns>
    public Symbol Concrete => IsSpecialized ? Parent! : this;


    public string QualifiersStr()
    {
        lock (s_tags)
        {
            var key = (int)Kind + ((int)Qualifiers << 8);
            if (s_tags.TryGetValue(key, out var t))
                return t;

            switch (Kind)
            {
                case SymKind.Field: t = "field"; break;
                case SymKind.Fun: t = "fun"; break;
                case SymKind.FunParam: t = "fun_param"; break;
                case SymKind.TupleParam: t = "tuple_param"; break;
                case SymKind.TypeParam: t = "type_param"; break;
                case SymKind.Module: t = "module"; break;
                case SymKind.Type: t = "type"; break;
                case SymKind.Local: t = "local"; break;
                default: t = ""; Debug.Assert(false); break;
            }

            // Add qualifier names
            foreach (var q in s_qualifierNames)
                if (Qualifiers.HasFlag(q.Value))
                    t += " " + q.Key;

            s_tags[key] = t;
            return t;
        }
    }

    public void SetQualifiers(Token[]? qualifiers)
    {
        if (qualifiers != null)
            foreach (var q in qualifiers)
                SetQualifier(q.Name);
    }
    public void SetQualifiers(string[]? qualifiers)
    {
        if (qualifiers != null)
            foreach (var q in qualifiers)
                SetQualifier(q);
    }

    public void SetQualifier(string name)
    {
        switch (name)
        {
            case "module": Debug.Assert(Kind == SymKind.Module); break;
            case "type": Debug.Assert(Kind == SymKind.Type); break;
            case "type_param": Debug.Assert(Kind == SymKind.TypeParam); break;
            case "fun_param": Debug.Assert(Kind == SymKind.FunParam); break;
            case "tuple_param": Debug.Assert(Kind == SymKind.TupleParam); break;
            case "field": Debug.Assert(Kind == SymKind.Field); break;
            case "fun": Debug.Assert(Kind == SymKind.Fun); break;
            case "specialized":
                Debug.Assert(false); // Set when created
                break;
            default:
                if (s_qualifierNames.TryGetValue(name, out var s))
                    Qualifiers |= s;
                else
                     Debug.Assert(false); 
                break;
        }
    }

    /// <summary>
    /// Source code token if it exists.  Throws an exception for
    /// modules, and other symbols that don't hava a token.
    /// TBD: Force all symbols to have a source code token.
    /// </summary>
    public Token Token
    {
        get
        {
            if (_token == null)
            {
                Debug.Assert(false);
                throw new Exception($"Invalid symbol location for '{KindName}' named '{FullName}'");
            }
            return _token;
        }
    }

    /// <summary>
    /// This should only be called by functions in SymbolTable.
    /// It sets the symbol Order to the number of children.
    /// Returns TRUE if the symbol was inserted, false if it
    /// was a duplicate (then remoteSymbol contains the dup)
    /// </summary>
    internal bool SetChildInternal(Symbol sym, out Symbol? remoteSymbol)
    {
        sym.FinalizeFullName();
        if (sym.IsFun)
        {
            // Good enough for compiler
            // NOTE: Doesn't catch overloaded functions with
            //       same types but different parameter names
            foreach (var s in ChildrenNamed(sym.SimpleName))
            {
                if (sym.FullName == s.FullName)
                {
                    remoteSymbol = s;
                    return false;
                }
            }

            // Verify functions with same parameter types are rejected
            // TBD: Move to verifier
            foreach (var s in ChildrenNamed(sym.SimpleName)
                .Where(find => find.IsFun && find.FunParamTypes.Length == sym.FunParamTypes.Length))
            {
                var match = true;
                for (int i = 0; match && i < s.FunParamTypes.Length; i++)
                    if (s.FunParamTypes[i].FullName != sym.FunParamTypes[i].FullName)
                        match = false;
                if (match)
                {
                    remoteSymbol = s;
                    return false;
                }
            }
        }
        else
        {
            if (TryGetPrimary(sym.SimpleName, out remoteSymbol))
                return false;
        }

        if (_childrenNamed == null)
            _childrenNamed = new();
        if (!_childrenNamed.TryGetValue(sym.SimpleName, out var symList))
        {
            symList = new();
            _childrenNamed[sym.SimpleName] = symList;
        }

        // Internal consistency check (see TryGetPrimary)
        // There can only be one primary symbol per simple name,
        // and it must be at the beginning of the list
        if (!sym.IsFun && symList.Count != 0)
        {
            Debug.Assert(false);
            remoteSymbol = symList[0];
            return false;
        }

        symList.Add(sym);
        sym.Order = _childrenNamedCount++;

        remoteSymbol = null;
        return true;
    }

    public override string ToString()
    {
        return FullName;
    }

    /// <summary>
    /// Get the generic parameter number from a type parameter
    /// </summary>
    public int GenericParamNum()
    {
        Debug.Assert(IsTypeParam);
        return Order;
    }

    /// <summary>
    /// Get the function returns as a single return type or as a tuple
    /// when there are multiples.
    /// Only allowed to be called on a function.
    /// </summary>
    public Symbol FunReturnType
    {
        get
        {
            var returnTuple = FunReturnTuple;
            var returnTypeList = returnTuple.TypeArgs;
            if (returnTypeList.Length == 1)
                return returnTypeList[0];
            return returnTuple;
        }
    }

    /// <summary>
    /// Gets the function returns as a tuple.
    /// Only allowed to be called on a function.
    /// </summary>
    public Symbol FunReturnTuple
    {
        get
        {
            Debug.Assert((IsFun || IsLambda) && Type != null && Type.TypeArgs.Length == 2);
            return Type.TypeArgs[1];
        }
    }

    /// <summary>
    /// Get the function parameters as a tuple.
    /// Only allowed to be called on a function.
    /// </summary>
    public Symbol FunParamTuple
    {
        get
        {
            Debug.Assert((IsFun || IsLambda) && Type != null && Type.TypeArgs.Length == 2);
            return Type.TypeArgs[0];
        }
    }

    /// <summary>
    /// Get function parameter types as an array (only call on a function)
    /// </summary>
    public Symbol[] FunParamTypes => FunParamTuple.TypeArgs;

    /// <summary>
    /// Get function return types as an array (only call on a function)
    /// </summary>
    public Symbol[] FunReturnTypes => FunReturnTuple.TypeArgs;



}
