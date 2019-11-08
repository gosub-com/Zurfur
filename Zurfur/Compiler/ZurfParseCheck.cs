using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Check the parse tree for various errors and warnings.
    /// NOTE: Many of these errors will be moved to analysis phase
    /// </summary>
    class ZurfParseCheck
    {
        // Debug the parse tree
        bool mShowParseTree = true;

        public Token LastToken;

        static WordSet sRequireGlobalFieldQualifiers = new WordSet("static const");
        static WordSet sFuncInInterfaceQualifiersAllowedEmpty = new WordSet("static pub protected");
        static WordSet sFuncInInterfaceQualifiersAllowedNotEmpty = new WordSet("static pub private protected");
        static WordSet sGlobalFuncsRequiringStatic = new WordSet("func afunc construct destruct");
        static WordSet sStaticQualifier = new WordSet("static");
        static WordSet sGlobalFuncsNotAllowed = new WordSet("prop this operator");

        static WordSet sInterfaceQualifiers = new WordSet("pub public protected private internal");
        static WordSet sClassQualifiers = new WordSet("pub public protected private internal unsafe sealed sealed1 abstract");
        static WordSet sStructQualifiers = new WordSet("pub public protected private internal unsafe ref ro");
        static WordSet sEnumQualifiers = new WordSet("pub public protected private internal");
        static WordSet sFieldInStructQualifiers = new WordSet("pub public protected private internal unsafe static volatile ro const");
        static WordSet sFieldInClassQualifiers = new WordSet("pub public protected private internal unsafe static volatile ro const");
        static WordSet sFieldInEnumQualifiers = new WordSet("");
        static WordSet sFuncQualifiers = new WordSet("pub public protected private internal unsafe static extern abstract virtual override new");
        static WordSet sFuncOperatorQualifiers = new WordSet("pub public protected private internal unsafe extern");

        static WordSet sStatements = new WordSet("if return while for switch case default throw "
                                                     + "= ( += -= *= /= %= &= |= ~= <<= >>=");

        static WordMap<int> sClassFuncFieldQualifiersOrder = new WordMap<int>()
        {
            { "pub", 1 }, { "public", 1 }, { "protected", 1 }, { "private", 1 }, { "internal", 1 },
            { "unsafe", 2 }, { "extern", 3 }, { "static", 4 },  {"const", 4 },
            { "sealed", 6 }, { "sealed1", 6 },
            { "abstract", 8 }, { "virtual", 8},  { "override", 8 }, { "new", 8 },
            { "volatile", 9 },
            { "ref", 10},
            { "mut", 11 }, { "ro", 11}, {"readonly", 11}
        };

        ZurfParse mParser;

        public ZurfParseCheck(ZurfParse parser)
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
            foreach (var aClass in unit.Classes)
            {
                LastToken = aClass.Keyword;
                CheckQualifiers(aClass.ParentClass, aClass.Name, aClass.Qualifiers);

                var keyword = aClass.Keyword;
                var outerKeyword = aClass.ParentClass == null ? "" : aClass.ParentClass.Keyword;
                if (aClass.Namespace == null && outerKeyword == "")
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

                CheckQualifiers(func.ParentClass, func.Name, func.Qualifiers);

                CheckFuncBody(null, func.Statements);

                var keyword = func.Keyword;
                var outerKeyword = func.ParentClass == null ? "" : func.ParentClass.Keyword;
                if (func.Namespace == null && outerKeyword == "")
                    mParser.RejectToken(keyword, "The namespace must be defined before method");
                if (func.Statements == null && outerKeyword != "interface" && keyword != "prop" && keyword != "get" && keyword != "set")
                    RejectTokenIfNotQualified(keyword, func.Qualifiers, "extern", "Function without a body must have the'extern' qualifier");
                if (outerKeyword == "interface")
                {
                    if (func.Statements == null)
                        RejectQualifiers(func.Qualifiers, sFuncInInterfaceQualifiersAllowedEmpty, "This qualifier may not appear before an empty function defined inside an interface");
                    else
                        RejectQualifiers(func.Qualifiers, sFuncInInterfaceQualifiersAllowedNotEmpty, "This qualifier may not appear before an empty function defined inside an interface");

                }
                if (outerKeyword == ""
                        && sGlobalFuncsRequiringStatic.Contains(keyword)
                        && !HasQualifier(func.Qualifiers, sStaticQualifier)
                        && func.ClassName == null)
                    mParser.RejectToken(keyword, "Functions at the namespace level must be static");
                if (outerKeyword == "" && sGlobalFuncsNotAllowed.Contains(keyword))
                    mParser.RejectToken(keyword, "Must not be defined at the namespace level");

                switch (keyword)
                {
                    case "operator":
                        RejectQualifiers(func.Qualifiers, sFuncOperatorQualifiers, "Operator may not use this qualifier");
                        break;
                    case "construct":
                    case "func":
                    case "afunc":
                    case "this":
                    case "prop":
                        RejectQualifiers(func.Qualifiers, sFuncQualifiers, "Qualifier does not apply to this method type");
                        break;
                }
            }

            foreach (var field in unit.Fields)
            {
                LastToken = field.Name;

                CheckQualifiers(field.ParentClass, field.Name, field.Qualifiers);

                var outerKeyword = field.ParentClass == null ? "" : field.ParentClass.Keyword;

                var quals = outerKeyword == "class" ? sFieldInClassQualifiers : sFieldInStructQualifiers;
                if (outerKeyword == "enum")
                    quals = sFieldInEnumQualifiers;
                switch (outerKeyword)
                {
                    case "":
                        if (!HasQualifier(field.Qualifiers, sRequireGlobalFieldQualifiers))
                            mParser.RejectToken(field.Name, "Fields at the namespace level must be static or const");
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

        void CheckQualifiers(SyntaxClass parentClass, Token token, Token[] qualifiers)
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

        void CheckFuncBody(SyntaxExpr parent, SyntaxExpr expr)
        {
            if (expr == null)
                return;

            LastToken = expr.Token;
            switch (expr.Token)
            {
                case "__pcfail":
                    throw new Exception("Parse check fail test");

                case "{": // Statement
                    foreach (var e in expr)
                    {
                        if (!sStatements.Contains(e.Token))
                        {
                            if (e.Token == "{")
                                mParser.RejectToken(e.Token, "Unnecessary scope is not allowed");
                            else
                                mParser.RejectToken(e.Token, "Only assignment and call can be used as statements");
                        }
                        CheckFuncBody(expr, e);
                    }
                    break;

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

                    foreach (var e in expr)
                        CheckFuncBody(expr, e);
                    break;

                case "()": // Lambda parameters
                    if (parent == null || parent.Token != "=>")
                    {
                        mParser.RejectToken(expr.Token, expr.Count == 0
                            ? "Empty expression not allowed unless followed by lambda '=>' token"
                            : "Multi-parameter expression not allowed unless followed by lambda '=>' token");
                        foreach (var e in expr)
                            Grayout(e);
                    }
                    break;

                case "=>": // Lambda
                    //if (expr[])
                    break;

                default:
                    foreach (var e in expr)
                        CheckFuncBody(expr, e);
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
            foreach (var aClass in unit.Classes)
                ShowTypes(aClass);
            foreach (var func in unit.Funcs)
                ShowTypes(func);
            foreach (var field in unit.Fields)
            {
                ShowTypes(field.TypeName, true);
                ShowTypes(field.InitExpr, false);
            }
        }

        void ShowTypes(SyntaxClass aClass)
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
                            || expr.Token == ZurfParse.PTR) )
                expr.Token.Type = eTokenType.TypeName;

            // Cast
            if (ZurfParse.sCastOperators.Contains(expr.Token))
            {
                if (expr.Count != 0)
                    ShowTypes(expr[0], true);
                for (int i = 1; i < expr.Count; i++)
                    ShowTypes(expr[i], isType);
                return;
            }

            if (expr.Token == ZurfParse.VIRTUAL_TOKEN_TYPE_ARG_LIST)
                isType = true;
            foreach (var e in expr)
                ShowTypes(e, isType);
        }

        public void ShowParseTree(SyntaxUnit unit)
        {
            if (!mShowParseTree)
                return;

            foreach (var aClass in unit.Classes)
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
