using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Gosub.Zurfur.Lex;
using System.Diagnostics;

namespace Gosub.Zurfur.Compiler
{

    /// <summary>
    /// NOTE: This is all strictly internal to the compiler.
    /// The public definitions are contained in PackageDefinitions.cs.
    /// For example, `SymMethodGroup` doesn't exist in the header file json
    /// and type names are stored here without the separator or generic
    /// arguments parameter. (e.g. 'Range' is used here, but '/Range`1'
    /// is stored as the name in the json)
    /// 
    /// Symbol symbols:
    ///     .   Module
    ///     /   Type name
    ///     @   Field name
    ///     :   Method group (all overloads with the same name)
    ///     !   Method, followed by prototype
    ///     #   Generic argument (followed by argument number)
    ///     ~   Parameter name
    ///     `   Number of generic arguments, suffix for type name
    ///     ()  Method parameters
    ///     <>  Generic parameters
    ///     $   Special symbol, e.g. $this, $ext, etc.
    ///    
    /// Sepecial symbols (prefixed with $):
    ///     this        Implicit extension/member method parameter
    ///     return      Implicit return parameter name
    ///     ext         Extension type (container for extension methods)
    ///     fun         Function
    ///     get/aget    Prefix for getters
    ///     set/aset    Prefix for setters
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

        // Set by `SetChildInternal`.  Type parameters are always first.
        public int Order { get; private set; } = -1;
        string mFullNameCache = null;

        /// <summary>
        /// Name as it appears in the lookup table.  For most types, it is
        /// the same as the source code token.  The exceptions are SymMethod
        /// and SymParameterizedType which include type info.
        /// </summary>        
        public string Name { get; private set; }

        /// <summary>
        /// Prefix for the kind of symbol:
        ///     .   Module
        ///     /   Type name
        ///     @   Field name
        ///     :   Method group (all overloads with the same name)
        ///     !   Method, followed by prototype
        ///     #   Generic argument (followed by argument number)
        ///     ~   Parameter name
        /// </summary>
        protected abstract string Separator { get; }

        /// <summary>
        /// Only for type names with generic parameters.  A backtick followed
        /// by a number (e.g. "`2" for a type with two generic parameters).
        /// </summary>
        protected virtual string Suffix => "";

        /// <summary>
        /// Source code token if it exists.  Throws an exception for
        /// SymMethodGroup, SymParameterizedType, SymModule and built
        /// in SymType's like "$ext" and "ref"
        /// </summary>
        public Token Token
        {
            get
            {
                if (mToken == null)
                    throw new Exception($"Invalid symbol location for '{Kind}' named '{FullName}'");
                return mToken;
            }
        }

        /// <summary>
        /// Source code token if it exists.  Throws an exception for
        /// SymMethodGroup, SymParameterizedType, SymModule and built
        /// in SymType's like "$ext" and "ref"
        /// </summary>
        public string File
        {
            get
            {
                if (mFile == null)
                    throw new Exception($"Invalid symbol location for '{Kind}' named '{FullName}'");
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
        /// The name doesn't need to match the token.  e.g. For "fun A(){}",
        /// the name is "$fun()()", but the token is "A".
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
        /// code (e.g. SymMethodGroup, SymParameterizedType, SymModule,
        /// and built-in types like "$ext", "ref", "*", etc.)
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
        }

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

        public override string ToString()
        {
            return FullName;
        }

        public bool HasQualifier(string qualifier)
        {
            return Qualifiers.Contains(qualifier);
        }

        /// <summary>
        /// Find direct children of specific type
        /// </summary>
        public List<T> FindChildren<T>()
        {
            var children = new List<T>();
            foreach (var child in Children.Values)
                if (child is T symChild)
                    children.Add(symChild);
            return children;
        }

        public int CountChildren<T>()
        {
            int count = 0;
            foreach (var child in Children.Values)
                if (child is T)
                    count++;
            return count;
        }

        /// <summary>
        /// Get total generic parameter count, including encolsing scopes:
        /// (e.g, In`MyClass<T1,T2>.MyFunc<T>()`, MyFunc expects 3 parameters)
        /// </summary>
        public int GenericParamTotal()
        {
            var count = CountChildren<SymTypeParam>();
            var p = Parent;
            while (p is SymType || p is SymMethod || p is SymMethodGroup)
            {
                count += p.CountChildren<SymTypeParam>();
                p = p.Parent;
            }
            return count;
        }

        /// <summary>
        /// Get generic parameter count at this level.
        /// </summary>
        public int GenericParamCount()
        {
            return CountChildren<SymTypeParam>();
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
        public SymType(Symbol parent, string file, Token token) : base(parent, file, token) { }
        public SymType(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "type";
        protected override string Separator => "/";

        protected override string Suffix
        {
            get
            {
                var tp = FindChildren<SymTypeParam>();
                return tp.Count == 0 ? "" : $"`{tp.Count}";
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

    }

    class SymField : Symbol
    {
        public SymField(Symbol parent, string file, Token token) : base(parent, file, token) { }
        public override string Kind => "field";
        public string TypeName = "";
        protected override string Separator => "@";
    }

    /// <summary>
    /// This is strictly internal to the compiler, so we can deal with
    /// overloaded functions.
    /// </summary>
    class SymMethodGroup : Symbol
    {
        public SymMethodGroup(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "methods";
        protected override string Separator => ":";

    }

    class SymMethod : Symbol
    {
        public SymMethod(Symbol parent, string file, Token token, string name) : base(parent, file, token, name) { }
        public override string Kind => "method";
        protected override string Separator => "!";

        public bool IsGetter => Name.Contains("$get(") || Name.Contains("$aget(");
        public bool IsSetter => Name.Contains("$set(") || Name.Contains("$aset(");
        public bool IsFunc => Name.Contains("$fun(") || Name.Contains("$afun(");
    }

    class SymMethodParam : Symbol
    {
        public SymMethodParam(Symbol parent, string file, Token token) : base(parent, file, token)
        {
        }
        public override string Kind => "parameter";
        protected override string Separator => "~";
        public bool IsReturn;
        public string TypeName = "";

    }

    /// <summary>
    /// Parent is the full name of the generic type, typeParams are the full name of each argument.
    /// The symbol name is a combination of both parentName<T0,T1,T2...>
    /// or for generic functions fun(T0,T1)(R0,R1)
    /// </summary>
    class SymParameterizedType : Symbol
    {
        public readonly Symbol[] Params;
        public readonly Symbol[] Returns;

        public override string Kind => "parameterized type";
        protected override string Separator => "";

        // Constructor for generic type argument
        public SymParameterizedType(Symbol parent, string name)
            : base(parent, name)
        {
            Params = Array.Empty<Symbol>();
            Returns = Array.Empty<Symbol>();
        }


        // Constructor for generic type 'F<T>' or function 'F(p1,p2...)(r1,r2...)'
        public SymParameterizedType(Symbol parent, Symbol[] typeParams, Symbol[] typeReturns = null)
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
