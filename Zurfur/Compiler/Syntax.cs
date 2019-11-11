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

    class SyntaxUsing
    {
        public Token Keyword;
        public Token[] QualifiedIdentifiers = Token.EmptyArray;
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

    class SyntaxScope
    {
        public SyntaxNamespace Namespace;
        public SyntaxClass ParentClass;
        public string Comments;
        public Token[] Qualifiers;
        public Token Keyword; // class, struct, func, prop, blank for field, etc.
        public Token Name;

        protected string mFullName;

        virtual public string FullName
        {
            get
            {
                if (mFullName != null)
                    return mFullName;
                mFullName = Name == null ? "(none)" : Name.ToString();
                if (ParentClass == null)
                    mFullName = (Namespace == null ? "(none)" : Namespace.ToString()) + "." + mFullName;
                else
                    mFullName = ParentClass.FullName + "/" + mFullName;
                return mFullName;
            }
        }
        public override string ToString() => FullName;
    }

    class SyntaxClass : SyntaxScope // or struct, enum, or interface
    {
        public SyntaxExpr []BaseClasses;
        public SyntaxExpr TypeParams;
        public SyntaxExpr Alias;
        public SyntaxConstraint []Constraints;
    }

    class SyntaxConstraint
    {
        public Token Keyword;  // Where
        public Token GenericTypeName;
        public SyntaxExpr []TypeNames;
    }

    class SyntaxField : SyntaxScope
    {
        public SyntaxExpr TypeName;
        public Token InitToken;
        public SyntaxExpr InitExpr;

        public SyntaxField()
        {
            Keyword = Token.Empty;
        }

        public override string FullName
        {
            get
            {
                if (mFullName != null)
                    return mFullName;
                mFullName = base.FullName + "::" + Name;
                return mFullName;
            }
        }

    }

    class SyntaxFunc : SyntaxScope
    {
        public SyntaxExpr ClassName;
        public SyntaxExpr TypeParams;
        public SyntaxExpr Params;
        public SyntaxExpr ReturnType;
        public SyntaxConstraint[] Constraints;
        public SyntaxExpr Statements;

    }

}