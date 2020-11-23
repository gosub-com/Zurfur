using System;
using System.Collections.Generic;
using System.Linq;
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
        static WordSet sFuncInInterfaceQualifiersAllowed = new WordSet("pub private protected static mut async error");

        static WordSet sInterfaceQualifiers = new WordSet("pub public protected private internal static");
        static WordSet sClassQualifiers = new WordSet("pub pfublic protected private internal unsafe unsealed abstract ro boxed");
        static WordSet sStructQualifiers = new WordSet("pub public protected private internal unsafe ref ro");
        static WordSet sEnumQualifiers = new WordSet("pub public protected private internal");

        static WordSet sFieldInStructQualifiers = new WordSet("pub public protected private internal unsafe static ro const var mut @");
        static WordSet sFieldInClassQualifiers = new WordSet("pub public protected private internal unsafe static ro const var mut @");
        static WordSet sFieldInEnumQualifiers = new WordSet("");

        static WordSet sFuncQualifiers = new WordSet("pub public protected private internal unsafe virtual override new mut static async error");
        static WordSet sPropQualifiers = new WordSet("pub public protected private internal unsafe static virtual override new");

        static WordSet sTopLevelStatements = new WordSet("{ ( = += -= *= /= %= &= |= ~= <<= >>= => @ "
            + "const var let mut defer use throw switch case return for break default while if else get set do unsafe error finally exit fun afun");
        static WordSet sAssignments = new WordSet("= += -= *= /= %= &= |= ~= <<= >>=");
        static WordSet sInvalidLeftAssignments = new WordSet("+ - * / % & | == != < <= > >=");

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

        static WordMap<int> sOpClass = new WordMap<int>
        {
            { "+", 1 }, { "-", 1 }, { "*", 1 }, { "/", 1 }, { "%", 1 },
            { "|", 2 }, { "&", 2 },  { ">>", 2 }, { "<<", 2 },
            { "~", 3 },
        };

        const string DO_NOT_MIX_ARITMETIC_ERROR = "Do not mix arithmetic and bitwise operators, use parentheses.";
        const string DO_NOT_MIX_XOR_ERROR = "Do not mix XOR with arithmetic or bitwise operators, use parentheses.";


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
                if (aClass.NamePath.Length == 0)
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

            foreach (var func in unit.Methods)
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
                if (func.NamePath.Length == 0)
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

                if (func.Name == "new" && func.ParentScope is SyntaxType t && t.Simple)
                    mParser.RejectToken(func.Name, "'new' may not be defined inside class/struct with parameters");

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

                if (!field.Simple && field.ParentScope is SyntaxType t && t.Simple)
                    mParser.RejectToken(field.Name, "Fields may not be defined inside class/struct with parameters");

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
            LastToken = expr.Token;

            foreach (var e in expr)
                CheckStatements(expr, e);

            // Check invalid operator class
            if (expr.Count == 2 && sOpClass.TryGetValue(expr.Token.Name, out var opClass))
            {
                if (expr[0].Count == 2
                    && sOpClass.TryGetValue(expr[0].Token.Name, out var opClass0)
                    && opClass0 != opClass)
                {
                    var message = expr.Token == "~" || expr[0].Token == "~" ? DO_NOT_MIX_XOR_ERROR : DO_NOT_MIX_ARITMETIC_ERROR;
                    mParser.RejectToken(expr.Token, message);
                    mParser.RejectToken(expr[0].Token, message);
                }
                if (expr[1].Count == 2
                    && sOpClass.TryGetValue(expr[1].Token.Name, out var opClass1)
                    && opClass1 != opClass)
                {
                    var message = expr.Token == "~" || expr[0].Token == "~" ? DO_NOT_MIX_XOR_ERROR : DO_NOT_MIX_ARITMETIC_ERROR;
                    mParser.RejectToken(expr.Token, message);
                    mParser.RejectToken(expr[1].Token, message);
                }
            }


            // The rest only gets checked for statements
            bool isStatement = expr.Token == "{";
            if (!isStatement)
                return;

            // Check valid top level statements
            if (!sTopLevelStatements.Contains(expr.Token.Name))
                mParser.RejectToken(expr.Token, "Only assignment, function call, and create typed variable can be used as statements");

            // Check no assignment to rvalue 'a+b = 1'
            // NOTE: This probably goes away when we check types (cannot assign to r value)
            if (sAssignments.Contains(expr.Token.Name) && expr.Count >= 2)
                if (sInvalidLeftAssignments.Contains(expr[0].Token))
                {
                    if (expr[0].Token != "*" || expr[0].Count >= 2) // Unary `*` is OK
                        mParser.RejectToken(expr[0].Token, "Invalid operator in left side of assignment");
                }

            // TBD: Check for 'return' before 'error' and 'continue', etc. after (see README) 
            // Check for 'return' before or code after local function
            bool hasLocalFunction = false;
            for (int i = 0;  i < expr.Count;  i ++)
            {
                var token = expr[i].Token;
                if (token.Name == "fun" || token.Name == "afun")
                {
                    // Local function defined
                    hasLocalFunction = true;
                    var parentName = parent == null ? "" : parent.Token.Name;
                    if (parent != null)
                        mParser.RejectToken(token, "Local function must be defined at top level of a function scope");
                    else if (i == 0 || expr[i - 1].Token.Name != "return")
                        mParser.RejectToken(token, "Expecting 'return' before a new local function is defined");
                }
                else
                {
                    if (hasLocalFunction)
                        mParser.RejectToken(token, "No code allowed after a local function has been defined");
                }
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
            foreach (var func in unit.Methods)
            {
                ShowParseTree(func.Params);
                if (func.Statements != null)
                    foreach (var statement in func.Statements)
                        ShowParseTree(statement);
            }
            foreach (var field in unit.Fields)
            {
                if (field.TypeName != null)
                    ShowParseTree(field.TypeName);
                if (field.Initializer != null)
                    ShowParseTree(field.Initializer);
            }
        }

        SyntaxExpr ShowParseTree(SyntaxExpr expr)
        {
            if (expr == null)
                return expr;
            expr.Token.AddInfo("Parse tree: " + expr.ToString());
            mParser.RecordInvisibleToken(expr.Token);
            foreach (var e in expr)
                ShowParseTree(e); // Subtrees without info token
            return expr;
        }



    }
}
