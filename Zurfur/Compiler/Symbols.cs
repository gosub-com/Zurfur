using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Gosub.Zurfur.Lex;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Gosub.Zurfur.Compiler
{
    enum SymKind
    {
        None = 0,
        Module = 1,
        Type = 2,
        TypeParam = 3,
        Field = 5,
        Fun = 6,
        FunParam = 7,
        Local = 8,
        All = 100
    }

    enum SymQualifiers
    {
        None = 0,
        Extension = 0x1,
        Interface = 0x2,
        Const = 0x4,
        Static = 0x8,
        Async = 0x10,
        Get = 0x20,
        Set = 0x40,
        Pub = 0x80,
        Protected = 0x100,
        Ro = 0x200,
        Mut = 0x400,
        Ref = 0x800,
        Boxed = 0x1000,        
        Unsafe = 0x2000,
        Enum = 0x4000,
        Init = 0x8000,
        Impl = 0x10000,
        Extern = 0x20000,
        PassCopy = 0x40000,
        ParamOut = 0x80000,
        Own = 0x200000,
        Copy = 0x400000,
        Union = 0x800000,
        NoCopy = 0x1000000,
        Specialized = 0x2000000
    }

    /// <summary>
    /// NOTE: This data structure is all internal to the compiler.
    /// The public definitions are contained in PackageDefinitions.cs.
    /// 
    /// Symbol symbols:
    ///     .   Module, type, or function separator
    ///     `   Number of generic arguments, suffix for type name
    ///     #   Generic argument (followed by argument number)
    ///     ()  Function parameters
    ///     <>  Generic parameters
    ///     $   Special symbol, e.g. $0, $fun, etc.
    /// </summary>
    class Symbol
    {
        static Dictionary<string, Symbol> sEmptyDict = new Dictionary<string, Symbol>();
        static Dictionary<int, string> sTags = new Dictionary<int, string>();

        public Symbol Parent { get; }
        public SymKind Kind { get; protected set; }
        public SymQualifiers Qualifiers;
        Token mToken;
        public string Comments = "";

        // Definitive list of all children.  The key matches the symbol name.
        Dictionary<string, Symbol> mChildren;

        // Quick lookup (by token name) of module, type, field, or parameter.
        // i.e. things that have unique token names within a scope.
        // NOTE: This is an optimization (we could use only mChildren)
        Dictionary<string, Symbol> mPrimary;

        // Quick lookup of token name
        // NOTE: This is an optimization (we could use only mChildren)
        Dictionary<string, bool> mHasFunNamed;

        // Set by `SetChildInternal`.  Type parameters are always first.
        public int Order { get; private set; } = -1;

        /// <summary>
        /// The symbol's full name, excluding tuple names.
        /// e.g. the full name of (a int, b int) is (int,int).
        /// </summary>
        public string FullName { get; private set; }


        /// <summary>
        /// Name as it appears in the lookup table.  For most types, it is
        /// the same as the source code token.  The exceptions are SymFun
        /// and specialized types which include type info.
        /// </summary>        
        public string LookupName { get; private set; }

        /// <summary>
        /// Field, parameter, or function type.
        /// A function's return type is a tuple containing two 
        /// tuples: ((ParameterTypes),(ReturnTypes)).
        /// </summary>
        public Symbol Type;

        /// <summary>
        /// Type arguments for a specialized type or function.  The parent is
        /// always the concrete type or function.  e.g, Map`2 is the concrete
        /// type without supplied TypeArgs, and Map<int,str> is the specialized
        /// type with <int,str> in this array.
        /// concrete
        /// </summary>
        public readonly Symbol[] TypeArgs = Array.Empty<Symbol>();
        
        
        /// <summary>
        /// When supplied, the length of this array always matches the length
        /// of TypeArgs.  Or this array is empty when type args are not named.
        /// Tuples from function parameters and returns have names, but other
        /// type args don't, e.g. Map<int,str> has unnamed type args, but
        /// fun f(a int, b str) does.
        /// </summary>
        public readonly string[] TupleNames = Array.Empty<string>();


        /// <summary>
        /// Applicable to Types and Functions
        /// </summary>
        public Dictionary<string, string[]> Constraints;


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
            Symbol parent, 
            Token token, 
            string name = null, 
            Symbol []typeArgs = null,
            string []tupleNames = null)
        {
            Kind = kind;
            Parent = parent;
            LookupName = name == null ? token.Name : name;
            mToken = token;
            TypeArgs = typeArgs == null ? Array.Empty<Symbol>() : typeArgs;
            TupleNames = tupleNames == null ? Array.Empty<string>() : tupleNames;
            FullName = GetFullName();
        }

        /// <summary>
        /// Create a symbol that is non-existent or not unique in the source
        /// code (e.g. modules, and built-in types like "ref", "*", etc.)
        /// </summary>
        public Symbol(SymKind kind, Symbol parent, string name)
        {
            Kind = kind;
            Parent = parent;
            LookupName = name;
            FullName = GetFullName();
        }


        public int ChildrenCount => mChildren == null ? 0 : mChildren.Count;
        public int PrimaryCount => mPrimary == null ? 0 : mPrimary.Count;
        public string KindName => sKindNames[Kind];

        public string TypeName => Type == null ? "" : Type.FullName;
        public bool HasToken => mToken != null;

        public bool IsGenericArg => FullName.Length != 0 && FullName[0] == '#';
        public bool HasGenericArg => FullName.Contains('#');

        public bool IsTuple => Parent != null && Parent.FullName.StartsWith("()");

        public bool IsInterface
            => IsType && Qualifiers.HasFlag(SymQualifiers.Interface)
                || IsSpecialized && Parent.IsType && Parent.Qualifiers.HasFlag(SymQualifiers.Interface);
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


        public bool IsExtension => Qualifiers.HasFlag(SymQualifiers.Extension);
        public bool IsConst => Qualifiers.HasFlag(SymQualifiers.Const);
        public bool IsStatic => Qualifiers.HasFlag(SymQualifiers.Static);
        public bool IsGetter => Qualifiers.HasFlag(SymQualifiers.Get);
        public bool IsSetter => Qualifiers.HasFlag(SymQualifiers.Set);

        public bool ParamOut
        {
            get { return (Qualifiers & SymQualifiers.ParamOut) != SymQualifiers.None; }
            set
            {
                if (value)
                    Qualifiers |= SymQualifiers.ParamOut;
                else
                    Qualifiers &= ~SymQualifiers.ParamOut;
            }
        }

        /// <summary>
        /// The complete name of the symbol, including all parents up the tree.
        /// </summary>
        string GetFullName()
        {
            if (IsLocal || IsFunParam || IsTypeParam)
                return LookupName;
            else if (Parent == null || Parent.LookupName == "")
                return LookupName;
            else if (Parent != null && IsTuple)
                return LookupName; // The full tuple name is set by the type
            else
            {
                var suffix = "";
                if (IsType)
                {
                    var count = GenericParamCount();
                    suffix = count == 0 ? "" : $"`{count}";
                }
                var separator = IsSpecialized ? "" : ".";

                // Specialized functions get the parents functions parant
                var parentFullName = IsSpecialized && IsFun ? Parent.Parent.FullName + "." : Parent.FullName;
                return parentFullName + separator + LookupName + suffix;
            }
        }

        public string SimpleName
        {
            get
            {
                if (IsFunParam || IsTypeParam || IsLocal)
                    return FullName;
                return mToken == null ? LookupName : Token.Name;
            }
        }



        public static Dictionary<SymKind, string> sKindNames = new Dictionary<SymKind, string>()
        {
            { SymKind.None, "none" },
            { SymKind.Module, "module" },
            { SymKind.Type, "type" },
            { SymKind.TypeParam, "type parameter" },
            { SymKind.Field, "field" },
            { SymKind.Fun, "function" },
            { SymKind.FunParam, "function parameter" },
            { SymKind.Local, "local variable" },
            { SymKind.All, "(all)" },
        };

        // Find module, type, field, or parameter (ignore functions) 
        public bool TryGetPrimary(string key, out Symbol sym)
        {
            if (mPrimary == null)
            {
                sym = null;
                return false;
            }
            return mPrimary.TryGetValue(key, out sym);
        }

        // Check to see if there is a function (i.e. non-primary symbol)
        public bool HasFunNamed(string name)
        {
            if (mHasFunNamed == null)
                return false;
            return mHasFunNamed.ContainsKey(name);
        }

        public Dictionary<string, Symbol>.ValueCollection Children
        {
            get { return mChildren == null ? sEmptyDict.Values : mChildren.Values; }
        }

        public IEnumerable<Symbol> ChildrenFilter(SymKind filter, string named = null)
        {
            if (ChildrenCount == 0)
                yield break;

            if (filter == SymKind.All)
            {
                foreach (var sym in mChildren.Values)
                    if (named == null || named == sym.Token)
                        yield return sym;
            }
            else if (filter == SymKind.Fun)
            {
                foreach (var sym in mChildren.Values)
                    if (sym.IsFun)
                        if (named == null | named == sym.Token)
                            yield return sym;
            }
            else
            {
                if (PrimaryCount == 0)
                    yield break;
                foreach (var sym in mPrimary.Values)
                    if (filter == SymKind.All || sym.Kind == filter)
                        if (named == null || named == sym.Token)
                            yield return sym;
            }
        }

        /// <summary>
        /// Returns all immediate children (types, fields, funs, parameters) of this symbol
        /// </summary>
        public IEnumerable<Symbol> ChildrenRecurse()
        {
            if (ChildrenCount == 0)
                yield break;

            foreach (var sym in Children)
            {
                yield return sym;
                foreach (var child in sym.ChildrenRecurse())
                    yield return child;
            }
        }

        /// <summary>
        /// Return the top level generic type (e.g. List<int> and List<str> return List<T>).
        /// Non generic types just return the type.
        /// </summary>
        /// <returns></returns>
        public Symbol Unspecial()
        {
            return IsSpecialized ? Parent : this;
        }

        public string QualifiersStr()
        {
            lock (sTags)
            {
                var key = (int)Kind + ((int)Qualifiers << 8);
                if (sTags.TryGetValue(key, out var t))
                    return t;

                switch (Kind)
                {
                    case SymKind.Field: t = "field"; break;
                    case SymKind.Fun: t = "fun"; break;
                    case SymKind.FunParam: t = "fun_param"; break;
                    case SymKind.TypeParam: t = "type_param"; break;
                    case SymKind.Module: t = "module"; break;
                    case SymKind.Type: t = "type";  break;
                    case SymKind.Local: t = "local";  break;
                    default: t = "";  Debug.Assert(false); break;
                }

                if (Qualifiers.HasFlag(SymQualifiers.Async)) t += " async";
                if (Qualifiers.HasFlag(SymQualifiers.Boxed)) t += " boxed";
                if (Qualifiers.HasFlag(SymQualifiers.Const)) t += " const";
                if (Qualifiers.HasFlag(SymQualifiers.Enum)) t += " enum";
                if (Qualifiers.HasFlag(SymQualifiers.Extension)) t += " extension";
                if (Qualifiers.HasFlag(SymQualifiers.Extern)) t += " extern";
                if (Qualifiers.HasFlag(SymQualifiers.Get)) t += " get";
                if (Qualifiers.HasFlag(SymQualifiers.Init)) t += " init";
                if (Qualifiers.HasFlag(SymQualifiers.Interface)) t += " interface";
                if (Qualifiers.HasFlag(SymQualifiers.Mut)) t += " mut";
                if (Qualifiers.HasFlag(SymQualifiers.ParamOut)) t += " out";
                if (Qualifiers.HasFlag(SymQualifiers.PassCopy)) t += " passcopy";
                if (Qualifiers.HasFlag(SymQualifiers.Protected)) t += " protected";
                if (Qualifiers.HasFlag(SymQualifiers.Pub)) t += " pub";
                if (Qualifiers.HasFlag(SymQualifiers.Ref)) t += " ref";
                if (Qualifiers.HasFlag(SymQualifiers.Ro)) t += " ro";
                if (Qualifiers.HasFlag(SymQualifiers.Set)) t += " set";
                if (Qualifiers.HasFlag(SymQualifiers.Static)) t += " static";
                if (Qualifiers.HasFlag(SymQualifiers.Unsafe)) t += " unsafe";
                if (Qualifiers.HasFlag(SymQualifiers.Own)) t += " own";
                if (Qualifiers.HasFlag(SymQualifiers.Copy)) t += " copy";
                if (Qualifiers.HasFlag(SymQualifiers.Union)) t += " union";
                if (Qualifiers.HasFlag(SymQualifiers.NoCopy)) t += " nocopy";
                if (Qualifiers.HasFlag(SymQualifiers.Specialized)) t += " specialized";
                sTags[key] = t;
                return t;
            }
        }

        public void SetQualifiers(Token []qualifiers)
        {
            foreach (var q in qualifiers)
                SetQualifier(q.Name);
        }
        public void SetQualifiers(string []qualifiers)
        {
            foreach (var q in qualifiers)
                SetQualifier(q);
        }

        public void SetQualifier(string name)
        {
            switch (name)
            {
                case "module": Debug.Assert(Kind == SymKind.Module);  break;
                case "type": Debug.Assert(Kind == SymKind.Type); break;
                case "type_param": Debug.Assert(Kind == SymKind.TypeParam);  break;
                case "fun_param": Debug.Assert(Kind == SymKind.FunParam); break;
                case "field": Debug.Assert(Kind == SymKind.Field);  break;
                case "fun": Debug.Assert(Kind == SymKind.Fun); break;
                case "set": Qualifiers |= SymQualifiers.Set; break;
                case "get": Qualifiers |= SymQualifiers.Get; break;
                case "afun": Qualifiers |= SymQualifiers.Async; Debug.Assert(Kind == SymKind.Fun); break;
                case "extension": Qualifiers |= SymQualifiers.Extension; break;
                case "interface": Qualifiers |= SymQualifiers.Interface; break;
                case "extern": Qualifiers |= SymQualifiers.Extern; break;
                case "const": Qualifiers |= SymQualifiers.Const; break;
                case "static": Qualifiers |= SymQualifiers.Static; break;
                case "pub": Qualifiers |= SymQualifiers.Pub; break;
                case "protected": Qualifiers |= SymQualifiers.Protected; break;
                case "ro": Qualifiers |= SymQualifiers.Ro; break;
                case "mut": Qualifiers |= SymQualifiers.Mut; break;
                case "ref": Qualifiers |= SymQualifiers.Ref; break;
                case "boxed": Qualifiers |= SymQualifiers.Boxed; break;
                case "unsafe": Qualifiers |= SymQualifiers.Unsafe; break;
                case "enum": Qualifiers |= SymQualifiers.Enum; break;
                case "class": break; // TBD: Implement classes in the future
                case "init": Qualifiers |= SymQualifiers.Init; break;
                case "passcopy": Qualifiers |= SymQualifiers.PassCopy; break;
                case "own": Qualifiers |= SymQualifiers.Own;  break;
                case "copy": Qualifiers |= SymQualifiers.Copy; break;
                case "union": Qualifiers |= SymQualifiers.Union; break;
                case "nocopy": Qualifiers |= SymQualifiers.NoCopy; break;
                case "specialized":
                    Debug.Assert(false); // Set when created
                    break;
                default: Debug.Assert(false);  break;
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
                if (mToken == null)
                {
                    Debug.Assert(false);
                    throw new Exception($"Invalid symbol location for '{KindName}' named '{FullName}'");
                }
                return mToken;
            }
        }

        /// <summary>
        /// Do not set the symbol name after it has been added to the symbol table.
        /// </summary>
        public void SetLookupName(string name)
        {
            LookupName = name;
            FullName = GetFullName();
        }

        /// <summary>
        /// Set function name based on return tuple type.
        /// </summary>
        public void SetFunName()
        {
            // Require the return type to be a tuple
            Debug.Assert(IsFun);
            Debug.Assert(Type.TypeArgs.Length == 2);
            var parameters = Type.TypeArgs[0];
            var returns = Type.TypeArgs[1];

            var genericsCount = GenericParamCount();
            var name = Token + (genericsCount == 0 ? "" : "`" + genericsCount)
                + "(" + string.Join<Symbol>(",", parameters.TypeArgs) + ")"
                + "(" + string.Join<Symbol>(",", returns.TypeArgs) + ")";
            SetLookupName(name);
        }


        /// <summary>
        /// This should only be called by functions in SymbolTable.
        /// It sets the symbol Order to the number of children.
        /// Returns TRUE if the symbol was inserted, false if it
        /// was a duplicate (then remoteSymbol contains the dup)
        /// </summary>
        internal bool SetChildInternal(Symbol value, out Symbol remoteSymbol)
        {
            if (mChildren == null)
                mChildren = new Dictionary<string, Symbol>();
            if (mChildren.TryGetValue(value.LookupName, out remoteSymbol))
                return false;
            mChildren[value.LookupName] = value;

            if (!value.IsFun)
            {
                // Quick lookup of non-functions
                if (mPrimary == null)
                    mPrimary = new Dictionary<string, Symbol>();
                Debug.Assert(!mPrimary.ContainsKey(value.LookupName));
                value.Order = mPrimary.Count;
                mPrimary[value.LookupName] = value;
            }
            else
            {
                // Quick lookup of function name
                if (mHasFunNamed == null)
                    mHasFunNamed = new Dictionary<string, bool>();
                mHasFunNamed[value.Token] = true;
            }

            FullName = GetFullName();
            return true;
        }


        public override string ToString()
        {
            return FullName;
        }

        /// <summary>
        /// Get generic parameter count at this level.
        /// </summary>
        public int GenericParamCount()
        {
            var count = 0;
            foreach (var child in ChildrenFilter(SymKind.TypeParam))
                count++;
            return count;
        }

        /// <summary>
        /// Get total generic parameter count, including encolsing scopes:
        /// (e.g, In`MyClass<T1,T2>.MyFunc<T>()`, MyFunc expects 3 parameters)
        /// </summary>
        public int GenericParamTotal()
        {
            var count = GenericParamCount();
            var p = Parent;
            while (p != null && (p.IsType || p.IsFun))
            {
                count += p.GenericParamCount();
                p = p.Parent;
            }
            return count;
        }

        /// <summary>
        /// Get the generic parameter number (only valid for type parameters)
        /// </summary>
        public int GenericParamNum()
        {
            return Parent.Parent.GenericParamTotal() + Order;
        }

        /// <summary>
        /// Get the function returns as a single return type or as a tuple
        /// when there are multiples.
        /// Only allowed to be called on a function.
        /// </summary>
        public Symbol FunReturnTupleOrType
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
                Debug.Assert(IsFun && Type != null && Type.TypeArgs.Length == 2);
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
                Debug.Assert(IsFun && Type != null && Type.TypeArgs.Length == 2);
                return Type.TypeArgs[0];
            }
        }

    }

}
