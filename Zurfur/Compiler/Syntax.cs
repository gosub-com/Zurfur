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
        public Dictionary<string, SyntaxPragma> Pragmas = new Dictionary<string, SyntaxPragma>();
        public List<SyntaxUsing> Using = new List<SyntaxUsing>();
        public Dictionary<string, SyntaxNamespace> Namespaces = new Dictionary<string, SyntaxNamespace>();
        public List<SyntaxType> Types = new List<SyntaxType>();
        public List<SyntaxFunc> Methods = new List<SyntaxFunc>();
        public List<SyntaxField> Fields = new List<SyntaxField>();
    }

    class SyntaxPragma
    {
        public SyntaxPragma(Token token) { Name = token; }
        public Token Name;
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

        public bool Simple;
        public SyntaxExpr AliasOrExtends;
        public SyntaxExpr []Implements;
        public SyntaxExpr TypeArgs;
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

        public SyntaxExpr ExtensionType;
        public SyntaxExpr TypeArgs;
        public SyntaxExpr MethodSignature;
        public SyntaxConstraint[] Constraints;
        public SyntaxExpr Statements;

        // Only for fields/properties
        public bool IsProperty;
        public Token GetToken;
        public Token SetToken;
        public Token GetSetVisibilityToken;

    }


}