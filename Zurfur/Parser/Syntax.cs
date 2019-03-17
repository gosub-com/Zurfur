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
        public Token[] QualifiedIdentifiers;
        public List<SyntaxClass> Classes = new List<SyntaxClass>();
        public List<SyntaxFunc> Funcs = new List<SyntaxFunc>();
    }

    public class SyntaxUsing
    {
        public Token Keyword;
        public Token[] QualifiedIdentifiers;
    }

    class SyntaxClass // or struct or interface
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword;
        public SyntaxTypeName BaseClass;
        public SyntaxTypeName ClassName;
        public SyntaxTypeName Alias;
        public List<SyntaxClass> Classes = new List<SyntaxClass>();
        public List<SyntaxFunc> Funcs = new List<SyntaxFunc>();
        public List<SyntaxField> Fields = new List<SyntaxField>();


    }

    class SyntaxField
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Name;
        public SyntaxExpr Expr;
    }

    class SyntaxFunc
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword;
        public SyntaxTypeName ClassName;
        public Token GetOrSetToken;
        public SyntaxTypeName FuncName;
        public SyntaxFuncParam[] Params;
        public SyntaxTypeName Return;
        public SyntaxExpr Statements;
    }

    class SyntaxFuncParam
    {
        public Token[] Qualifiers;
        public Token Name;
        public SyntaxTypeName TypeName;
    }

    class SyntaxTypeName
    {
        static readonly SyntaxTypeName []sEmptyTypeName = new SyntaxTypeName[0];

        public Token[] Qualifiers = Token.EmptyArray;
        public Token Name;
        public SyntaxTypeName[] TypeParameters = sEmptyTypeName;
    }

}