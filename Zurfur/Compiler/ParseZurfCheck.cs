using System;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Check the parse tree for various errors and warnings.
    /// NOTE: Many of these errors will be moved to analysis phase
    /// </summary>
    class ParseZurfCheck
    {
        // Debug the parse tree
        bool mShowParseTree = true;

        public Token LastToken;

        static WordSet sRequireGlobalFieldQualifiers = new WordSet("const static");
        static WordSet sRequireGlobalFuncQualifiers = new WordSet("static");
        static WordSet sFuncInInterfaceQualifiersAllowed = new WordSet("pub private protected static mut async");

        static WordSet sInterfaceQualifiers = new WordSet("pub public protected private internal static");
        static WordSet sClassQualifiers = new WordSet("pub pfublic protected private internal unsafe unsealed abstract ro boxed");
        static WordSet sStructQualifiers = new WordSet("pub public protected private internal unsafe ref ro");
        static WordSet sEnumQualifiers = new WordSet("pub public protected private internal");

        static WordSet sFieldInStructQualifiers = new WordSet("pub public protected private internal unsafe static ro const var mut @");
        static WordSet sFieldInClassQualifiers = new WordSet("pub public protected private internal unsafe static ro const var mut @");
        static WordSet sFieldInEnumQualifiers = new WordSet("");

        static WordSet sFuncQualifiers = new WordSet("pub public protected private internal unsafe virtual override new mut static async");
        static WordSet sPropQualifiers = new WordSet("pub public protected private internal unsafe static virtual override new");

        static WordMap<int> sClassFuncFieldQualifiersOrder = new WordMap<int>()
        {
            { "pub", 1 }, { "public", 1 }, { "protected", 1 }, { "private", 1 }, { "internal", 1 },
            { "unsafe", 2 },
            { "static", 4 },  {"const", 4 },
            { "unsealed", 6 },
            { "abstract", 8 }, { "virtual", 8},  { "override", 8 }, { "new", 8 },
            { "ref", 10},
            { "mut", 11 }, { "ro", 11}, {"readonly", 11},
            { "async", 12 }
        };

        ParseZurf mParser;

        public ParseZurfCheck(ParseZurf parser)
        {
            mParser = parser;
        }

        public void Check(SyntaxFile unit)
        {
            LastToken = null;
            CheckParseTree(unit);
            LastToken = null;
            ShowParseTree(unit);
        }

        void CheckParseTree(SyntaxFile unit)
        {
            LastToken = null;
            foreach (var aClass in unit.Types)
            {
                LastToken = aClass.Keyword;
                CheckQualifiers(aClass.ParentScope, aClass.Name, aClass.Qualifiers);

                var keyword = aClass.Keyword;
                var outerKeyword = aClass.ParentScope == null ? "" : aClass.ParentScope.Keyword;
                if (outerKeyword == "")
                    mParser.RejectToken(keyword, "The namespace must be defined before the " + keyword);
                if (outerKeyword != "" && outerKeyword == "enum")
                    mParser.RejectToken(keyword, "Classes, structs, enums, and interfaces may not be nested inside an enum");

                switch (keyword)
                {
                    case "enum":
                        RejectQualifiers(aClass.Qualifiers, sEnumQualifiers, "Qualifier does not apply to enums");
                        break;
                    case "interface":
                        RejectQualifiers(aClass.Qualifiers, sInterfaceQualifiers, "Qualifier does not apply to interfaces");
                        break;
                    case "struct":
                        RejectQualifiers(aClass.Qualifiers, sStructQualifiers, "Qualifier does not apply to structs");
                        break;
                    case "class":
                        RejectQualifiers(aClass.Qualifiers, sClassQualifiers, "Qualifier does not apply to classes");
                        break;
                }
            }

            foreach (var func in unit.Funcs)
            {
                LastToken = func.Keyword;

                CheckQualifiers(func.ParentScope, func.Name, func.Qualifiers);

                CheckStatements(null, func.Statements);
                if (func.Statements == null)
                {
                    mParser.RejectToken(func.Keyword, "Missing body");
                }

                var keyword = func.Keyword;
                var outerKeyword = func.ParentScope == null ? "" : func.ParentScope.Keyword;
                if (outerKeyword == "")
                    mParser.RejectToken(keyword, "The namespace must be defined before method");
                if ((outerKeyword == "" || outerKeyword == "namespace") && func.ClassName == null)
                {
                    if (!HasQualifier(func.Qualifiers, sRequireGlobalFuncQualifiers))
                        mParser.RejectToken(keyword, "Methods at the namespace level must be 'static' or an extension method");
                }
                if (outerKeyword == "interface")
                {
                    RejectQualifiers(func.Qualifiers, sFuncInInterfaceQualifiersAllowed, "This qualifier may not appear before a function defined inside an interface");
                }

                switch (keyword)
                {
                    case "prop":
                        RejectQualifiers(func.Qualifiers, sPropQualifiers, "Qualifier does not apply to properties");
                        break;
                    case "fun":
                    case "func":
                    case "afun":
                    case "afunc":
                        RejectQualifiers(func.Qualifiers, sFuncQualifiers, "Qualifier does not apply to functions");
                        break;
                }
            }

            foreach (var field in unit.Fields)
            {
                LastToken = field.Name;

                CheckQualifiers(field.ParentScope, field.Name, field.Qualifiers);

                var outerKeyword = field.ParentScope == null ? "" : field.ParentScope.Keyword;

                switch (outerKeyword)
                {
                    case "":
                    case "namespace":
                        if (!HasQualifier(field.Qualifiers, sRequireGlobalFieldQualifiers))
                            mParser.RejectToken(field.Name, "Fields at the namespace level must be 'const' or 'static'");
                        break;
                    case "interface":
                        mParser.RejectToken(field.Name, "Fields are not allowed inside an interface");
                        break;
                    case "class":
                        RejectQualifiers(field.Qualifiers, sFieldInClassQualifiers, "Does not apply to a field in a class");
                        break;
                    case "struct":
                        RejectQualifiers(field.Qualifiers, sFieldInStructQualifiers, "Does not apply to a field in a struct");
                        break;
                    case "enum":
                        RejectQualifiers(field.Qualifiers, sFieldInEnumQualifiers, "Does not apply to a field in an enum");
                        break;
                }
            }
        }

        void CheckQualifiers(SyntaxScope parentClass, Token token, Token[] qualifiers)
        {
            if (token == null || token.Name == "" || qualifiers == null)
                return;

            bool isInInterface = parentClass != null && parentClass.Keyword.Name == "interface";
            int sortOrder = -1;
            for (int i = 0; i < qualifiers.Length; i++)
            {
                var qualifier = qualifiers[i];

                // Verify no duplicates
                for (int j = 0; j < i; j++)
                {
                    if (qualifiers[j].Name == qualifier.Name)
                        mParser.RejectToken(qualifier, "Cannot have duplicate qualifiers");
                }

                // Sort order and no combining
                if (sClassFuncFieldQualifiersOrder.TryGetValue(qualifier.Name, out var newSortOrder))
                {
                    if (newSortOrder == sortOrder)
                        mParser.RejectToken(qualifier, "The '" + qualifier + "' qualifier cannot be combined with '" + qualifiers[i-1] + "'");
                    if (newSortOrder < sortOrder)
                        mParser.RejectToken(qualifier, "The '" + qualifier + "' qualifier must come before '" + qualifiers[i-1] + "'");
                    sortOrder = newSortOrder;
                }

                if (qualifier.Name == "public")
                    mParser.RejectToken(qualifier, "Use 'pub' instead of public");
            }
        }

        bool HasQualifier(Token[] qualifiers, WordSet accept)
        {
            foreach (var q in qualifiers)
                if (accept.Contains(q.Name))
                    return true;
            return false;
        }

        // Reject the token if it's not qualified
        void RejectTokenIfNotQualified(Token token, IEnumerable<Token> qualifiers, string expected, string errorMessage)
        {
            foreach (var t in qualifiers)
                if (t.Name == expected)
                    return;
            mParser.RejectToken(token, errorMessage);
        }

        void RejectQualifiers(Token []qualifiers, string errorMessage)
        {
            RejectQualifiers(qualifiers, null, errorMessage);
        }

        // Reject tokens with errorMessage.  Reject all of them if acceptSet is null.
        void RejectQualifiers(Token []qualifiers, WordSet acceptSet, string errorMessage)
        {
            foreach (var token in qualifiers)
            {
                if (token == "readonly")
                {
                    mParser.RejectToken(token, "Use 'ro' instead");
                    continue;
                }
                if (acceptSet == null || !acceptSet.Contains(token))
                    mParser.RejectToken(token, errorMessage);
            }
        }

        void CheckStatements(SyntaxExpr parent, SyntaxExpr expr)
        {
            if (expr == null)
                return;

            // TBD: This will be generalized when generating code
            WordSet sInvalidLeftAssignments = new WordSet("+ - * / % & | == != < <= > >=");

            LastToken = expr.Token;
            switch (expr.Token)
            {
                case "__pcfail":
                    throw new Exception("Parse check fail test");

                case "{": // Statement
                    foreach (var e in expr)
                        CheckStatements(expr, e);
                    break;

                case "(": // Function call
                    foreach (var e in expr)
                        CheckExpr(expr, e);
                    break;
                
                case "=": case "+=": case "-=": case "*=": case "/=":
                case "%=": case "&=": case "|=": case "~=": case "<<=":
                case ">>=":
                    if (expr.Count >= 2)
                    {
                        if (sInvalidLeftAssignments.Contains(expr[0].Token))
                        {
                            if (expr[0].Token != "*" || expr[0].Count >= 2) // Unary `*` is OK
                                mParser.RejectToken(expr[0].Token, "Invalid operator in left side of assignment");
                        }
                        else
                        {
                            CheckExpr(expr, expr[0]);
                        }
                        CheckExpr(expr, expr[1]);
                    }
                    break;

                case "=>":
                    if (expr.Count != 0)
                        CheckExpr(expr, expr[0]);
                    break;

                case "->": // Lambda
                    // TBD: Check lambda
                    break;


                case "const":
                case "var":
                case "let":
                case "mut":
                case "@":
                    if (expr.Count > 2)
                        CheckExpr(expr, expr[2]);
                    break;

                case "defer":
                case "use":
                case "throw":
                    foreach (var e in expr)
                        CheckStatements(expr, e);
                    break;

                case "scope":
                    if (expr.Count != 0)
                        CheckStatements(expr, expr[0]);
                    break;

                case "switch":
                case "while":
                case "do":
                case "if":
                    if (expr.Count != 0)
                        CheckExpr(expr, expr[0]);
                    for (int i = 1; i < expr.Count; i++)
                        CheckStatements(expr, expr[i]);
                    break;

                case "for":
                    if (expr.Count >= 2)
                        CheckExpr(expr, expr[1]);
                    for (int i = 2;  i < expr.Count;  i++)
                        CheckStatements(expr, expr[i]);
                    break;

                case "case":
                    foreach (var e in expr)
                        CheckExpr(expr, e);
                    break;

                case "continue":
                case "break":
                case "default":
                case "catch":
                case "finally":
                    break;

                case "unsafe":
                case "return":
                    if (expr.Count != 0)
                        CheckExpr(expr, expr[0]);
                    break;

                default:
                    mParser.RejectToken(expr.Token, "Only assignment, function call, and create typed variable can be used as statements");
                    break;
            }
        }

        void CheckExpr(SyntaxExpr parent, SyntaxExpr expr)
        {
            switch (expr.Token)
            {
                case "switch":
                    // Switch expression (not statement)
                    if (parent != null && parent.Token != "{" && expr.Count >= 2)
                    {
                        if (expr[1].Count == 0)
                            mParser.RejectToken(expr[1].Token, "Switch expression list may not be empty");                       
                    }

                    if (expr.Count == 2)
                    {
                        CheckExpr(expr, expr[0]);
                        foreach (var e in expr[1])
                            CheckStatements(null, e);
                    }

                    //foreach (var e in expr)
                    //    CheckStatements(expr, e);
                    break;


                default:
                    foreach (var e in expr)
                        CheckExpr(expr, e);
                    break;
            }
        }

        public void ShowParseTree(SyntaxFile unit)
        {
            if (!mShowParseTree)
                return;

            foreach (var aClass in unit.Types)
            {
                if (aClass.Extends != null)
                    ShowParseTree(aClass.Extends);
                if (aClass.Implements != null)
                    foreach (var baseClass in aClass.Implements)
                        ShowParseTree(baseClass);
                ShowParseTree(aClass.Alias);
            }
            foreach (var func in unit.Funcs)
            {
                ShowParseTree(func.ReturnType);
                if (func.Params != null)
                    foreach (var param in func.Params)
                        if (param.Count >= 1)
                            ShowParseTree(param[0]);
                if (func.Statements != null)
                    foreach (var statement in func.Statements)
                        ShowParseTree(statement);
            }
            foreach (var field in unit.Fields)
            {
                ShowParseTree(field.TypeName);
            }
        }

        SyntaxExpr ShowParseTree(SyntaxExpr expr)
        {
            if (expr == null)
                return expr;
            expr.Token.AddInfo("Parse tree: " + expr.ToString());
            foreach (var e in expr)
                ShowParseTree(e); // Subtrees without info token
            return expr;
        }



    }
}
