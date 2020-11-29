using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class SyntaxFile
    {
        public Lexer Lexer = new Lexer();
        public List<SyntaxUsing> Using = new List<SyntaxUsing>();
        public Dictionary<string, SyntaxNamespace> Namespaces = new Dictionary<string, SyntaxNamespace>();
        public List<SyntaxType> Types = new List<SyntaxType>();
        public List<SyntaxFunc> Methods = new List<SyntaxFunc>();
        public List<SyntaxField> Fields = new List<SyntaxField>();
    }

    class SyntaxUsing
    {
        public Token Keyword;
        public Token[] NamePath = Token.EmptyArray;
    }

    class SyntaxScope : SyntaxExpr
    {
        public SyntaxScope(Token keyword)
            : base(keyword, 0)
        {
        }
        public SyntaxScope(Token keyword, int count)
            : base(keyword, count)
        {
        }

        public override SyntaxExpr this[int index]
            =>  throw new IndexOutOfRangeException();
        public override IEnumerator<SyntaxExpr> GetEnumerator()
        {
            yield break;
        }


        public Token Keyword => Token; // class, struct, func, prop, blank for field, etc.
        public virtual bool IsType => false;
        public SyntaxScope ParentScope;
        public string[] NamePath;
        public string Comments;
        public Token[] Qualifiers;
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
        public Token[] Path;
        public string Comments = "";
    }

    /// <summary>
    /// Includes struct, enum, interface
    /// </summary>
    class SyntaxType : SyntaxScope
    {
        public SyntaxType(Token keyword)
            : base(keyword)
        {
        }

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
        public SyntaxField(Token name)
            : base(name)
        {
            Name = name;
        }

        public bool Simple;
        public SyntaxExpr TypeName;
        public SyntaxExpr Initializer;

        // Only for properties
        public Token GetToken;
        public Token SetToken;
        public Token GetSetVisibilityToken;
    }

    class SyntaxFunc : SyntaxScope
    {
        public SyntaxFunc(Token keyword)
            : base(keyword, 1)
        {
        }
        public override SyntaxExpr this[int index]
        {
            get
            {
                if (index != 0)
                    throw new IndexOutOfRangeException();
                return Statements;
            }
        }
        public override IEnumerator<SyntaxExpr> GetEnumerator()
        {
            yield return Statements;
        }


        public SyntaxExpr ClassName;
        public SyntaxExpr Params; // 0: Type params, 1: Parameters, 2: Returns
        public SyntaxConstraint[] Constraints;
        public SyntaxExpr Statements;
    }


}