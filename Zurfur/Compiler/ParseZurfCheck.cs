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
        bool mShowParseTree = false;

        public Token LastToken;

        static WordSet sRequireGlobalFieldQualifiers = new WordSet("const");
        static WordSet sFuncInInterfaceQualifiersAllowedEmpty = new WordSet("func afunc operator prop static pub protected mut");
        static WordSet sFuncInInterfaceQualifiersAllowedNotEmpty = new WordSet("func afunc operator prop static pub private protected mut");
        static WordSet sGlobalFuncsRequiringStatic = new WordSet("func afunc");
        static WordSet sStaticQualifier = new WordSet("static");
        static WordSet sGlobalFuncsNotAllowed = new WordSet("prop this operator");

        static WordSet sInterfaceQualifiers = new WordSet("interface pub public protected private internal static");
        static WordSet sClassQualifiers = new WordSet("class pub pfublic protected private internal unsafe unsealed abstract mut boxed");
        static WordSet sStructQualifiers = new WordSet("struct pub public protected private internal unsafe ref mut");
        static WordSet sEnumQualifiers = new WordSet("enum pub public protected private internal");

        static WordSet sFieldInStructQualifiers = new WordSet("pub public protected private internal unsafe static mut const");
        static WordSet sFieldInClassQualifiers = new WordSet("pub public protected private internal unsafe static mut const");
        static WordSet sFieldInEnumQualifiers = new WordSet("");

        static WordSet sFuncQualifiers = new WordSet("func afunc pub public protected private internal unsafe static virtual override new mut");
        static WordSet sPropQualifiers = new WordSet("prop pub public protected private internal unsafe static virtual override new");
        static WordSet sFuncOperatorQualifiers = new WordSet("operator pub public protected private internal unsafe");

        static WordMap<int> sClassFuncFieldQualifiersOrder = new WordMap<int>()
        {
            { "pub", 1 }, { "public", 1 }, { "protected", 1 }, { "private", 1 }, { "internal", 1 },
            { "unsafe", 2 },
            { "static", 4 },  {"const", 4 },
            { "unsealed", 6 },
            { "abstract", 8 }, { "virtual", 8},  { "override", 8 }, { "new", 8 },
            { "class",9 }, { "struct",9 }, { "enum",9 }, { "interface",9 }, {"operator", 9}, {"func",9}, {"afunc",9},
            { "ref", 10},
            { "mut", 11 }, { "ro", 11}, {"readonly", 11},
            { "boxed", 12 }
        };

        ParseZurf mParser;

        public ParseZurfCheck(ParseZurf parser)
        {
            mParser = parser;
        }

        public void Check(SyntaxUnit unit)
        {
            LastToken = null;
            CheckParseTree(unit);
            LastToken = null;
            ShowTypes(unit); // TBD: This is temporary until type analysis is complete
            LastToken = null;
            ShowParseTree(unit);
        }

        void CheckParseTree(SyntaxUnit unit)
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

                var keyword = func.Keyword;
                var outerKeyword = func.ParentScope == null ? "" : func.ParentScope.Keyword;
                if (outerKeyword == "")
                    mParser.RejectToken(keyword, "The namespace must be defined before method");
                if (func.Statements == null)
                {
                    mParser.RejectToken(func.Keyword, "Missing body");
                }
                if (outerKeyword == "interface")
                {
                    if (func.Statements == null)
                        RejectQualifiers(func.Qualifiers, sFuncInInterfaceQualifiersAllowedEmpty, "This qualifier may not appear before an empty function defined inside an interface");
                    else
                        RejectQualifiers(func.Qualifiers, sFuncInInterfaceQualifiersAllowedNotEmpty, "This qualifier may not appear before a non-empty function defined inside an interface");

                }
                if ( (outerKeyword == "" || outerKeyword == "namespace")
                        && sGlobalFuncsRequiringStatic.Contains(keyword)
                        && !HasQualifier(func.Qualifiers, sStaticQualifier)
                        && func.ClassName == null)
                    mParser.RejectToken(keyword, "Functions at the namespace level must be static or extension methods");
                if ( (outerKeyword == "" || outerKeyword == "namespace")
                        && sGlobalFuncsNotAllowed.Contains(keyword))
                    mParser.RejectToken(keyword, "Must not be defined at the namespace level");

                switch (keyword)
                {
                    case "operator":
                        RejectQualifiers(func.Qualifiers, sFuncOperatorQualifiers, "Operator may not use this qualifier");
                        break;
                    case "prop":
                        RejectQualifiers(func.Qualifiers, sPropQualifiers, "Qualifier does not apply to properties");
                        break;
                    case "func":
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

                var quals = outerKeyword == "class" ? sFieldInClassQualifiers : sFieldInStructQualifiers;
                if (outerKeyword == "enum")
                    quals = sFieldInEnumQualifiers;
                switch (outerKeyword)
                {
                    case "":
                    case "namespace":
                        if (!HasQualifier(field.Qualifiers, sRequireGlobalFieldQualifiers))
                            mParser.RejectToken(field.Name, "Fields at the namespace level must be const");
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

            //            static WordSet sStatements = new WordSet("if return while for switch case default throw defer break continue do use "
            //                                             + "{ = ( += -= *= /= %= &= |= ~= <<= >>= @");

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
                    break;

                case "return":
                    if (expr.Count != 0)
                        CheckExpr(expr, expr[0]);
                    break;

                case "->": // Lambda
                    // TBD: Check lambda
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
                        // All cases should have "=>"
                        foreach (var e in expr[1])
                            if (e.Token != "=>")
                                mParser.RejectToken(e.Token, "Expecting switch expression to contain '=>'");
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

        void Grayout(SyntaxExpr expr)
        {
            expr.Token.Grayed = true;
            var connectors = expr.Token.GetInfo<Token[]>();
            if (connectors != null)
                foreach (var t in connectors)
                    t.Grayed = true;
            foreach (var e in expr)
                Grayout(e);
        }

        public void ShowTypes(SyntaxUnit unit)
        {
            foreach (var aClass in unit.Types)
                ShowTypes(aClass);
            foreach (var func in unit.Funcs)
                ShowTypes(func);
            foreach (var field in unit.Fields)
            {
                ShowTypes(field.TypeName, true);
                ShowTypes(field.InitExpr, false);
            }
        }

        void ShowTypes(SyntaxType aClass)
        {
            ShowTypes(aClass.Alias, true);
            if (aClass.BaseClasses != null)
                foreach (var baseClass in aClass.BaseClasses)
                    ShowTypes(baseClass, true);
            aClass.Name.Type = eTokenType.TypeName;
            ShowTypes(aClass.TypeParams, true);
            ShowTypes(aClass.Constraints);
        }

        void ShowTypes(SyntaxConstraint []constraints)
        {
            if (constraints == null)
                return;
            foreach (var constraint in constraints)
            {
                if (constraint.GenericTypeName != null)
                    constraint.GenericTypeName.Type = eTokenType.TypeName;
                if (constraint.TypeNames != null)
                    foreach (var typeName in constraint.TypeNames)
                        ShowTypes(typeName, true);
            }
        }

        void ShowTypes(SyntaxFunc func)
        {
            ShowTypes(func.ClassName, true);
            ShowTypes(func.TypeParams, true);
            ShowTypes(func.ReturnType, true);
            ShowTypes(func.Statements, false);
            ShowTypes(func.Constraints);
            if (func.Params != null)
                foreach (var param in func.Params)
                    if (param.Count >= 1)
                        ShowTypes(param[0], true);

        }

        void ShowTypes(SyntaxExpr expr, bool isType)
        {
            if (expr == null)
                return;
            if (isType && (expr.Token.Type == eTokenType.Identifier
                            || expr.Token == ParseZurf.PTR
                            || expr.Token == ParseZurf.REFERENCE
                            || expr.Token == "?") )
                expr.Token.Type = eTokenType.TypeName;

            // New variable
            if (expr.Token == ParseZurf.NEWVAR)
            {
                if (expr.Count >= 2)
                    ShowTypes(expr[1], true);
                for (int i = 2; i < expr.Count; i++)
                    ShowTypes(expr[i], isType);
                return;
            }

            // Cast
            if (expr.Token == "#" || expr.Token == "sizeof")
            {
                if (expr.Count >= 1)
                    ShowTypes(expr[0], true);
                if (expr.Count >= 2)
                    ShowTypes(expr[1], false);
            }

            if (expr.Token == ParseZurf.VIRTUAL_TOKEN_TYPE_ARG_LIST)
                isType = true;
            foreach (var e in expr)
                ShowTypes(e, isType);
        }

        public void ShowParseTree(SyntaxUnit unit)
        {
            if (!mShowParseTree)
                return;

            foreach (var aClass in unit.Types)
            {
                if (aClass.BaseClasses != null)
                    foreach (var baseClass in aClass.BaseClasses)
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
                ShowParseTree(field.InitExpr);
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
