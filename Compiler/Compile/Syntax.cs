using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Gosub.Lex;

namespace Zurfur.Compiler;

record class SyntaxFile
{
    public Lexer Lexer { get; init; } = new();
    public Dictionary<string, SyntaxPragma> Pragmas { get; init; } = new();
    public List<SyntaxUsing> Using { get; init; } = new();
    public Dictionary<string, SyntaxModule> Modules { get; init; } = new();
    public List<SyntaxType> Types { get; init; } = new();
    public List<SyntaxFunc> Functions { get; init; }  = new();
    public List<SyntaxField> Fields { get; init; } = new();
}

record class SyntaxPragma(Token Name);

record class SyntaxUsing(Token[] ModuleName, Token[] Symbols);

record class SyntaxScope : SyntaxExpr
{
    public SyntaxScope(Token keyword, Token name)
        : base(keyword, 0)
    {
        Name = name;
    }

    public SyntaxScope(Token keyword, Token name, int count)
        : base(keyword, count)
    {
        Name = name;
    }

    public override SyntaxExpr this[int index]
        =>  throw new IndexOutOfRangeException();

    public override IEnumerator<SyntaxExpr> GetEnumerator()
    {
        yield break;
    }


    public Token Keyword => Token; // class, struct, func, prop, blank for field, etc.
    public SyntaxScope? Parent { get; init; }
    public string? Comments;
    public Token[] Qualifiers = Token.EmptyArray;
    public Token Name {  get; init; }

    public string FullName
    {
        get
        {
            return Parent == null ? Name : Parent.FullName + "." + Name;
        }
    }
    public override string ToString() => FullName;

}

record class SyntaxModule : SyntaxScope
{
    public SyntaxModule(Token keyword, Token name, SyntaxScope? parent)
        : base(keyword, name)
    {
        Parent = parent;
    }
}

/// <summary>
/// Includes struct, enum, interface, impl
/// </summary>
record class SyntaxType : SyntaxScope
{
    public SyntaxType(Token keyword, Token name)
        : base(keyword, name)
    {
    }

    public bool Simple;
    public SyntaxExpr? Alias;
    public SyntaxExpr? TypeArgs { get; init; }
    public SyntaxConstraint[]? Constraints { get; init; }
}

record class SyntaxConstraint(Token? TypeName, SyntaxExpr[]? TypeConstraints);

record class SyntaxField : SyntaxScope
{
    public SyntaxField(Token name)
        : base(name, name)
    {
    }

    public bool Simple { get; init; }
    public SyntaxExpr? TypeName { get; init; }
    public SyntaxExpr? Initializer { get; init; }
}

record class SyntaxFunc : SyntaxScope
{
    public SyntaxFunc(Token keyword, Token name)
        : base(keyword, name, 1)
    {
    }
    public override IEnumerator<SyntaxExpr> GetEnumerator()
    {
        if (Statements != null)
            yield return Statements;
    }

    public SyntaxExpr? ReceiverParam { get; init; }
    public SyntaxExpr? TypeArgs { get; init; }
    public SyntaxExpr? FunctionSignature { get; init; }
    public SyntaxConstraint[]? Constraints { get; init; }
    public SyntaxExpr? Statements;
}