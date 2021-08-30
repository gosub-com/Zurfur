using System;
using System.Collections.Generic;
using System.Text;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Symbol location (file + token)
    /// </summary>
    readonly struct SymLoc
    {
        public readonly string File;
        public readonly Token Token;
        public SymLoc(string file, Token token) { File = file; Token = token; }
    }

    abstract class Symbol
    {
        public abstract string Kind { get; }

        public Symbol Parent { get; }
        public string Name { get; }
        public int Order { get; private set; } = -1;
        public string Comments = "";
        public string[] Qualifiers = Array.Empty<string>();
        public List<SymLoc> Locations = new List<SymLoc>();
        
        Dictionary<string, Symbol> mChildren = new Dictionary<string, Symbol>();
        public RoDict<string, Symbol> Children { get; private set; }

        public Symbol(Symbol parent, string file, Token token)
        {
            Children = new RoDict<string, Symbol>(mChildren);
            Parent = parent;
            Name = token.Name;
            AddLocation(file, token);
        }

        public Symbol(Symbol parent, string name)
        {
            Children = new RoDict<string, Symbol>(mChildren);
            Parent = parent;
            Name = name;
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
        }

        /// <summary>
        /// This should only be called by functions in SymbolTable.
        /// It sets the symbol Order to the number of children.
        /// </summary>
        internal void SetChildInternal(string key, Symbol value)
        {
            value.Order = mChildren.Count;
            mChildren[key] = value;
        }


        public virtual string GetFullName()
        {
            if (Parent == null || Parent.Name == "")
                return Name;
            return Parent.GetFullName() + "." + Name;
        }

        public void AddLocation(string file, Token token)
        {
            Locations.Add(new SymLoc(file, token));
        }

        /// <summary>
        /// Retrieve the file containing the symbol definition.
        /// Types, functions, and fields should have exactly one location.
        /// Namespaces and function groups may have multiples.
        /// Throws an exception if file location is not present or if
        /// multiple location exist.
        /// </summary>
        SymLoc Location
        {
            get
            {
                if (Locations.Count == 0)
                    throw new Exception("Symbol location cannot be found");
                if (Locations.Count > 1)
                    throw new Exception("Symbol has multiple locations");
                return Locations[0];
            }
        }

        /// <summary>
        /// Throws exception if not a unique symbol with exactly one location
        /// </summary>
        public Token Token => Location.Token;

        /// <summary>
        /// Throws exception if not a unique symbol with exactly one location
        /// </summary>
        public string File => Location.File;

        public override string ToString()
        {
            return GetFullName();
        }

        /// <summary>
        /// Find direct children of specific type
        /// </summary>
        public List<T>FindChildren<T>()
        {
            var children = new List<T>();
            foreach (var child in Children.Values)
                if (child is T symChild)
                    children.Add(symChild);
            return children;
        }

    }

    class SymEmpty : Symbol
    {
        public SymEmpty(string name) : base(null, name) { }
        public override string Kind => "Empty";
    }

    class SymNamespace : Symbol
    {
        public SymNamespace(Symbol parent, string file, Token token) : base(parent, file, token) { }
        public override string Kind => "namespace";
    }

    /// <summary>
    /// Class, struct, enum, interface
    /// </summary>
    class SymType : Symbol
    {
        public SymType(Symbol parent, string file, Token token) : base(parent, file, token) { }
        public SymType(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "type";

        public override string GetFullName()
        {
            var tp = FindChildren<SymTypeParam>();
            return Parent.GetFullName() + "." + Name + (tp.Count == 0 ? "" : $"`{tp.Count}");
        }
    }

    class SymTypeParam : Symbol
    {
        public SymTypeParam(Symbol parent, string file, Token token) : base(parent, file, token)
        {
        }
        public override string Kind => "type parameter";

        public override string GetFullName()
        {
            return "!" + Name;
        }
    }

    class SymField : Symbol
    {
        public SymField(Symbol parent, string file, Token token) : base(parent, file, token) { }
        public override string Kind => "field";
        public string TypeName;
    }

    class SymMethodGroup : Symbol
    {
        public SymMethodGroup(Symbol parent, string file, Token token) : base(parent, file, token) { }
        public override string Kind => "methods";
    }

    class SymMethod : Symbol
    {
        public SymMethod(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "method";

        public override string GetFullName()
        {
            return Parent.GetFullName() + Name;
        }
    }

    class SymMethodParam : Symbol
    {
        public SymMethodParam(Symbol parent, string file, Token token) : base(parent, file, token)
        {
        }
        public override string Kind => "parameter";

        public bool IsReturn;
        public string TypeName;

        public override string GetFullName()
        {
            return "##" + Name;
        }

    }

    /// <summary>
    /// Parent is the full name of the generic type, typeParams are the full name of each argument.
    /// The symbol name is a combination of both parentName<T0,T1,T2...>
    /// or for generic functions fun(T0,T1)(R0,R1)
    /// </summary>
    class SymSpecializedType : SymType
    {
        public readonly string[] Params;
        public readonly string[] Returns;

        public override string Kind => "specialized type";

        public SymSpecializedType(Symbol parent, string name, string[] typeParams)
            : base(parent, ":SPECIALIZED:" + name + "<" + TypeParamsFullName(typeParams) + ">")
        {
            Params = typeParams;
            Returns = Array.Empty<string>();
        }
        public SymSpecializedType(Symbol parent, string name, string[] typeParams, string[] typeReturns)
            : base(parent, ":SPECIALIZED:" + name + "." + ParamsFuncFullName(typeParams, typeReturns))
        {
            Params = typeParams;
            Returns = typeReturns;
        }

        public static string ParamsFuncFullName(string[] typeParams, string[] typeReturns)
        {
            return "(" + TypeParamsFullName(typeParams) + ")(" + TypeParamsFullName(typeReturns) + ")";
        }

        static string TypeParamsFullName(string[] typeParams)
        {
            if (typeParams.Length == 0)
                return "";
            if (typeParams.Length == 1)
                return typeParams[0];
            StringBuilder sb = new StringBuilder();
            sb.Append(typeParams[0]);
            for (int i = 1; i < typeParams.Length; i++)
            {
                sb.Append(",");
                sb.Append(typeParams[i]);
            }
            return sb.ToString();
        }

    }

}
