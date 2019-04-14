using System;
using System.Drawing;
using System.Collections.Generic;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Base class for the parser
    /// </summary>
    class ParseZurf
    {
        // Since '<' is ambiguous for type argument lists, this symbol
        // is used instead.  '<' is only for less than symbol.
        public const string TYPE_ARG_LIST_TOKEN = "@type_arg_list@";

        const bool SHOW_PARSE_TREE = true;

        bool				mParseError;	// Flag is set whenever a parse error occurs
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken;
        Token               mPrevToken;
        List<string>        mComments = new List<string>();


        // TBD: >> >= will be omitted and handled at parser level 
        public const string TokenSymbols = "<< >> <= >= == != && || += -= *= /= %= &= |= ^= => ===";

        static readonly string sReservedWordsList = "abstract as base break case catch class const "
            + "continue default delegate do else enum event explicit extern false "
            + "finally fixed for goto if implicit in interface internal is lock namespace "
            + "new null operator out override params private protected pub ro ref "
            + "return sealed sealed1 sizeof stackalloc static struct switch this throw true try "
            + "typeof unsafe using static virtual volatile while "
            + "async await astart get set yield global partial var where nameof func construct cast";
        static readonly string sReservedControlWords = "using namespace class struct interface func construct if else switch case";
        static WordMap<eTokenType> sReservedWords = new WordMap<eTokenType>();

        static WordSet sClassAndFuncQualifiers = new WordSet("pub public protected private internal ref "
            + "static extern virtual override ro readonly volatile new async abstract unsafe sealed sealed1");
        static WordSet sTypeParameterQualifiers = new WordSet("in out ref ro");
        static WordSet sFuncParameterQualifiers = new WordSet("out ref");

        static WordSet sOverloadableOperators = new WordSet("+ - * / % ~ & | ^ >> <<");
        static WordSet sComparisonOperators = new WordSet("== === != > >= < <=");
        static WordSet sAddOperators = new WordSet("+ - | ^");
        static WordSet sMultiplyOperators = new WordSet("* / % & << >>");
        static WordSet sAssignOperators = new WordSet("= += -= *= /= %= |= &= ^= <<= >>=");
        static WordSet sUnaryOperators = new WordSet("+ - ! ~ & * new #");

        // C# uses these symbols to resolve type argument ambiguities.  TBD: Good enough for us?
        // TBD: Add '[' so we can have 'new Array<byte>[0]'?
        static WordSet sTypeArgumentParameterSymbols = new WordSet("(  ) [ ]  }  :  ;  ,  .  ?  ==  !=  |  ^");

        static WordSet sRejectUnitTokens = new WordSet("using namespace interface struct class enum func { } ;", true);
        static WordSet sRejectLine = new WordSet("{ } ;", true);
        static WordSet sRejectFuncName = new WordSet("{ } ( [ ;", true);
        static WordSet sRejectFuncParamsParen = new WordSet("{ } ) ;", true);
        static WordSet sRejectFuncParamsBracket = new WordSet("{ } ) ;", true);
        static WordSet sRejectFuncParam = new WordSet("{ } , ) ;", true);
        static WordSet sRejectParen = new WordSet("; ) { }", true);
        static WordSet sRejectBracket = new WordSet("; ] { }", true);
        static WordSet sRejectAssign = new WordSet("{ } ; = += -= *= /= %= |= &= ^= <<= >>=", true);

        static ParseZurf()
        {
            sReservedWords.AddWords(sReservedWordsList, eTokenType.Reserved);
            sReservedWords.AddWords(sReservedControlWords, eTokenType.ReservedControl);
        }

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public ParseZurf(Lexer tokens)
        {
            mLexer = tokens;
            mLexerEnum = new Lexer.Enumerator(mLexer);
            Accept();
        }

        public SyntaxUnit Parse()
        {
            return ParseCompilationUnit();
        }

        public SyntaxUnit ParseCompilationUnit()
        {
            var unit = new SyntaxUnit();
            
            while (mTokenName != "")
            {
                Token[] qualifiers = ParseQualifiers(sClassAndFuncQualifiers);

                var token = mToken;
                var allowQualifiers = true;
                var comments = mComments.ToArray();
                switch (mTokenName)
                {
                    case ";": SkipSemicolon();  break;

                    case "using":
                        allowQualifiers = false;
                        unit.Using.Add(ParseUsingStatement());
                        if (unit.Namespaces.Count != 0)
                            RejectToken(token, "Using statements must come before the namespace");
                        break;

                    case "namespace":
                        allowQualifiers = false;
                        unit.Namespaces.Add(ParseNamespaceStatement(unit, comments));
                        break;

                    case "interface":
                    case "struct":
                    case "class":
                    case "enum":
                        var synClass = ParseClass(qualifiers, comments);
                        if (unit.CurrentNamespace != null)
                            unit.CurrentNamespace.Classes.Add(synClass);
                        else
                            RejectToken(token, "The namespace must be defined before '" + token.Name + "'");
                        break;

                    case "func":
                        var synFunc = ParseFuncDef(qualifiers, comments, false);
                        RejectTokenIfNotQualified(synFunc.Keyword, qualifiers, "static", "Functions in a namespace must be static");
                        if (unit.CurrentNamespace != null)
                            unit.CurrentNamespace.Funcs.Add(synFunc);
                        else
                           RejectToken(token, "The namespace must be defined before a " + token.Name);
                        break;

                    default:
                        Reject("Expecting keyword: 'using', 'namespace', 'class', 'struct', 'interface', or 'func'", sRejectUnitTokens);
                        if (mTokenName == "{" || mTokenName == "}")
                            Accept();
                        break;
                }
                // Reject qualifiers when not in front if class or struct
                if (qualifiers.Length != 0 && !allowQualifiers)
                    RejectTokens(qualifiers, "Qualifier may not come before '" + token.Name + "' statement");
            }
            return unit;
        }

        SyntaxUsing ParseUsingStatement()
        {
            var synUsing = new SyntaxUsing();
            synUsing.Keyword = Accept();
            synUsing.QualifiedIdentifiers = ParseQualifiedIdentifier("Expecting a namespace identifier", sRejectUnitTokens);
            SkipSemicolon();
            return synUsing;
        }

        SyntaxNamespace ParseNamespaceStatement(SyntaxUnit unit, string []comments)
        {
            var nspace = new SyntaxNamespace();
            nspace.Comments = comments;
            nspace.Keyword = Accept();
            nspace.QualifiedIdentifiers = ParseQualifiedIdentifier("Expecting a namespace identifier", sRejectUnitTokens);
            SkipSemicolon();
            return nspace;
        }

        // Parse class, struct, interface, or enum
        SyntaxClass ParseClass(Token []qualifiers, string []comments)
        {
            var synClass = new SyntaxClass();
            synClass.Comments = comments;
            synClass.Qualifiers = qualifiers;
            synClass.Keyword = Accept();
            synClass.ClassName = TryParseTypeName(true, sRejectLine);
            ShowParseTree(synClass.ClassName);

            if (AcceptMatch(":"))
            {
                synClass.BaseClass = TryParseTypeName(true, sRejectLine);
                ShowParseTree(synClass.BaseClass);
            }

            List<SyntaxConstraint> constraints = new List<SyntaxConstraint>();
            while (mTokenName == "where")
            {
                constraints.Add(ParseConstraint());
            }

            if (mTokenName == ";")
            {
                RejectToken(mToken, "Empty body not allowed");
                SkipSemicolon();
                return synClass;
            }
            if (AcceptMatch("="))
            {
                synClass.Alias = TryParseTypeName(true, sRejectLine);
                ShowParseTree(synClass.Alias);
                SkipSemicolon();
                return synClass;
            }
            if (mTokenName != "{")
                Reject("Expecting start of " + synClass.Keyword + " body, '{' or ';'", sRejectLine);

            if (mTokenName == "{")
            {
                ParseClassBody(synClass);
            }
            return synClass;
        }

        SyntaxConstraint ParseConstraint()
        {
            var constraint = new SyntaxConstraint();
            constraint.Keyword = Accept();
            if (!ParseIdentifier("Expecting a type name", sRejectLine, out constraint.Typename))
                return constraint;
            if (!AcceptMatch(":"))
            {
                mToken.Reject("Expecting ':'");
                return constraint;
            }
            List<SyntaxExpr> identifiers = new List<SyntaxExpr>();
            identifiers.Add(ParseQualifiedIdentifier("Expecting qualified identifier", sRejectLine, true));
            while (AcceptMatch(","))
                identifiers.Add(ParseQualifiedIdentifier("Expecting a type name", sRejectLine, true));
            constraint.QualifiedIdentifiers = identifiers.ToArray();
            return constraint;                       
        }

        // Parse class, struct, interface, or enum body
        private void ParseClassBody(SyntaxClass synClass)
        {
            // Read open token, '{'
            var openToken = Accept();
            if (openToken != "{")
                throw new Exception("Compiler error: Expecting '{' while parsing class body");

            var classKeyword = synClass.Keyword.Name;
            while (mTokenName != "" && mTokenName != "}")
            {
                var comments = mComments.ToArray();
                var qualifiers = ParseQualifiers(sClassAndFuncQualifiers);
                switch (mTokenName)
                {
                    case ";": SkipSemicolon(); break;

                    case "interface":
                    case "struct":
                    case "class":
                    case "enum":
                        if (classKeyword == "interface" || classKeyword == "enum")
                            RejectToken(mToken, "Classes, structs, enums, and interfaces may not be nested inside an interface or enum");
                        synClass.Classes.Add(ParseClass(qualifiers, comments));
                        break;

                    case "func":
                        synClass.Funcs.Add(ParseFuncDef(qualifiers, comments, classKeyword == "interface"));
                        break;

                    default:
                        if (ParseIdentifier("Expecting a variable name", sRejectLine, out var fieldName))
                        {
                            if (mTokenName == "=" && classKeyword == "interface")
                                RejectToken(mToken, "Fields are not allowed inside an interface");
                            synClass.Fields.Add(ParseField(qualifiers, comments, fieldName));
                            SkipSemicolon();
                        }
                        break;
                }
            }
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);
            else
                RejectToken(mToken, "Expecting '}'");
        }

        SyntaxField ParseField(Token []qualifiers, string []comments, Token fieldName)
        {
            var field = new SyntaxField();
            field.Qualifiers = qualifiers;
            field.Comments = comments;
            field.Name = fieldName;

            if (!AcceptMatch("="))
            {
                Reject("Expecting '='", sRejectLine);
                return field;
            }
            var eqToken = mPrevToken;
            field.Expr = ParseExpr();
            ShowParseTree(field.Expr);
            return field;
        }

        SyntaxExpr ShowParseTree(SyntaxExpr expr)
        {
            if (!SHOW_PARSE_TREE || expr == null)
                return expr;
            expr.Token.AddInfo("Parse tree: " + expr.ToString());
            foreach (var e in expr)
                ShowParseTree(e);
            return expr;
        }

        SyntaxFunc ParseFuncDef(Token []qualifiers, string []comments, bool isInterface)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc();
            synFunc.Comments = comments;
            synFunc.Qualifiers = qualifiers;
            synFunc.Keyword = Accept();

            // Parse function name
            if (AcceptMatch("operator"))
            {
                // Function name is an operator
                synFunc.FuncName = new SyntaxExprToken(mToken);
                if (sOverloadableOperators.Contains(mTokenName))
                    Accept();
                else
                    Reject("Expecting an overloadable operator", sRejectFuncName);
            }
            else if (AcceptMatch("new"))
            {
                // Function name is the constructor
                mPrevToken.Type = eTokenType.ReservedControl;
                synFunc.FuncName = new SyntaxExprToken(mToken);
            }
            else
            {
                if (AcceptMatch("get") || AcceptMatch("set"))
                {
                    mPrevToken.Type = eTokenType.ReservedControl;
                    synFunc.GetOrSetToken = mPrevToken;
                }
                if (mToken == "[")
                {
                    synFunc.FuncName = new SyntaxExprToken(mToken);
                }
                else
                {
                    // Usually just an indentifier, but could also be ClassType.Name
                    synFunc.FuncName = TryParseTypeName(false, sRejectFuncName);                
                }
            }
            ShowParseTree(synFunc.FuncName);

            // Parse parameters
            if (mToken != "(" && mToken != "[")
                Reject("Expecting '(' or '['", sRejectFuncName);
            if (mToken == "(" || mToken == "[")
                synFunc.Params = ParseFuncParams();

            if (mTokenName != "{" && mTokenName != ";")
            {
                synFunc.Return = TryParseTypeName(true, sRejectFuncParam);
                ShowParseTree(synFunc.Return);
            }

            if (mToken == ";")
            {
                if (!isInterface)
                    RejectTokenIfNotQualified(mToken, qualifiers, "extern", "This function must have 'extern' qualifier");
                SkipSemicolon();
                return synFunc;
            }
            if (mToken != "{")
                Reject("Expecting start of function body, '{' or ';'", sRejectLine);

            if (mToken == "{")
            {
                if (isInterface)
                    RejectToken(mToken, "Interface may not contain function body");
                synFunc.Statements = ParseStatements();
            }
            return synFunc;
        }

        SyntaxFuncParam []ParseFuncParams()
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '[' or '(' while parsing function parameters");

            // Parse parameters
            var closeToken = openToken.Name == "(" ? ")" : "]";
            var parameters = new List<SyntaxFuncParam>();
            if (mTokenName != closeToken)
                parameters.Add(ParseFuncParam());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseFuncParam());
            }

            if (mTokenName != closeToken)
                Reject("Expecting '" + closeToken + "'", closeToken == ")" ? sRejectFuncParamsParen : sRejectFuncParamsBracket);
            if (AcceptMatch(closeToken))
                Connect(openToken, mPrevToken);
            return parameters.ToArray();
        }

        SyntaxFuncParam ParseFuncParam()
        {
            var synParam = new SyntaxFuncParam();
            if (!ParseIdentifier("Expecting a variable name", sRejectFuncParam, out synParam.Name))
                return synParam;
            synParam.TypeName = TryParseTypeName(true, sRejectFuncParam);
            ShowParseTree(synParam.TypeName);
            return synParam;
        }

        private SyntaxExpr ParseStatements()
        {
            // Read open token, '{'
            var openToken = Accept();
            if (openToken != "{")
                throw new Exception("Compiler error: Expecting '{' while parsing function body");

            if (AcceptMatch("}"))
            {
                Connect(openToken, mPrevToken);
                return new SyntaxExprToken(openToken);
            }

            var statements = new List<SyntaxExpr>();
            while (mTokenName != "" && mTokenName != "}")
            {
                var keyword = mToken;
                switch (mToken)
                {
                    case ";": SkipSemicolon();  break;

                    case "while":
                    case "if":
                        Accept();
                        var conditionExpr = ParseExpr();
                        ShowParseTree(conditionExpr);
                        if (mTokenName != "{")
                            Reject("Expecting " + keyword + " body, '{'", sRejectLine);
                        if (mTokenName == "{")
                        {
                            var bodyExpr = ParseStatements();
                            if (mToken == "else" && keyword == "if")
                            {
                                // IF (condition) (if-body) (else-body)
                                statements.Add(new SyntaxExprMulti(keyword,
                                    new SyntaxExpr[] { conditionExpr, bodyExpr, ParseStatements() }));
                            }
                            else
                            {
                                // IF/WHILE (condition) (body)
                                statements.Add(new SyntaxExprBinary(keyword, conditionExpr, bodyExpr));
                            }
                        }
                        break;

                    case "else":
                        Reject("Else must follow 'if' statement", sRejectLine);
                        break;

                    case "for":
                        Reject("Not parsed yet", sRejectLine);
                        SkipSemicolon();
                        break;

                    case "return":
                        Accept();
                        if (mTokenName == ";" || mTokenName == "}")
                            statements.Add(new SyntaxExprToken(keyword));
                        else
                            statements.Add(new SyntaxExprUnary(keyword, ShowParseTree(ParseExpr())));
                        SkipSemicolon();
                        break;

                    case "throw":
                        Accept();
                        statements.Add(new SyntaxExprUnary(keyword, ShowParseTree(ParseExpr())));
                        break;

                    default:
                        var result = ParseExpr();
                        if (sAssignOperators.Contains(mToken))
                            result = new SyntaxExprBinary(Accept(), result, ParseExpr());
                        ShowParseTree(result);
                        statements.Add(result);
                        SkipSemicolon();
                        break;
                }
            }
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);
            else
                RejectToken(mToken, "Expecting '}'");

            return new SyntaxExprMulti(openToken, statements.ToArray());

        }

        ///  Parse expression (doesn't include ',' or '=' statements)
        public SyntaxExpr ParseExpr()
        {
            return ParseLambda();
        }

        SyntaxExpr ParseLambda()
        {
            var result = ParseTernary();
            if (mTokenName == "=>")
            {
                var lambdaToken = mToken;
                result = new SyntaxExprBinary(Accept(), result, ParseTernary());
                if (mTokenName == "=>")
                    Reject("Lambda operator is not associative, must use parentheses", sRejectLine);
                mToken.Reject("Lambda not yet supported, use 'func' keyword instead");
            }
            return result;
        }

        SyntaxExpr ParseTernary()
        {
            var result = ParseConditionalOr();
            if (mTokenName == "?")
            {
                Token operatorToken = Accept();
                var firstConditional = ParseConditionalOr();
                if (mTokenName != ":")
                {
                    Connect(mToken, operatorToken);
                    Reject("Expecting a ':' to separate expression for the ternary '?' operator", sRejectLine);
                    return result;
                }
                Accept();
                result = new SyntaxExprMulti(operatorToken, new SyntaxExpr[] { result, firstConditional, ParseConditionalOr() });
            }
            return result;
        }

        SyntaxExpr ParseConditionalOr()
        {
            var result = ParseConditionalAnd();
            while (mTokenName == "||")
                result = new SyntaxExprBinary(Accept(), result, ParseConditionalAnd());
            return result;
        }

        SyntaxExpr ParseConditionalAnd()
        {
            var result = ParseComparison();
            while (mTokenName == "&&")
                result = new SyntaxExprBinary(Accept(), result, ParseComparison());
            return result;
        }

        SyntaxExpr ParseComparison()
        {
            var result = ParseRange();
            if (sComparisonOperators.Contains(mTokenName))
            {
                result = new SyntaxExprBinary(Accept(), result, ParseRange());
                if (sComparisonOperators.Contains(mTokenName))
                    Reject("Compare operators are not associative, must use parentheses", sRejectLine);
            }
            return result;
        }

        SyntaxExpr ParseRange()
        {
            var result = ParseAdd();
            if (mTokenName == "..")
            {
                result = new SyntaxExprBinary(Accept(), result, ParseAdd());
                if (mTokenName == "..")
                    Reject("Range operator is not associative, must use parentheses", sRejectLine);
            }
            return result;
        }

        SyntaxExpr ParseAdd()
        {
            var result = ParseMultiply();
            while (sAddOperators.Contains(mTokenName))
                result = new SyntaxExprBinary(Accept(), result, ParseMultiply());
            return result;
        }

        SyntaxExpr ParseMultiply()
        {
            var result = ParseExpon();
            while (sMultiplyOperators.Contains(mTokenName))
                result = new SyntaxExprBinary(Accept(), result, ParseExpon());
            return result;
        }

        SyntaxExpr ParseExpon()
        {
            var result = ParseUnary();
            // TBD: Do we want exponentiation?
            //while (mTokenName == "**")
            //    result = new SyntaxExprBinary(Accept(), result, ParseUnary());
            return result;
        }

        SyntaxExpr ParseUnary()
        {
            // TBD: "new" operator may be parsed differently, see C# spec primary_no_array_creation_expression
            if (sUnaryOperators.Contains(mTokenName))
                return new SyntaxExprUnary(Accept(), ParseUnary());
            return ParsePrimary();
        }

        SyntaxExpr ParsePrimary()
        {
            var result = ParseAtom();
            bool accepted;
            do
            {
                accepted = false;
                if (mTokenName == "(" || mTokenName == "[")
                {
                    // Function call or array access
                    accepted = true;
                    var openToken = mToken;
                    var parameters = new List<SyntaxExpr>() { result };
                    ParseParameters(parameters);
                    result = new SyntaxExprMulti(openToken, parameters.ToArray());
                }
                else if (AcceptMatch("."))
                {
                    // Member access
                    accepted = true;
                    result = new SyntaxExprBinary(mPrevToken, result,
                        new SyntaxExprToken(ParseIdentifier("Expecting identifier", sRejectLine)));
                }
                else if (mTokenName == "<" && mPrevToken.Type == eTokenType.Identifier)
                {
                    // Possibly a type argument list.  Let's try it and find out.
                    var p = SaveParsePoint();
                    var typeArguments = TryParseTypeArgumentList(false, null);
                    if (typeArguments != null && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Type argument list
                        accepted = true;
                        typeArguments.Insert(0, result);
                        result = new SyntaxExprMulti(new Token(TYPE_ARG_LIST_TOKEN, 0, 0), typeArguments.ToArray());
                    }
                    else
                    {
                        // Failed, restore the enumerator back to before trying type argument list
                        RestoreParsePoint(p);
                    }
                }
            } while (accepted);
            return result;
        }

        /// <summary>
        /// Parse an atom - identifier, number, string literal, or parentheses
        /// </summary>
        SyntaxExpr ParseAtom()
        {
            if (mTokenName == "")
            {
                Reject("Unexpected end of file", sRejectLine);
                return new SyntaxExprToken(mToken);
            }

            // Parse parentheses (not function call)
            if (mTokenName == "(")
            {
                var result = ParseParen();

                // Parse a cast when the closing ')' is followed by '(' or an identifier.
                if (mToken.Type == eTokenType.Identifier || mToken == "(") 
                {
                    // Use ')' to differentiate from function call which is '('
                    result = new SyntaxExprBinary(mPrevToken, result, ParsePrimary());
                }
                return result;
            }

            // Parse number or string
            if (char.IsDigit(mTokenName, 0) || mTokenName[0] == '"')
                return new SyntaxExprToken(Accept());

            // Parse variable name
            if (char.IsLetter(mTokenName, 0))
            {
                return new SyntaxExprToken(ParseIdentifier("Expecting an identifier", sRejectLine));
            }
            var errorToken = mToken;
            Reject("Expecting an identifier, number, string literal, or parentheses", sRejectLine);
            return new SyntaxExprToken(errorToken);
        }

        /// <summary>
        /// Read the open '(' or '[' and parse the expression
        /// Returns the expression that was parsed.
        /// </summary>
        SyntaxExpr ParseParen()
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '[' or '(' while parsing paren");

            // Read open '(' or '[' and create an Expr
            var expectedToken = openToken == "(" ? ")" : "]";
            var rejectTokens = openToken == "(" ? sRejectParen : sRejectBracket;

            // Reject empty () or []
            if (mTokenName == expectedToken)
            {
                // Return an empty () or []
                Connect(openToken, mToken);
                Reject("Expecting an expression", rejectTokens);
                var emptyExpr = new SyntaxExprToken(Accept());
                return emptyExpr;
            }

            // Parse the expression
            SyntaxExpr result = ParseExpr();
            if (mTokenName != expectedToken)
            {
                // The rest of the line is rejected
                Reject("Expecting '" + expectedToken + "'", rejectTokens);
            }
            if (mTokenName == expectedToken)
            {
                Connect(openToken, mToken);
                Accept();
            }
            return result;
        }

        /// <summary>
        /// Read the open '(' or '[' and then parse the parameters into parameters
        /// </summary>
        void ParseParameters(List<SyntaxExpr> parameters)
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '(' or '[' while parsing parameters");

            // Empty () function call?
            var expectedToken = openToken == "(" ? ")" : "]";
            if (mTokenName == expectedToken)
            {
                // Return an empty () or []
                Connect(openToken, Accept());
                return;
            }

            // Parse parameters
            bool allowParamQualifiers = openToken == "(";
            parameters.Add(ParseParameter(allowParamQualifiers));
            while (AcceptMatch(","))
                parameters.Add(ParseParameter(allowParamQualifiers));

            // If not ended properly, reject this expression
            if (mTokenName != expectedToken)
                Reject("Expecting '" + expectedToken + "' or ','",
                    openToken == "(" ? sRejectParen : sRejectBracket);

            if (AcceptMatch(expectedToken))
                Connect(openToken, mPrevToken);
        }

        SyntaxExpr ParseParameter(bool allowParamQualifier)
        {
            if (allowParamQualifier && sFuncParameterQualifiers.Contains(mTokenName))
                return new SyntaxExprUnary(Accept(), ParseExpr());
            return ParseExpr();
        }

        /// <summary>
        /// Returns null if there is an error.
        /// Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        SyntaxExpr TryParseTypeName(bool allowTypeQualifiers, WordSet errorStop)
        {
            // Unary operators '*' and '[]', short for Pointer<type> and Array<type>
            // Treat qualifiers `in`, `out`, `ref`, `ro` similar to unary operators
            if (mToken == "*" || mToken == "["
                || allowTypeQualifiers && sTypeParameterQualifiers.Contains(mToken))
            {
                var token = Accept();
                if (token.Name == "[" && !AcceptMatch("]"))
                {
                    if (errorStop != null)
                        Reject("Expecting ']'", errorStop);
                    return null;
                }
                var expr = TryParseTypeName(allowTypeQualifiers, errorStop);
                if (expr == null)
                    return null;
                return new SyntaxExprUnary(token, expr);
            }

            if (!ParseIdentifier("Expecting a type or namespace", errorStop, out var typeName))
                return null;

            var result = (SyntaxExpr)new SyntaxExprToken(typeName);

            bool accepted;
            do
            {
                accepted = false;
                if (AcceptMatch("."))
                {
                    accepted = true;
                    var dotToken = mPrevToken;
                    if (!ParseIdentifier("Expecting a type or namespace", errorStop, out var dotTypeName))
                        return null;
                    result = new SyntaxExprBinary(dotToken, result, new SyntaxExprToken(dotTypeName));
                }

                if (mToken == "<")
                {
                    accepted = true;
                    var openToken = mToken;
                    var expr = TryParseTypeArgumentList(allowTypeQualifiers, errorStop);
                    if (expr == null)
                        return null;
                    expr.Insert(0, result);
                    result = new SyntaxExprMulti(openToken, expr.ToArray());
                }
            } while (accepted);
            return result;
        }

        /// <summary>
        /// Try parsing a type argument list.  Returns NULL on failure.
        /// Error causes reject until errorStop unless errorStop is null.
        List<SyntaxExpr> TryParseTypeArgumentList(bool allowTypeQualifiers, WordSet errorStop)
        {
            var openToken = Accept();
            if (openToken.Name != "<")
                throw new Exception("Compiler error: Expecting '<' while parsing type argument list");

            // Parse the first parameter
            var arguments = new List<SyntaxExpr>();
            var p = TryParseTypeName(allowTypeQualifiers, errorStop);
            if (p == null)
                return null;
            arguments.Add(p);

            // Parse the rest of the parameters
            while (AcceptMatch(","))
            {
                p = TryParseTypeName(allowTypeQualifiers, errorStop);
                if (p == null)
                    return null;
                arguments.Add(p);
            }

            if (!AcceptMatch(">"))
            {
                if (errorStop != null)
                    Reject("Expecting '>' to end the type argument list", errorStop);
                return null;
            }
            Connect(openToken, mPrevToken);
            return arguments;
        }
        
        void SkipSemicolon()
        {
            if (AcceptMatch(";"))
                return;
            if (mToken == "}")
                return;
            RejectToken(mPrevToken, "Expecting ';' after this token");
        }

        Token []ParseQualifiers(WordMap<bool> qualifiers)
        {
            List<Token> acceptedQualifiers = null;
            while (qualifiers.Contains(mTokenName))
            {
                if (acceptedQualifiers == null)
                    acceptedQualifiers = new List<Token>();
                acceptedQualifiers.Add(mToken);
                if (mTokenName == "public")
                    RejectToken(mToken, "Use 'pub' instead");
                if (mTokenName == "readonly")
                    RejectToken(mToken, "Use 'ro' instead");
                Accept();
            }
            return acceptedQualifiers == null ? Token.EmptyArray : acceptedQualifiers.ToArray();
        }

        /// <summary>
        /// Parse a qualified identifier.  
        /// Error causes reject until errorStop and returns null.
        /// </summary>
        SyntaxExpr ParseQualifiedIdentifier(string errorMessage, WordSet errorStop, bool allow1Reserved = false)
        {
            // Parse first identifier
            if (!ParseIdentifier(errorMessage, errorStop, out var t1, allow1Reserved))
                return null;
            if (t1.Type == eTokenType.ReservedControl)
                t1.Type = eTokenType.Reserved; // Downgrade symbol
            var identifier = new SyntaxExprToken(t1);
            if (mTokenName != ".")
                return identifier;

            // Parse the rest
            var dotToken = mToken;
            var tokens = new List<SyntaxExpr>();
            tokens.Add(identifier);
            while (AcceptMatch("."))
            {
                if (!ParseIdentifier(errorMessage, errorStop, out var t2))
                    return null;
                tokens.Add(new SyntaxExprToken(t2));
            }
            return new SyntaxExprMulti(dotToken, tokens.ToArray());
        }

        /// <summary>
        /// Parse an identifier.  Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        Token ParseIdentifier(string errorMessage, WordSet errorStop)
        {
            ParseIdentifier(errorMessage, errorStop, out var token);
            return token;
        }

        /// <summary>
        /// Parse an identifier.  Error returns false and causes
        /// reject until errorStop unless errorStop is null.
        /// </summary>
        bool ParseIdentifier(string errorMessage, WordSet errorStop, out Token token, bool allowReserved = false)
        {
            token = mToken;
            if (mToken.Type == eTokenType.Identifier 
                || (allowReserved && (mToken.Type == eTokenType.Reserved || mToken.Type == eTokenType.ReservedControl)))
            {
                Accept();
                return true;
            }
            if (errorStop != null)
            {
                if (mToken.Type == eTokenType.Reserved || mToken.Type == eTokenType.ReservedControl)
                    Reject(errorMessage + ", must not be a reserved word", errorStop);
                else
                    Reject(errorMessage + ", must begin with a letter", errorStop);
            }
            return false;
        }

        // Accept the token if it matches.  Returns true if it was accepted.
        bool AcceptMatch(string matchToken)
        {
            if (mTokenName == matchToken)
            {
                Accept();
                return true;
            }
            return false;
        }

        struct ParsePoint
        {
            public Lexer.Enumerator LexerEnum;
            public Token PrevToken;
            public Token Token;
        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.LexerEnum = mLexerEnum;
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mLexerEnum = p.LexerEnum;
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mTokenName = mToken.Name;
        }

        // Accept the current token and advance to the next, skipping all comments.
        // The new token is saved in mToken and the token name is saved in mTokenName.
        // Returns the token that was accepted.  Token is pre-maked with token type
        Token Accept()
        {
            // Already at end of file?
            mPrevToken = mToken;
            if (mTokenName == "")
                return mPrevToken;

            // Read next token, and skip comments
            var firstComment = true;
            do
            {
                // Read next token (set EOF flag if no more tokens on line)
                if (mLexerEnum.MoveNext())
                    mToken = mLexerEnum.Current;
                else
                    mToken = new Token("", 0, 0, eTokenType.Reserved);

                if (mToken.Type == eTokenType.Comment && mTokenName.StartsWith("///"))
                {
                    if (firstComment)
                        mComments.Clear();
                    firstComment = false;
                    mComments.Add(mToken.Name);
                }

            } while (mToken.Type == eTokenType.Comment);
            
            // Reset token info
            mTokenName = mToken.Name;
            mToken.ClearInfo();
            mToken.ClearBits();
            if (mTokenName.Length == 0)
                mToken.Type = eTokenType.Normal;
            else if (mTokenName[0] == '\"')
                mToken.Type = eTokenType.Quote;
            else if (sReservedWords.TryGetValue(mTokenName, out var tokenType))
                mToken.Type = tokenType;
            else if (mTokenName[0] >= '0' && mTokenName[0] <= '9')
                mToken.Type = eTokenType.Number;
            else if (char.IsLetter(mTokenName[0]))
                mToken.Type = eTokenType.Identifier;
            else
                mToken.Type = eTokenType.Normal;

            return mPrevToken;
        }
      
        // Reject the token if it's not qualified
        void RejectTokenIfNotQualified(Token token, Token []qualifiers, string expected, string errorMessage)
        {
            foreach (var t in qualifiers)
                if (t.Name == expected)
                    return;
            RejectToken(token, errorMessage);
        }

        // Reject the given token
        void RejectToken(Token token, string errorMessage)
        {
            mParseError = true;
            token.Reject(errorMessage);
        }

        // Reject all tokens in the list
        void RejectTokens(IEnumerable<Token> tokens, string errorMessage)
        {
            foreach (var token in tokens)
            {
                mParseError = true;
                token.Reject(errorMessage);
            }
        }

        // Reject the current token, then advance until the first stopToken
        void Reject(string errorMessage, WordSet stopTokens)
        {
            mParseError = true;
            mToken.Reject(errorMessage);
            while (!stopTokens.Contains(mToken))
            {
                mToken.Grayed = true;
                Accept();
            }
        }

        /// <summary>
        /// Connect the tokens so the user sees the same info
        /// for both tokens (and both are grayed out when
        /// hovering with the mouse).  
        /// </summary>
        void Connect(Token s1, Token s2)
        {
            // Find tokens that are already connected
            List <Token> tokens = new List<Token>();
            Token []s1Connectors = s1.GetInfo<Token[]>();
            if (s1Connectors != null)
                foreach (Token s in s1Connectors)
                    tokens.Add(s);
            Token []s2Connectors = s2.GetInfo<Token[]>();
            if (s2Connectors != null)
                foreach (Token s in s2Connectors)
                    tokens.Add(s);

            // Add these tokens to the list
            tokens.Add(s1);
            tokens.Add(s2);

            // Set token info
            Token []sa = tokens.ToArray();
            foreach (Token s in sa)
                s.RepaceInfo(sa);
        }

    }

}
