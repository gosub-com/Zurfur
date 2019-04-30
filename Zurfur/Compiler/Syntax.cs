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
        public List<SyntaxNamespace> Namespaces = new List<SyntaxNamespace>();
        public SyntaxNamespace CurrentNamespace { get { return Namespaces.Count == 0 ? null : Namespaces[Namespaces.Count - 1]; } }
    }

    class SyntaxNamespace
    {
        public string[] Comments;
        public Token Keyword;
        public SyntaxExpr QualifiedIdentifiers;
        public List<SyntaxClass> Classes = new List<SyntaxClass>();
        public List<SyntaxFunc> Funcs = new List<SyntaxFunc>();
    }

    class SyntaxUsing
    {
        public Token Keyword;
        public SyntaxExpr QualifiedIdentifiers;
    }

    class SyntaxClass // or struct or interface
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword;
        public SyntaxExpr BaseClass;
        public SyntaxExpr ClassName;
        public SyntaxExpr Alias;
        public SyntaxConstraint []Constraints;
        public List<SyntaxClass> Classes = new List<SyntaxClass>();
        public List<SyntaxFunc> Funcs = new List<SyntaxFunc>();
        public List<SyntaxField> Fields = new List<SyntaxField>();
    }

    class SyntaxConstraint
    {
        public Token Keyword;  // Where
        public Token Typename;
        public SyntaxExpr[] QualifiedIdentifiers;
    }

    class SyntaxField
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Name;
        public SyntaxExpr TypeName;
        public SyntaxExpr InitExpr;
    }

    class SyntaxFunc
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword;
        public SyntaxExpr ClassName;
        public Token GetOrSetToken;
        public SyntaxExpr FuncName;
        public SyntaxFuncParam[] Params;
        public SyntaxExpr Return;
        public SyntaxExpr Statements;
    }

    class SyntaxFuncParam
    {
        public Token[] Qualifiers;
        public Token Name;
        public SyntaxExpr TypeName;
    }

}