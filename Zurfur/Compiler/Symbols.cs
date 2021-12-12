using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Gosub.Zurfur.Lex;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// NOTE: This data structure is all internal to the compiler.
    /// The public definitions are contained in PackageDefinitions.cs.
    /// For example, `SymMethodGroup` doesn't exist in the header file
    /// json, and there can be other differences.
    /// 
    /// Symbol symbols:
    ///     .   Module, type, or method separator
    ///     @   Field name separator
    ///     ~   Parameter (method or type) separator
    ///     `   Number of generic arguments, suffix for type name
    ///     #   Generic argument (followed by argument number)
    ///     ()  Method parameters
    ///     <>  Generic parameters
    ///     $   Special symbol, e.g. $this, $extension, etc.
    ///    
    /// Sepecial symbols (prefixed with $):
    ///     $this        Implicit extension/member method parameter
    ///     $return      Implicit return parameter name
    ///     $extension   Extension type (container for extension methods)
    /// </summary>
    abstract class Symbol
    {
        public Symbol Parent { get; }
        string mFile;
        Token mToken;
        public string Comments = "";
        public string[] Qualifiers = Array.Empty<string>();
        Dictionary<string, Symbol> mChildren = new Dictionary<string, Symbol>();
        public abstract string Kind { get; }

        /// <summary>
        /// True for built in unary types that don't get serialized,
        /// such as "? * ^ ref", etc.
        /// </summary>
        public bool IsIntrinsic;

        // Set by `SetChildInternal`.  Type parameters are always first.
        public int Order { get; private set; } = -1;
        string mFullNameCache = null;

        /// <summary>
        /// Name as it appears in the lookup table.  For most types, it is
        /// the same as the source code token.  The exceptions are SymMethod
        /// and SymSpecializedType which include type info.
        /// </summary>        
        public string Name { get; private set; }

        /// <summary>
        /// Prefix for the kind of symbol. 
        /// </summary>
        protected abstract string Separator { get; }

        /// <summary>
        /// Only for type names with generic parameters.  A backtick followed
        /// by a number (e.g. "`2" for a type with two generic parameters).
        /// </summary>
        public virtual string Suffix => "";

        public bool HasToken => mToken != null && mFile != null;

        /// <summary>
        /// Field or parameter type name (not applicable to types or methods, etc.)
        /// </summary>
        public string TypeName = "";

        /// <summary>
        /// Applicable to Types and Methods
        /// </summary>
        public Dictionary<string, string[]> Constraints = new Dictionary<string, string[]>();

        public bool IsInterface
            => this is SymType && Qualifiers.Contains("interface")
                || this is SymSpecializedType && Parent is SymType && Parent.Qualifiers.Contains("interface");

        /// <summary>
        /// Source code token if it exists.  Throws an exception for
        /// SymMethodGroup, SymSpecializedType, SymModule and built
        /// in SymType's like "$extension" and "ref"
        /// </summary>
        public Token Token
        {
            get
            {
                if (mToken == null)
                {
                    Debug.Assert(false);
                    throw new Exception($"Invalid symbol location for '{Kind}' named '{FullName}'");
                }
                return mToken;
            }
        }

        /// <summary>
        /// Source code token if it exists.  Throws an exception for
        /// SymMethodGroup, SymSpecializedType, SymModule and built
        /// in SymType's like "$extension" and "ref"
        /// </summary>
        public string File
        {
            get
            {
                if (mFile == null)
                {
                    Debug.Assert(false);
                    throw new Exception($"Invalid symbol location for '{Kind}' named '{FullName}'");
                }
                return mFile;
            }
        }

        /// <summary>
        /// Read-only access to the children.  The key always matches
        /// the child symbol Name.
        /// </summary>
        public RoDict<string, Symbol> Children { get; private set; }

        /// <summary>
        /// Create a symbol that is unique in the soruce code (e.g. SymMethod,
        /// SymType, SymField, etc.) and can be marked with token information.
        /// </summary>
        public Symbol(Symbol parent, string file, Token token, string name = null)
        {
            Children = new RoDict<string, Symbol>(mChildren);
            Parent = parent;
            Name = name == null ? token.Name : name;
            mFile = file;
            mToken = token;
        }

        /// <summary>
        /// Create a symbol that is non-existent or not unique in the source
        /// code (e.g. SymMethodGroup, SymSpecializedType, SymModule,
        /// and built-in types like "$extension", "ref", "*", etc.)
        /// </summary>
        public Symbol(Symbol parent, string name)
        {
            Children = new RoDict<string, Symbol>(mChildren);
            Parent = parent;
            Name = name;
        }

        /// <summary>
        /// Do not set the symbol name after it has been added to the symbol table.
        /// </summary>
        public void SetName(string name)
        {
            Debug.Assert(!Parent.Children.ContainsKey(Name));
            Name = name;
            mFullNameCache = null;
        }
        
        /// <summary>
        /// This should only be called by functions in SymbolTable.
        /// It sets the symbol Order to the number of children.
        /// </summary>
        internal void SetChildInternal(Symbol value)
        {
            value.Order = mChildren.Count;
            Debug.Assert(!mChildren.ContainsKey(value.Name));
            mChildren[value.Name] = value;
            mFullNameCache = null;
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
                if (Parent == null || Parent.Name == "")
                    mFullNameCache = Name + Suffix;
                else
                    mFullNameCache = Parent.FullName + Separator + Name + Suffix;
                return mFullNameCache;
            }
        }

        /// <summary>
        /// Return the simple name of this symbol (methods use the group, not the full type name)
        /// </summary>
        public string SimpleName
            => this is SymMethod ? Parent.Name : Name;

        public override string ToString()
        {
            return FullName;
        }

        public bool HasQualifier(string qualifier)
        {
            return Qualifiers.Contains(qualifier);
        }

        public List<Symbol> FindChildren(string qualifier)
        {
            var children = new List<Symbol>();
            foreach (var child in Children.Values)
                if (child.Qualifiers.Contains(qualifier))
                    children.Add(child);
            return children;
        }

        public int CountChildren(string qualifier)
        {
            var count = 0;
            foreach (var child in Children.Values)
                if (child.Qualifiers.Contains(qualifier))
                    count++;
            return count;
        }

        /// <summary>
        /// Get generic parameter count at this level.
        /// </summary>
        public int GenericParamCount()
        {
            return CountChildren("type_param");
        }

        /// <summary>
        /// Get total generic parameter count, including encolsing scopes:
        /// (e.g, In`MyClass<T1,T2>.MyFunc<T>()`, MyFunc expects 3 parameters)
        /// </summary>
        public int GenericParamTotal()
        {
            var count = GenericParamCount();
            var p = Parent;
            while (p is SymType || p is SymMethod || p is SymMethodGroup)
            {
                count += p.GenericParamCount();
                p = p.Parent;
            }
            return count;
        }


        public struct RoDict<TKey, TValue>
        {
            Dictionary<TKey, TValue> Map;
            public RoDict(Dictionary<TKey, TValue> map) { Map = map; }
            public int Count => Map.Count;
            public bool ContainsKey(TKey key) => Map.ContainsKey(key);
            public bool TryGetValue(TKey key, out TValue symbol) => Map.TryGetValue(key, out symbol);
            public TValue this[TKey key] => Map[key];
            public Dictionary<TKey, TValue>.ValueCollection Values => Map.Values;
            public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => Map.GetEnumerator();
            public override string ToString() => Map.ToString();
        }


    }

    class SymModule : Symbol
    {
        public SymModule(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "module";
        protected override string Separator => ".";
    }

    class SymType : Symbol
    {
        public SymType(Symbol parent, string file, Token token, string name = null) : base(parent, file, token, name) { }
        public SymType(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "type";
        protected override string Separator => ".";

        public override string Suffix
        {
            get
            {
                var count = CountChildren("type_param");
                return count == 0 ? "" : $"`{count}";
            }
        }

    }

    class SymTypeParam : Symbol
    {
        public SymTypeParam(Symbol parent, string file, Token token) : base(parent, file, token)
        {
        }
        public override string Kind => "type parameter";
        protected override string Separator => "~";

        public int GenericParamNum() => Parent.Parent.GenericParamTotal() + Order;
    }

    class SymField : Symbol
    {
        public SymField(Symbol parent, string file, Token token) : base(parent, file, token) { }
        public override string Kind => "field";
        protected override string Separator => "@";
    }

    /// <summary>
    /// This is strictly internal to the compiler, so we can deal with
    /// overloaded functions.
    /// </summary>
    class SymMethodGroup : Symbol
    {
        public SymMethodGroup(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "method group";
        protected override string Separator => ".";

    }

    class SymMethod : Symbol
    {
        public SymMethod(Symbol parent, string file, Token token, string name) : base(parent, file, token, name) { }
        public override string Kind => "method";
        protected override string Separator => ""; // The method group is the separator

        public bool IsGetter => Qualifiers.Contains("get") || Qualifiers.Contains("aget");
        public bool IsSetter => Qualifiers.Contains("set") || Qualifiers.Contains("aset");
        public bool IsFunc => Qualifiers.Contains("fun") || Qualifiers.Contains("afun");
    }

    class SymMethodParam : Symbol
    {
        public SymMethodParam(Symbol parent, string file, Token token, bool isReturn) : base(parent, file, token)
        {
            IsReturn = isReturn;
        }
        public SymMethodParam(Symbol parent, string file, Token token, string name, bool isReturn) : base(parent, file, token, name)
        {
            IsReturn = isReturn;
        }
        public override string Kind => "method parameter";
        protected override string Separator => "~";
        public bool IsReturn { get; private set; }
    }

    /// <summary>
    /// Parent is the full name of the generic type, typeParams are the full name of each argument.
    /// The symbol name is a combination of both parentName<T0,T1,T2...>
    /// or for generic functions fun(T0,T1)(R0,R1)
    /// </summary>
    class SymSpecializedType : Symbol
    {
        public readonly Symbol[] Params;
        public readonly Symbol[] Returns;

        public override string Kind => "specialized type";
        protected override string Separator => "";

        // Constructor for generic type argument
        public SymSpecializedType(Symbol parent, string name)
            : base(parent, name)
        {
            Params = Array.Empty<Symbol>();
            Returns = Array.Empty<Symbol>();
        }


        // Constructor for generic type 'F<T>' or function 'F<p1,p2...><r1,r2...>'
        public SymSpecializedType(Symbol parent, Symbol[] typeParams, Symbol[] typeReturns = null)
            : base(parent, FullTypeParamNames(typeParams, typeReturns))
        {
            Debug.Assert(parent is SymType);
            Params = typeParams;
            Returns = typeReturns != null ? typeReturns : Array.Empty<Symbol>();
        }

        public static string FullTypeParamNames(Symbol[] typeParams, Symbol[] typeReturns)
        {
            if (typeReturns == null || typeReturns.Length == 0)
                return "<" + TypeParamNames(typeParams) + ">";
            return "<" + TypeParamNames(typeParams) + "><" + TypeParamNames(typeReturns) + ">";
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
