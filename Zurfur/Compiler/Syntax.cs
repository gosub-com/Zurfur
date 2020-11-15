using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class SyntaxFile
    {
        public List<SyntaxUsing> Using = new List<SyntaxUsing>();
        public Dictionary<string, SyntaxNamespace> Namesapces = new Dictionary<string, SyntaxNamespace>();
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
        public virtual bool IsType => false;
        public SyntaxScope ParentScope;
        public string[] NamePath;
        public string Comments;
        public Token[] Qualifiers;
        public Token Keyword; // class, struct, func, prop, blank for field, etc.
        public Token Name;

        public string FullName
        {
            get
            {
                return string.Join(".", NamePath) + ":" + Name;
            }
        }
        public override string ToString() => FullName;
    }

    class SyntaxNamespace
    {
        public string Comments = "";
        public List<Token> Tokens = new List<Token>();
    }

    /// <summary>
    /// Includes struct, enum, interface
    /// </summary>
    class SyntaxType : SyntaxScope
    {
        public override bool IsType => true;
        public bool Simple;
        public SyntaxExpr Extends;
        public SyntaxExpr []Implements;
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
        public bool Simple;
        public SyntaxExpr TypeName;
        public SyntaxExpr Initializer;

        // Only for properties
        public Token GetToken;
        public Token SetToken;
        public Token GetSetVisibilityToken;

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