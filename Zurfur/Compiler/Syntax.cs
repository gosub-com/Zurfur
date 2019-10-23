using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Gosub.Zurfur
{
    class SyntaxUnit
    {
        public List<SyntaxUsing> Using = new List<SyntaxUsing>();
        public SyntaxNamespace CurrentNamespace { get { return Namespaces.Count == 0 ? null : Namespaces[Namespaces.Count - 1]; } }

        public List<SyntaxNamespace> Namespaces = new List<SyntaxNamespace>();
        public List<SyntaxClass> Classes = new List<SyntaxClass>();
        public List<SyntaxFunc> Funcs = new List<SyntaxFunc>();
        public List<SyntaxField> Fields = new List<SyntaxField>();
    }

    class SyntaxNamespace
    {
        public string[] Comments;
        public Token Keyword;
        public SyntaxExpr QualifiedIdentifiers;

        public override string ToString()
        {
            if (QualifiedIdentifiers == null)
                return "(namespace)";
            var s = "";
            for (int i = 0; i < QualifiedIdentifiers.Count; i++)
                s += QualifiedIdentifiers[i].Token + (i == QualifiedIdentifiers.Count - 1 ? "" : ".");
            return s;
        }
    }

    class SyntaxUsing
    {
        public Token Keyword;
        public SyntaxExpr QualifiedIdentifiers;
    }

    class SyntaxClass // or struct, enum, or interface
    {
        public SyntaxNamespace Namespace;
        public SyntaxClass ParentClass;
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword; // class, struct, etc.
        public SyntaxExpr []BaseClasses;
        public Token Name;
        public SyntaxExpr TypeParams;
        public SyntaxExpr Alias;
        public SyntaxConstraint []Constraints;

        public override string ToString()
        {
            return Name == null ? "(class)" : Name.ToString();
        }
    }

    class SyntaxConstraint
    {
        public Token Keyword;  // Where
        public Token GenericTypeName;
        public SyntaxExpr []TypeNames;
    }

    class SyntaxField
    {
        public SyntaxNamespace Namespace;
        public SyntaxClass ParentClass;
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Name;
        public SyntaxExpr TypeName;
        public Token InitToken;
        public SyntaxExpr InitExpr;

        public override string ToString()
        {
            return Name == null ? "(token)" : Name;
        }
    }

    class SyntaxFunc
    {
        public SyntaxNamespace Namespace;
        public SyntaxClass ParentClass;
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword; // func, afunc, prop, this, construct, etc.
        public SyntaxExpr ClassName;
        public Token Name;
        public SyntaxExpr TypeParams;
        public SyntaxExpr Params;
        public SyntaxExpr ReturnType;
        public SyntaxExpr Statements;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Name == null ? "(func)" : Name.ToString());
            sb.Append("(");
            if (Params != null)
                for (int i = 0; i < Params.Count; i++)
                    sb.Append(Params[i].ToString() + (i == Params.Count - 1 ? "" : ","));
            sb.Append(")");
            return sb.ToString();
        }
    }

}