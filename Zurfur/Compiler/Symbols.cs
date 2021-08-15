using System;
using System.Collections.Generic;
using System.Text;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class SymFile
    {
        public string Name = "";
        public string Path = "";
        public SyntaxFile SyntaxFile;
        public string[] Use = new string[0];

        public SymFile(string path, SyntaxFile syntaxFile)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            SyntaxFile = syntaxFile;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Symbol location (file + token)
    /// </summary>
    readonly struct SymLoc
    {
        public readonly SymFile File;
        public readonly Token Token;
        public SymLoc(SymFile file, Token token) { File = file; Token = token; }
    }

    abstract class Symbol
    {
        public abstract string Kind { get; }

        public Symbol Parent { get; private set; }
        string mName = "";
        string mFullNameCache;
        public string Comments = "";
        public string[] Qualifiers = Array.Empty<string>();
        public List<SymLoc> Locations = new List<SymLoc>();
        public Dictionary<string, Symbol> Children = new Dictionary<string, Symbol>();

        public Symbol(Symbol parent, SymFile file, Token token)
        {
            Parent = parent;
            mName = token.Name;
            AddLocation(file, token);
        }

        public Symbol(Symbol parent, string name)
        {
            Parent = parent;
            mName = name;
        }

        /// <summary>
        /// Short name
        /// </summary>
        public string Name
        {
            get { return mName; }
            set
            {
                mName = value;
                ClearFullNameCache();
            }
        }

        void ClearFullNameCache()
        {
            mFullNameCache = null;
            foreach (var child in Children.Values)
                child.ClearFullNameCache();
        }

        /// <summary>
        /// Fully qualified name (namespaces, types, functions, and fields) or short name (parameters)
        /// </summary>
        public string FullName
        {
            get
            {
                if (mFullNameCache == null)
                    mFullNameCache = GetFullName();
                return mFullNameCache;
            }
        }

        protected virtual string GetFullName()
        {
            if (Parent == null || Parent.Name == "")
                return Name;
            return Parent.FullName + "." + Name;
        }

        public void AddLocation(SymFile file, Token token)
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
        public SymFile File => Location.File;

        public override string ToString()
        {
            return FullName;
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
        public SymNamespace(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "namespace";
    }

    /// <summary>
    /// Class, struct, enum, interface
    /// </summary>
    class SymType : Symbol
    {
        public string TypeKeyword = "";
        public SymType(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public SymType(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "type";

        protected override string GetFullName()
        {
            var tp = FindChildren<SymTypeParam>();
            return Parent.FullName + "." + Name + (tp.Count == 0 ? "" : $"`{tp.Count}");
        }
    }

    class SymTypeParam : SymType
    {
        public SymTypeParam(Symbol parent, SymFile file, Token token, int index) : base(parent, file, token)
        {
            Order = index;
        }
        public override string Kind => "type parameter";
        public int Order;

        protected override string GetFullName()
        {
            return "!" + Order;
        }
    }

    class SymField : Symbol
    {
        public SymField(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "field";
        public SymType TypeName;
    }

    class SymMethodGroup : Symbol
    {
        public SymMethodGroup(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "methods";
    }

    class SymMethod : Symbol
    {
        public SymMethod(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "method";

        protected override string GetFullName()
        {
            return Parent + Name;
        }
    }

    class SymMethodParam : Symbol
    {
        public SymMethodParam(Symbol parent, SymFile file, Token token, int order) : base(parent, file, token)
            { Order = order; }
        public override string Kind => "parameter";

        public bool IsReturn;
        public int Order;
        public SymType TypeName;

        protected override string GetFullName()
        {
            return "#" + Name;
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

        public SymSpecializedType(string name, string[] typeParams)
            : base(new SymEmpty(name), "<" + TypeParamsFullName(typeParams) + ">")
        {
            Params = typeParams;
            Returns = Array.Empty<string>();
        }
        public SymSpecializedType(string name, string[] typeParams, string[] typeReturns)
            : base(new SymEmpty(name), ParamsFuncFullName(typeParams, typeReturns))
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
