using System;
using System.Collections.Generic;
using System.Text;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class SyntaxUnit
    {
        public List<SyntaxUsing> Using = new List<SyntaxUsing>();
        public SyntaxNamespace CurrentNamespace { get { return Namespaces.Count == 0 ? null : Namespaces[Namespaces.Count - 1]; } }

        public List<SyntaxNamespace> Namespaces = new List<SyntaxNamespace>();
        public List<SyntaxType> Types = new List<SyntaxType>();
        public List<SyntaxFunc> Funcs = new List<SyntaxFunc>();
        public List<SyntaxField> Fields = new List<SyntaxField>();
    }

    class SyntaxUsing
    {
        public Token Keyword;
        public Token[] NamePath = Token.EmptyArray;
    }

    class SyntaxScope
    {
        public virtual bool IsNamespace => false;
        public virtual bool IsType => false;
        public SyntaxScope ParentScope;
        public string Comments;
        public Token[] Qualifiers;
        public Token Keyword; // class, struct, func, prop, blank for field, etc.
        public Token Name;

        virtual public string FullName
        {
            get
            {
                var name = Name == null ? "(none)" : Name;
                if (ParentScope != null)
                    name = ParentScope.FullName + "." + name;
                return name;
            }
        }
        public override string ToString() => FullName;
    }

    class SyntaxNamespace : SyntaxScope
    {
        public override bool IsNamespace => true;
    }

    /// <summary>
    /// Includes struct, enum, interface
    /// </summary>
    class SyntaxType : SyntaxScope
    {
        public override bool IsType => true;
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
        public override string FullName => base.FullName + "::" + Name;

        public SyntaxField()
        {
            Keyword = Token.Empty;
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