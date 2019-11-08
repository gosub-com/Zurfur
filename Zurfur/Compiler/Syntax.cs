using System;
using System.Collections.Generic;
using System.Text;


namespace Gosub.Zurfur.Compiler
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
        public string Comments;
        public Token Name => QualifiedIdentifiers.Length == 0 ? new Token("(none)", 0, 0) : QualifiedIdentifiers[QualifiedIdentifiers.Length - 1];
        public Token Keyword;
        public Token[] QualifiedIdentifiers = Token.EmptyArray;
        public SyntaxNamespace ParentNamespace;

        string mFullName;
        public string FullName => ToString();

        public override string ToString()
        {
            if (mFullName != null)
                return mFullName;
            mFullName = "";
            for (int i = 0; i < QualifiedIdentifiers.Length;  i++)
                mFullName = mFullName + QualifiedIdentifiers[i] + (i == QualifiedIdentifiers.Length-1 ? "" : ".");
            if (ParentNamespace != null)
                mFullName = ParentNamespace.FullName + "." + mFullName;
            return mFullName;
        }

    }

    class SyntaxUsing
    {
        public Token Keyword;
        public Token[] QualifiedIdentifiers = Token.EmptyArray;
    }

    class SyntaxClass // or struct, enum, or interface
    {
        public SyntaxNamespace Namespace;
        public SyntaxClass ParentClass;
        public string Comments;
        public Token[] Qualifiers;
        public Token Keyword; // class, struct, etc.
        public SyntaxExpr []BaseClasses;
        public Token Name;
        public SyntaxExpr TypeParams;
        public SyntaxExpr Alias;
        public SyntaxConstraint []Constraints;

        public string FullName => ToString();
        string mFullName;

        public override string ToString()
        {
            if (mFullName != null)
                return mFullName;
            mFullName = Name == null ? "(none)" : Name.ToString();
            if (ParentClass != null)
                mFullName = ParentClass.FullName + "." + mFullName;
            else
                mFullName = (Namespace == null ? "(none)" : Namespace.ToString()) + "." + mFullName;
            return mFullName;
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
        public string Comments;
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
        public string Comments;
        public Token[] Qualifiers;
        public Token Keyword; // func, afunc, prop, this, construct, etc.
        public SyntaxExpr ClassName;
        public Token Name;
        public SyntaxExpr TypeParams;
        public SyntaxExpr Params;
        public SyntaxExpr ReturnType;
        public SyntaxConstraint[] Constraints;
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