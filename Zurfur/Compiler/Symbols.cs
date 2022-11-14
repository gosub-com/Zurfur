using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Gosub.Zurfur.Lex;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{
    enum SymKind
    {
        None = 0,
        Module = 1,
        Type = 2,
        TypeParam = 3,
        SpecializedType = 4,
        Field = 5,
        Method = 6,
        MethodParam = 7,
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
        Anonymous = 0x100000,
        Own = 0x200000,
        Copy = 0x400000
    }

    /// <summary>
    /// NOTE: This data structure is all internal to the compiler.
    /// The public definitions are contained in PackageDefinitions.cs.
    /// 
    /// Symbol symbols:
    ///     .   Module, type, or method separator
    ///     `   Number of generic arguments, suffix for type name
    ///     #   Generic argument (followed by argument number)
    ///     ()  Method parameters
    ///     <>  Generic parameters
    ///     $   Special symbol, e.g. $0, $fun, etc.
    /// </summary>
    class Symbol
    {
        static Dictionary<string, Symbol> sEmptyDict = new Dictionary<string, Symbol>();

        public Symbol Parent { get; }
        static Dictionary<int, string> sTags = new Dictionary<int, string>();
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
        Dictionary<string, bool> mHasMethodNamed;

        // Set by `SetChildInternal`.  Type parameters are always first.
        public int Order { get; private set; } = -1;
        string mFullNameCache = null;
        List<Symbol> mParamTypeListCache;


        /// <summary>
        /// Name as it appears in the lookup table.  For most types, it is
        /// the same as the source code token.  The exceptions are SymMethod
        /// and SymSpecializedType which include type info.
        /// </summary>        
        public string LookupName { get; private set; }

        /// <summary>
        /// Field or parameter type (not applicable to types, etc.).
        /// </summary>
        public Symbol Type;

        /// <summary>
        /// Applicable to Types and Methods
        /// </summary>
        public Dictionary<string, string[]> Constraints;


        /// <summary>
        /// Create a symbol that is unique in the soruce code (e.g. SymMethod,
        /// SymType, SymField, etc.) and can be marked with token information.
        /// </summary>
        public Symbol(SymKind kind, Symbol parent, Token token, string name = null)
        {
            Kind = kind;
            Parent = parent;
            LookupName = name == null ? token.Name : name;
            mToken = token;
        }

        /// <summary>
        /// Create a symbol that is non-existent or not unique in the source
        /// code (e.g. SymSpecializedType, SymModule,
        /// and built-in types like "ref", "*", etc.)
        /// </summary>
        public Symbol(SymKind kind, Symbol parent, string name)
        {
            Kind = kind;
            Parent = parent;
            LookupName = name;
        }

        public int ChildrenCount => mChildren == null ? 0 : mChildren.Count;
        public int PrimaryCount => mPrimary == null ? 0 : mPrimary.Count;
        public string KindName => sKindNames[Kind];

        public string TypeName => Type == null ? "" : Type.FullName;
        public bool HasToken => mToken != null;

        public string SimpleName
        {
            get
            {
                if (IsMethodParam || IsTypeParam || IsLocal)
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
            { SymKind.SpecializedType, "specialized type" },
            { SymKind.Field, "field" },
            { SymKind.Method, "method" },
            { SymKind.MethodParam, "method parameter" },
            { SymKind.Local, "local variable" },
            { SymKind.All, "(all)" },
        };

        // Find module, type, field, or parameter (ignore methods) 
        public bool TryGetPrimary(string key, out Symbol sym)
        {
            if (mPrimary == null)
            {
                sym = null;
                return false;
            }
            return mPrimary.TryGetValue(key, out sym);
        }

        // Check to see if there is a method (i.e. non-primary symbol)
        public bool HasMethodNamed(string name)
        {
            if (mHasMethodNamed == null)
                return false;
            return mHasMethodNamed.ContainsKey(name);
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
            else if (filter == SymKind.Method)
            {
                foreach (var sym in mChildren.Values)
                    if (sym.IsMethod)
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
        /// Returns all immediate children (types, fields, methods, parameters) of this symbol
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
            return IsSpecializedType ? Parent : this;
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
                    case SymKind.Method: t = "method"; break;
                    case SymKind.MethodParam: t = "method_param"; break;
                    case SymKind.TypeParam: t = "type_param"; break;
                    case SymKind.Module: t = "module"; break;
                    case SymKind.SpecializedType: t = "specialized";  break;
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
                if (Qualifiers.HasFlag(SymQualifiers.Impl)) t += " impl";
                if (Qualifiers.HasFlag(SymQualifiers.Init)) t += " init";
                if (Qualifiers.HasFlag(SymQualifiers.Interface)) t += " interface";
                if (Qualifiers.HasFlag(SymQualifiers.Mut)) t += " mut";
                if (Qualifiers.HasFlag(SymQualifiers.ParamOut)) t += " out";
                if (Qualifiers.HasFlag(SymQualifiers.PassCopy)) t += " pass_copy";
                if (Qualifiers.HasFlag(SymQualifiers.Protected)) t += " protected";
                if (Qualifiers.HasFlag(SymQualifiers.Pub)) t += " pub";
                if (Qualifiers.HasFlag(SymQualifiers.Ref)) t += " ref";
                if (Qualifiers.HasFlag(SymQualifiers.Ro)) t += " ro";
                if (Qualifiers.HasFlag(SymQualifiers.Set)) t += " set";
                if (Qualifiers.HasFlag(SymQualifiers.Static)) t += " static";
                if (Qualifiers.HasFlag(SymQualifiers.Unsafe)) t += " unsafe";
                if (Qualifiers.HasFlag(SymQualifiers.Anonymous)) t += " anonymous";
                if (Qualifiers.HasFlag(SymQualifiers.Own)) t += " own";
                if (Qualifiers.HasFlag(SymQualifiers.Copy)) t += " copy";
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
                case "method_param": Debug.Assert(Kind == SymKind.MethodParam); break;
                case "field": Debug.Assert(Kind == SymKind.Field);  break;
                case "method":
                case "fun": Debug.Assert(Kind == SymKind.Method); break;
                case "set": Qualifiers |= SymQualifiers.Set; break;
                case "get": Qualifiers |= SymQualifiers.Get; break;
                case "afun": Qualifiers |= SymQualifiers.Async; Debug.Assert(Kind == SymKind.Method); break;
                case "aset": Qualifiers |= SymQualifiers.Async | SymQualifiers.Set; break;
                case "aget": Qualifiers |= SymQualifiers.Async | SymQualifiers.Get; break;
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
                case "impl": Qualifiers |= SymQualifiers.Impl; break;
                case "pass_copy": Qualifiers |= SymQualifiers.PassCopy; break;
                case "anonymous": Qualifiers |= SymQualifiers.Anonymous; break;
                case "own": Qualifiers |= SymQualifiers.Own;  break;
                case "copy": Qualifiers |= SymQualifiers.Copy; break;
                default: Debug.Assert(false);  break;
            }
        }

        public bool IsInterface
            => IsType && Qualifiers.HasFlag(SymQualifiers.Interface)
                || IsSpecializedType && Parent.IsType && Parent.Qualifiers.HasFlag(SymQualifiers.Interface);

        public bool IsModule => Kind == SymKind.Module;
        public bool IsType => Kind == SymKind.Type;
        public bool IsSpecializedType => Kind == SymKind.SpecializedType;
        public bool IsAnyType => IsModule || IsType || IsTypeParam || IsSpecializedType;
        public bool IsAnyTypeNotModule => IsType || IsTypeParam || IsSpecializedType;
        public bool IsField => Kind == SymKind.Field;
        public bool IsMethod => Kind == SymKind.Method;
        public bool IsTypeParam => Kind == SymKind.TypeParam;
        public bool IsMethodParam => Kind == SymKind.MethodParam;
        public bool IsLocal => Kind == SymKind.Local;

        public bool IsExtension => Qualifiers.HasFlag(SymQualifiers.Extension);
        public bool IsConst => Qualifiers.HasFlag(SymQualifiers.Const);
        public bool IsStatic => Qualifiers.HasFlag(SymQualifiers.Static);
        public bool IsGetter => Qualifiers.HasFlag(SymQualifiers.Get);
        public bool IsSetter => Qualifiers.HasFlag(SymQualifiers.Set);
        public bool IsImpl => Qualifiers.HasFlag(SymQualifiers.Impl);
        
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
        /// Source code token if it exists.  Throws an exception for
        /// SymModule, and other symbols that don't hava a token.
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
            mFullNameCache = null;
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

            if (!value.IsMethod)
            {
                // Quick lookup of non-methods
                if (mPrimary == null)
                    mPrimary = new Dictionary<string, Symbol>();
                Debug.Assert(!mPrimary.ContainsKey(value.LookupName));
                value.Order = mPrimary.Count;
                mPrimary[value.LookupName] = value;
            }
            else
            {
                // Quick lookup of method name
                if (mHasMethodNamed == null)
                    mHasMethodNamed = new Dictionary<string, bool>();
                mHasMethodNamed[value.Token] = true;
            }

            mFullNameCache = null;
            return true;
        }

        /// <summary>
        /// The complete name of the symbol, including all parents up the tree.
        /// </summary>
        public string FullName
        {
            get
            {
                if (mFullNameCache != null)
                    return mFullNameCache;

                if (IsLocal || IsMethodParam || IsTypeParam)
                    mFullNameCache = LookupName;
                else if (Parent == null || Parent.LookupName == "")
                    mFullNameCache = LookupName;
                else
                {
                    var suffix = "";
                    if (IsType)
                    {
                        var count = GenericParamCount();
                        suffix = count == 0 ? "" : $"`{count}";
                    }
                    var separator = Kind == SymKind.SpecializedType ? "" : ".";
                    mFullNameCache = Parent.FullName + separator + LookupName + suffix;
                }
                return mFullNameCache;
            }
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
            while (p.IsType || p.IsMethod)
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
        /// Get list of field types.  
        /// TBD: Still working on a generic tuple system
        /// </summary>
        public List<Symbol> GetTupleTypeList(SymbolTable table)
        {
            if (mParamTypeListCache != null)
                return mParamTypeListCache;
            var parameters = Children.ToList();
            parameters.Sort((a, b) => a.Order.CompareTo(b.Order));
            mParamTypeListCache = new List<Symbol>(parameters.Count);
            foreach (var param in parameters)
                mParamTypeListCache.Add(param.Type);
            return mParamTypeListCache;
        }


    }

    class SymModule : Symbol
    {
        public SymModule(Symbol parent, string name) 
            : base(SymKind.Module, parent, name)
        {
        }
    }

    class SymMethod : Symbol
    {
        public SymMethod(Symbol parent, Token token, string name) 
            : base(SymKind.Method, parent, token, name)
        {
        }

        // TBD: These should be part of a generic tuple system
        Symbol mReturnTypeCache;
        Symbol mParamTypeCache;

        /// <summary>
        /// Get a single return type as a non-tuple, or multiple types as a tuple.
        /// </summary>
        public Symbol GetReturnTupleOrType(SymbolTable table)
        {
            var returnTuple = GetReturnTuple(table);
            var returnTypeList = returnTuple.GetTupleTypeList(table);
            if (returnTypeList.Count == 1)
                return returnTypeList[0];
            return returnTuple;
        }

        /// <summary>
        /// Gets the return type, always as an anonymous tuple type
        public Symbol GetReturnTuple(SymbolTable table)
        {
            if (mReturnTypeCache != null)
                return mReturnTypeCache;
            mReturnTypeCache = GetParams(table, true);
            return mReturnTypeCache;
        }

        /// <summary>
        /// Get the parameters type, always as an anonymous tuple type
        /// </summary>
        public Symbol GetParamTuple(SymbolTable table)
        {
            if (mParamTypeCache != null)
                return mParamTypeCache;
            mParamTypeCache = GetParams(table, false);
            return mParamTypeCache;
        }

        // Get parameters or returns as an anonymous tuple type,
        // except for singe returns, which are just the type itself.
        Symbol GetParams(SymbolTable table, bool returns)
        {
            var parameters = ChildrenFilter(SymKind.MethodParam).Where(child => returns == child.ParamOut).ToList();
            parameters.Sort((a, b) => a.Order.CompareTo(b.Order));

            // Create anonymous type of parameters
            var paramType = new Symbol(SymKind.Type, table.AnonymousTypes, "");
            paramType.Qualifiers |= SymQualifiers.Anonymous;
            var sb = new StringBuilder("(");
            for (var i = 0; i < parameters.Count; i++)
            {
                // Create a field with same type and name as parameter
                var parameter = parameters[i];
                if (parameter.Type == null)
                    continue;  // Unresolved type name

                var field = new Symbol(SymKind.Field, paramType, parameter.SimpleName);
                field.Type = parameter.Type;

                // Update the anonymous type name
                paramType.SetChildInternal(field, out var d);
                sb.Append(parameter.SimpleName);
                sb.Append(" ");
                sb.Append(parameter.Type.FullName);
                if (i != parameters.Count - 1)
                    sb.Append(",");
            }
            sb.Append(")");
            paramType.SetLookupName(sb.ToString());
            return table.FindOrAddAnonymousType(paramType);
        }
    }

    /// <summary>
    /// Parent is the full name of the generic type, typeParams are the full name of each argument.
    /// The symbol name is a combination of both parentName<T0,T1,T2...>
    /// </summary>
    class SymSpecializedType : Symbol
    {
        public readonly Symbol[] Params;

        // Constructor for generic type argument
        public SymSpecializedType(Symbol parent, string name)
            : base(SymKind.SpecializedType, parent, parent.HasToken ? parent.Token : null, name)
        {
            Debug.Assert(parent.IsType || parent.LookupName == "");
            Params = Array.Empty<Symbol>();
        }


        // Constructor for generic type 'F<T>' or function 'F<p1,p2...><r1,r2...>'
        public SymSpecializedType(Symbol parent, Symbol[] typeParams)
            : base(SymKind.SpecializedType, parent, parent.HasToken ? parent.Token : null, FullTypeParamNames(typeParams))
        {
            Debug.Assert(parent.IsType);
            Params = typeParams;
        }

        public static string FullTypeParamNames(Symbol[] typeParams)
        {
            return "<" + TypeParamNames(typeParams) + ">";
        }

        static string TypeParamNames(Symbol[] typeParams)
        {
            if (typeParams.Length == 0)
                return "";
            if (typeParams.Length == 1)
                return typeParams[0].FullName;
            StringBuilder sb = new StringBuilder();
            sb.Append(typeParams[0].FullName);
            for (int i = 1; i < typeParams.Length; i++)
            {
                sb.Append(",");
                sb.Append(typeParams[i].FullName);
            }
            return sb.ToString();
        }

    }

}
