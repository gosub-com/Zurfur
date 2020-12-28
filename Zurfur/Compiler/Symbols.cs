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

        /// <summary>
        /// Short name
        /// </summary>
        public string Name { get; private set; } = "";

        /// <summary>
        /// Fully qualified name, except for type arguments which are short
        /// </summary>
        public string FullName { get; protected set; } = "";

        public string Comments = "";

        public Symbol Parent { get; private set; }

        public List<SymLoc> Locations = new List<SymLoc>();
        public Dictionary<string, Symbol> Children = new Dictionary<string, Symbol>();

        List<Symbol> mDuplicates;

        public Symbol(Symbol parent, SymFile file, Token token)
        {
            Parent = parent;
            Name = token.Name;
            FullName = GetFullName();
            AddLocation(file, token);
        }

        public Symbol(Symbol parent, string name)
        {
            Parent = parent;
            Name = name;
            FullName = GetFullName();
        }

        /// <summary>
        /// WARNING: Use with care.  Don't try to rename a symbol that has
        /// already been added to the table!  Everything will get messed up.
        /// This will fix the symbol FullName and also the FullName of
        /// the children, but it won't attempt to fix the parent.
        /// </summary>
        public void SetName(string name)
        {
            Name = name;
            FullName = GetFullName();
            foreach (var child in Children.Values)
                child.SetName(child.Name);
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

        public bool IsDuplicte => mDuplicates != null;

        public void AddDuplicate(Symbol symbol)
        {
            if (mDuplicates == null)
                mDuplicates = new List<Symbol>();
            mDuplicates.Add(symbol);
        }

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
        public override string Kind => "empty";
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

        // The following is redundant (do not serialize, call SetChildInfo)
        public string[] TypeArgs;
        public string TypeArgNames() => TypeArgs.Length == 0 ? "" : "<" + string.Join(",", TypeArgs) + ">";

        /// <summary>
        /// Call this only once to fill in the redundant information
        /// </summary>
        public void SetChildInfo()
        {
            var ta = FindChildren<SymTypeArg>();
            ta.Sort((a, b) => a.Order.CompareTo(b.Order));
            TypeArgs = ta.ConvertAll(a => a.Name).ToArray();
        }
    }


    class SymTypeArg : SymType
    {
        public SymTypeArg(Symbol parent, SymFile file, Token token, int index) : base(parent, file, token)
        {
            Order = index;
        }
        public override string Kind => "type argument";

        public int Order;

        protected override string GetFullName()
        {
            return Name;
        }
    }

    /// <summary>
    /// Parent is the full name of the generic type, typeParams are the full name of each argument.
    /// The symbol name is a combination of both parentName<T0,T1,T2...>
    /// or for generic functions fun(T0,T1)->(R0,R1)
    /// </summary>
    class SymSpecializedType : SymType
    {
        public readonly string[] Params;
        public readonly string[] Returns;

        public SymSpecializedType(string name, string[] typeParams)
            : base(new SymEmpty(name), "<" + TypeArgsFullName(typeParams) + ">") 
        {
            Params = typeParams;
            Returns = Array.Empty<string>();
        }
        public SymSpecializedType(string name, string[] typeParams, string []typeReturns)
            : base(new SymEmpty(name), ParamsFuncFullName(typeParams, typeReturns))
        {
            Params = typeParams;
            Returns = typeReturns;
        }

        public static string ParamsFuncFullName(string []typeParams, string []typeReturns)
        {
            return "(" + TypeArgsFullName(typeParams) + ")->(" + TypeArgsFullName(typeReturns) + ")";
        }

        static string TypeArgsFullName(string []typeParams)
        {
            if (typeParams.Length == 0)
                return "";
            if (typeParams.Length == 1)
                return typeParams[0];
            StringBuilder sb = new StringBuilder();
            sb.Append(typeParams[0]);
            for (int i = 1;  i < typeParams.Length;  i++)
            {
                sb.Append(",");
                sb.Append(typeParams[i]);
            }
            return sb.ToString();
        }

    }

    class SymField : Symbol
    {
        public SymField(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "field";
        public SyntaxField Syntax;
        public SymType TypeName;
    }

    class SymMethodGroup : Symbol
    {
        public SymMethodGroup(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "methods";
    }

    class SymMethod : Symbol
    {
        // The actual name is the method group (this name will be $1, etc.)
        public SymMethod(Symbol parent, string name) : base(parent, name) { }
        public override string Kind => "method";

        // The following is redundant (do not serialize, call SetChildInfo)
        public string[] TypeArgs;
        public string ParamTypeNames = "";
        public string ReturnTypeNames = "";
        public SymMethodArg[] Params;
        public SymMethodArg[] Returns;
        public string TypeArgNames() => TypeArgs.Length == 0 ? "" : "<" + string.Join(",", TypeArgs) + ">";


        /// <summary>
        /// Call this only once to fill in the redundant information
        /// </summary>
        public void SetChildInfo()
        {
            var ta = FindChildren<SymTypeArg>();
            ta.Sort((a,b) => a.Order.CompareTo(b.Order));
            TypeArgs = ta.ConvertAll(a => a.Name).ToArray();
            var p = FindChildren<SymMethodArg>();
            p.Sort((a, b) => a.Order.CompareTo(b.Order));
            Params = p.FindAll(a => !a.IsReturn).ToArray();
            Returns = p.FindAll(a => a.IsReturn).ToArray();

            ParamTypeNames = "(" + string.Join(",", Array.ConvertAll(Params, a => a.TypeName.ToString())) + ")";
            ReturnTypeNames = "(" + string.Join(",", Array.ConvertAll(Returns, a => a.TypeName.ToString())) + ")";
        }
    }

    class SymMethodArg : Symbol
    {
        public SymMethodArg(Symbol parent, SymFile file, Token token, int order) : base(parent, file, token)
            { Order = order; }
        public override string Kind => "parameter";

        public bool IsReturn;
        public int Order;
        public SymType TypeName;
    }
}
