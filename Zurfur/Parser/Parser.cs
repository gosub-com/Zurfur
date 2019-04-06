using System;
using System.Drawing;
using System.Collections.Generic;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Base class for the parser
    /// </summary>
    class Parser
    {
        const bool SHOW_PARSE_TREE = true;

        bool				mParseError;	// Flag is set whenever a parse error occurs
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken;
        Token               mPrevToken;
        List<string>        mComments = new List<string>();

        static readonly string sReservedWordsList = "abstract as base break case catch class const "
            + "continue default delegate do else enum event explicit extern false "
            + "finally fixed for goto if implicit in interface internal is lock namespace "
            + "new null operator out override params private protected pub ro ref "
            + "return sealed sealed1 sizeof stackalloc static struct switch this throw true try "
            + "typeof unsafe using static virtual void volatile while "
            + "async await astart get set yield global partial var where nameof func construct cast";
        static readonly string sReservedControlWords = "using namespace class struct interface func construct if else switch case";
        static WordMap<eTokenType> sReservedWords = new WordMap<eTokenType>();

        static WordSet sClassQualifiers = new WordSet("pub public protected private internal ref "
            + "static extern virtual override ro readonly volatile new async abstract unsafe sealed sealed1");
        static WordSet sTypeParameterQualifiers = new WordSet("in out ref ro");

        static WordSet sOverloadableOperators = new WordSet("+ - * ** / % ~ & | ^ >> <<");
        static WordSet sComparisonOperators = new WordSet("== === != > >= < <=");
        static WordSet sAddOperators = new WordSet("+ - | ^");
        static WordSet sMultiplyOperators = new WordSet("* / % & << >>");
        static WordSet sAssignOperators = new WordSet("= += -= *= /= %= |= &= ^= <<= >>=");


        // TBD: "new" and "#" need to be specially parsed
        static WordSet sUnaryOperators = new WordSet("+ - ! ~ & * new #");


        static WordSet sRejectLine = new WordSet("} ;", true);
        static WordSet sRejectLineUntilOpen = new WordSet("{ } ;", true);
        static WordSet sRejectFuncName = new WordSet("{ } ( [ ;", true);
        static WordSet sRejectFuncParamsParen = new WordSet("{ } ) ;", true);
        static WordSet sRejectFuncParamsBracket = new WordSet("{ } ) ;", true);
        static WordSet sRejectFuncParam = new WordSet("{ } , ) ;", true);
        static WordSet sRejectParen = new WordSet("; ) }", true);
        static WordSet sRejectBracket = new WordSet("; ] }", true);
        static WordSet sRejectAssign = new WordSet("} ; = += -= *= /= %= |= &= ^= <<= >>=", true);


        static Parser()
        {
            sReservedWords.AddWords(sReservedWordsList, eTokenType.Reserved);
            sReservedWords.AddWords(sReservedControlWords, eTokenType.ReservedControl);
        }

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public Parser(Lexer tokens)
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
                Token[] qualifiers = ParseQualifiers(sClassQualifiers);

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
                        Reject("Expecting keyword: 'using', 'namespace', 'class', 'struct', 'interface', or 'func'", sRejectLine);
                        if (mTokenName == "}")
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
            synUsing.QualifiedIdentifiers = ParseQualifiedIdentifier("Expecting a namespace identifier", sRejectLine);
            SkipSemicolon();
            return synUsing;
        }

        SyntaxNamespace ParseNamespaceStatement(SyntaxUnit unit, string []comments)
        {
            var nspace = new SyntaxNamespace();
            nspace.Comments = comments;
            nspace.Keyword = Accept();
            nspace.QualifiedIdentifiers = ParseQualifiedIdentifier("Expecting a namespace identifier", sRejectLine);
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
            synClass.ClassName = ParseTypeName(sRejectLineUntilOpen);

            if (AcceptMatch(":"))
            {
                synClass.BaseClass = ParseTypeName(sRejectLineUntilOpen);
            }

            if (mTokenName == ";")
            {
                RejectToken(mToken, "Empty body not allowed");
                SkipSemicolon();
                return synClass;
            }
            if (AcceptMatch("="))
            {
                synClass.Alias = ParseTypeName(sRejectLine);
                SkipSemicolon();
                return synClass;
            }
            if (mTokenName != "{")
                Reject("Expecting start of " + synClass.Keyword + " body, '{' or ';'", sRejectLineUntilOpen);

            if (mTokenName == "{")
            {
                ParseClassBody(synClass);
            }
            return synClass;
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
                var qualifiers = ParseQualifiers(sClassQualifiers);
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
                        var fieldName = ParseIdentifier("Expecting a variable name", sRejectLine, out var error);
                        if (!error)
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
            if (SHOW_PARSE_TREE)
            {
                ShowExprSyntax(field.Expr);
                eqToken.AddInfo(field.Expr.ToString());
            }
            return field;
        }

        void ShowExprSyntax(SyntaxExpr expr)
        {
            if (expr == null)
                return;
            expr.Function.AddInfo(expr.ToString());
            foreach (var e in expr)
                ShowExprSyntax(e);
        }

        SyntaxTypeName ParseTypeName(WordSet errorStop)
        {
            return ParseTypeName(errorStop, out var error);
        }

        SyntaxTypeName ParseTypeName(WordSet errorStop, out bool error)
        {
            error = false;
            var synType = new SyntaxTypeName();
            synType.Qualifiers = ParseQualifiers(sTypeParameterQualifiers);

            if (AcceptMatch("*"))
            {
                synType.Name = mPrevToken;
                synType.TypeParameters = new SyntaxTypeName[] { ParseTypeName(errorStop, out error) };
                return synType;
            }
            if (AcceptMatch("["))
            {
                synType.Name = mPrevToken;
                if (!AcceptMatch("]"))
                {
                    Reject("Expecting ']'", errorStop);
                    error = true;
                    return synType;
                }
                synType.TypeParameters = new SyntaxTypeName[] { ParseTypeName(errorStop, out error) };
                return synType;
            }

            synType.Name = ParseIdentifier("Expecting a type name", errorStop, out error);
            if (error || !AcceptMatch("<"))
                return synType;

            // Parse type parameters
            var openType = mPrevToken;
            var typeParams = new List<SyntaxTypeName>();
            if (mToken != ">")
                typeParams.Add(ParseTypeName(errorStop, out error));
            while (AcceptMatch(",") && !error)
            {
                typeParams.Add(ParseTypeName(errorStop, out error));
            }

            if (!AcceptMatch(">"))
            {
                Reject("Expecting '>', end of type parameter list", errorStop);
                error = true;
            }
            Connect(mPrevToken, openType);
            synType.TypeParameters = typeParams.ToArray();
            return synType;
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
                synFunc.FuncName = new SyntaxTypeName();
                synFunc.FuncName.Name = mToken;
                if (sOverloadableOperators.Contains(mTokenName))
                    Accept();
                else
                    Reject("Expecting an overloadable operator", sRejectFuncName);
            }
            else if (AcceptMatch("new"))
            {
                // Function name is the constructor
                mPrevToken.Type = eTokenType.ReservedControl;
                synFunc.FuncName = new SyntaxTypeName();
                synFunc.FuncName.Name = mPrevToken;
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
                    synFunc.FuncName = new SyntaxTypeName();
                    synFunc.FuncName.Name = mToken;
                }
                else
                {
                    // Parse type.name
                    synFunc.FuncName = ParseTypeName(sRejectFuncName);
                    if (AcceptMatch("."))
                    {
                        synFunc.ClassName = synFunc.FuncName;
                        synFunc.FuncName = ParseTypeName(sRejectFuncName);
                    }
                }
            }
            // Parse parameters
            if (mToken != "(" && mToken != "[")
                Reject("Expecting '(' or '['", sRejectFuncName);
            if (mToken == "(" || mToken == "[")
                synFunc.Params = ParseFuncParams();

            if (mTokenName != "{" && mTokenName != ";")
                synFunc.Return = ParseTypeName(sRejectFuncParam);

            if (mToken == ";")
            {
                if (!isInterface)
                    RejectTokenIfNotQualified(mToken, qualifiers, "extern", "This function must have 'extern' qualifier");
                SkipSemicolon();
                return synFunc;
            }
            if (mToken != "{")
                Reject("Expecting start of function body, '{' or ';'", sRejectLineUntilOpen);

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
            synParam.Name = ParseIdentifier("Expecting a variable name", sRejectFuncParam, out var error);
            if (error)
                return synParam;
            synParam.TypeName = ParseTypeName(sRejectFuncParam);
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
                return new SyntaxExpr();
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
                        if (mTokenName != "{")
                            Reject("Expecting " + keyword + " body, '{'", sRejectLineUntilOpen);
                        if (mTokenName == "{")
                        {
                            var bodyExpr = ParseStatements();
                            if (mToken == "else" && keyword == "if")
                                statements.Add(new SyntaxExprMulti(keyword, 
                                    new SyntaxExpr[] { conditionExpr, bodyExpr, ParseStatements() }));
                            else
                                statements.Add(new SyntaxExprBinary(keyword, conditionExpr, bodyExpr));
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
                            statements.Add(new SyntaxExpr(keyword));
                        else
                            statements.Add(new SyntaxExprUnary(keyword, ParseExpr()));
                        SkipSemicolon();
                        break;

                    case "throw":
                        Accept();
                        statements.Add(new SyntaxExprUnary(keyword, ParseExpr()));
                        break;

                    default:
                        var result = ParseExpr();
                        if (sAssignOperators.Contains(mToken))
                            result = new SyntaxExprBinary(Accept(), result, ParseExpr());
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
            while (mTokenName == "**")
                result = new SyntaxExprBinary(Accept(), result, ParseUnary());
            return result;
        }

        SyntaxExpr ParseUnary()
        {
            // TBD: "new" operator may be parsed differently.  Also, "cast" operator here?
            if (sUnaryOperators.Contains(mTokenName))
                return new SyntaxExprUnary(Accept(), ParsePrimary());
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
                    accepted = true;
                    var openToken = mToken;
                    var parameters = new List<SyntaxExpr>() { result };
                    ParseParameters(parameters);
                    result = new SyntaxExprMulti(openToken, parameters.ToArray());
                }
                else if (AcceptMatch("."))
                {
                    accepted = true;
                    result = new SyntaxExprBinary(mPrevToken, result,
                        new SyntaxExpr(ParseIdentifier("Expecting identifier", sRejectLine)));
                }
            } while (accepted);
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
            parameters.Add(ParseExpr());
            while (AcceptMatch(","))
                parameters.Add(ParseExpr());

            // If not ended properly, reject this expression
            if (mTokenName != expectedToken)
                Reject("Expecting '" + expectedToken + "' or ','", 
                    openToken == "(" ? sRejectParen : sRejectBracket);

            if (AcceptMatch(expectedToken))
                Connect(openToken, mToken);
        }



        /// <summary>
        /// Parse an atom - number, variable, parenthises
        /// </summary>
        SyntaxExpr ParseAtom()
        {
            if (mTokenName == "")
            {
                Reject("Unexpected end of file", sRejectLine);
                return new SyntaxExpr();
            }

            // Parse parentheses (not function call)
            if (mTokenName == "(")
            {
                var result = ParseParen();

                // Parse cast (TBD: Remove and make `cast` explicit?)
                if (mToken != "" && char.IsLetterOrDigit(mToken, 0) 
                    || mToken == "(" || mToken == "*" || mToken == "&") 
                {
                    result = new SyntaxExprBinary(mPrevToken, result, ParseUnary());
                }
                return result;
            }

            // Parse number
            if (char.IsDigit(mTokenName, 0))
                return new SyntaxExpr(Accept());

            // Parse variable name
            if (char.IsLetter(mTokenName, 0))
            {
                return new SyntaxExpr(ParseIdentifier("Expecting an identifier", sRejectLine));
            }
            Reject("Expecting an identifier, number, parentheses, or expression", sRejectLine);
            return new SyntaxExpr();
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

            // Empty () or []?
            if (mTokenName == expectedToken)
            {
                // Return an empty () or []
                Connect(openToken, mToken);
                Reject("Expecting an expression", rejectTokens);
                SyntaxExpr emptyExpr = new SyntaxExpr(Accept());
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
        /// Error causes reject until errorStop and returns empty array
        /// </summary>
        Token[] ParseQualifiedIdentifier(string errorMessage, WordSet errorStop)
        {
            // Parse first identifier
            var identifier = ParseIdentifier(errorMessage, errorStop, out var error);
            if (error)
                return Token.EmptyArray;
            if (mTokenName != ".")
                return new Token[] { identifier };

            // Parse the rest
            var tokens = new List<Token>();
            tokens.Add(identifier);
            while (AcceptMatch("."))
            {
                identifier = ParseIdentifier(errorMessage, errorStop, out error);
                if (error)
                    return Token.EmptyArray;
                tokens.Add(identifier);
            }
            return tokens.ToArray();
        }

        Token ParseIdentifier(string errorMessage, WordSet errorStop)
        {
            return ParseIdentifier(errorMessage, errorStop, out var error);
        }

        /// <summary>
        /// Parse an identifier.  
        /// Error causes reject until errorStop and returns Token.Name == ""
        /// </summary>
        Token ParseIdentifier(string errorMessage, WordSet errorStop, out bool error)
        {
            if (mTokenName.Length <= 0)
            {
                Reject(errorMessage, errorStop);
                error = true;
                return new Token();
            }
            if (!char.IsLetter(mTokenName[0]))
            {
                Reject(errorMessage + ", must begin with a letter", errorStop);
                error = true;
                return new Token();
            }
            for (int i = 1;  i < mTokenName.Length;  i++)
            {
                if (!char.IsLetterOrDigit(mTokenName[i]) && mTokenName[i] != '_')
                {
                    Reject(errorMessage + ", must contain only letters and numbers", errorStop);
                    error = true;
                    return new Token();
                }
            }
            if (mToken.Type == eTokenType.Reserved || mToken.Type == eTokenType.ReservedControl)
            {
                Reject(errorMessage + ", must not be a reserved word", errorStop);
                error = true;
                return new Token();
            }
            error = false;
            return Accept();
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
            else if (char.IsDigit(mTokenName[0]))
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
