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
        public Dictionary<string, SyntaxModule> Modules = new Dictionary<string, SyntaxModule>();
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
        public Token[] ModuleName = Token.EmptyArray;
        public Token[] Symbols = Token.EmptyArray;
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
        public string Comments;
        public Token[] Qualifiers;
        public Token Name;

        public string FullName
        {
            get
            {
                return ParentScope == null ? Name : ParentScope.FullName + "." + Name;
            }
        }
        public override string ToString() => FullName;

        /// <summary>
        /// Returns the impl type if both ImplInterface and
        /// ImplType are valid, otherwise returns null.
        /// </summary>
        public SyntaxType GetImplType()
        {
            if (!(this is SyntaxType implSyntax))
                return null;
            if (implSyntax.ImplInterface == null || implSyntax.ImplType == null)
                return null;
            return implSyntax;
        }
    }

    class SyntaxModule : SyntaxScope
    {
        public SyntaxModule(Token keyword, Token name, SyntaxScope parent)
            : base(keyword)
        {
            ParentScope = parent;
            Name = name;
        }
    }

    /// <summary>
    /// Includes struct, enum, interface, impl
    /// </summary>
    class SyntaxType : SyntaxScope
    {
        public SyntaxType(Token keyword)
            : base(keyword)
        {
        }

        public bool Simple;
        public SyntaxExpr Alias;
        public SyntaxExpr TypeArgs;
        public SyntaxConstraint []Constraints;

        public SyntaxExpr ImplType;
        public SyntaxExpr ImplInterface;
    }

    class SyntaxConstraint
    {
        public Token TypeName;
        public SyntaxExpr []TypeConstraints;
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
    }


}