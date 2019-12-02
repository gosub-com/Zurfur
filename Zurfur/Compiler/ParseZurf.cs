using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Base class for the parser
    /// </summary>
    class ParseZurf
    {
        // These tokens are ambiguous, so get replaced while parsing
        public const string VIRTUAL_TOKEN_TYPE_ARG_LIST = "<>";
        public const string VIRTUAL_TOKEN_INITIALIZER = "{}";

        public const string PTR = "^";
        public const string XOR = "~";
        public const string NEWVAR = "@";
        public const string SSEP = "=>";  // Statement separator

        ParseZurfCheck mZurfParseCheck;

        bool				mParseError;	// Flag is set whenever a parse error occurs
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken = new Token(";");
        Token               mPrevToken = new Token(";");
        Token               mLastVisibleToken = new Token(";");
        StringBuilder       mComments = new StringBuilder();
        SyntaxField         mLastField;
        List<Token>         mExtraTokens = new List<Token>();

        // Be kind to GC
        Queue<List<SyntaxExpr>>   mExprCache = new Queue<List<SyntaxExpr>>();

        SyntaxUnit mUnit;

        public bool ParseError => mParseError;

        /// <summary>
        /// Additional tokens (e.g. the ";" inserted at the end of the line).
        /// Only the ones with errors.
        /// </summary>
        public Token[] ExtraTokens() => mExtraTokens.ToArray();

        // NOTE: >=, >>, and >>= are omitted and handled at parser level.
        public const string MULTI_CHAR_TOKENS = "<< <= == != && || += -= *= /= %= &= |= " + XOR + "= <<= => -> !== === :: .. ... " + SSEP;

        // Add semicolons to all lines, except for:
        static WordSet sEndLineSkipSemicolon = new WordSet("; { [ ( ,");
        static WordSet sBeginLineSkipSemicolon = new WordSet("{ } ] ) + - * / % | & " + XOR + " || && == != = "
                                                + ": ? . , > << <= < => -> .. :: !== === += -= *= /= %= &= |= " + XOR + "= " 
                                                + "where is in as extends implements " + SSEP);
        Token mInsertedToken;

        static readonly string sReservedWordsList = "abstract as base break case catch class const "
            + "continue default delegate do else enum event explicit extern false defer use "
            + "finally fixed for goto if implicit in interface internal is lock namespace module include "
            + "new null operator out override pub public private protected readonly ro ref mut "
            + "return sealed sealed1 sizeof stackalloc heapalloc static struct switch this throw true try "
            + "typeof unsafe using static virtual volatile while dowhile asm managed unmanaged "
            + "async await astart func afunc get set yield global partial var where nameof of "
            + "mixin trait youdo extends implements implement";
        static readonly string sReservedControlWords = "using namespace module include class struct enum interface "
            + "func afunc prop get set operator if else switch await for while dowhile _";
        static WordMap<eTokenType> sReservedWords = new WordMap<eTokenType>();
        static WordSet sReservedIdentifierVariables = new WordSet("null this true false default");

        static WordSet sClassFuncFieldQualifiers = new WordSet("pub public protected private internal unsafe "
            + "static const sealed sealed1 abstract virtual override new volatile ref ro readonly");

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sFieldDefTypeQualifiers = new WordSet("ref");
        static WordSet sFuncDefReturnTypeQualifiers = new WordSet("ro ref");
        static WordSet sFuncDefParamTypeQualifiers = new WordSet("out ref");
        static WordSet sTypeDefParamQualifiers = new WordSet("in out");
        static WordSet sFuncCallParamQualifiers = new WordSet("out ref");

        static WordSet sAllowConstraintKeywords = new WordSet("class struct unmanaged");
        static WordSet sAutoImplementMethod = new WordSet("default youdo extern");

        public static WordSet sOverloadableOperators = new WordSet("+ - * / % in implicit explicit");
        static WordSet sComparisonOperators = new WordSet("== != < <= > >= === !== in"); // For '>=', use VIRTUAL_TOKEN_GE
        static WordSet sAddOperators = new WordSet("+ - | " + XOR);
        static WordSet sMultiplyOperators = new WordSet("* / % & << >>"); // For '>>', use VIRTUAL_TOKEN_SHIFT_RIGHT
        static WordSet sAssignOperators = new WordSet("= += -= *= /= %= |= &= " + XOR + "= <<= >>=");
        static WordSet sUnaryOperators = new WordSet("+ - ! & " + XOR + " " + PTR);

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // This seems stange because something like `a = F<T1,T2>;` is not a valid expression
        // The following symbols allow us to call functions, create types, access static members, and cast
        // For example `F<T1>()` to call a function or constructor, `F<T1>.Name` to access a static or member,
        // and #F<T1>(expression) to cast.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) .");

        static WordSet sStatementEndings = new WordSet("; => }");
        static WordSet sStatementsDone = new WordSet("} func namespace class struct interface enum", true);
        static WordSet sRejectAnyStop = new WordSet("=> ; { } namespace class struct interface enum if else for while throw switch case func afunc prop get set", true);
        static WordSet sRejectForCondition = new WordSet("in");
        static WordSet sRejectFuncName = new WordSet("( [");
        static WordSet sRejectFuncParam = new WordSet(", )");
        static WordSet sRejectIndexerParams = new WordSet("[");
        static WordSet sRejectUntilOpenBrace = new WordSet("{");
        static WordSet sRejectUntilEq = new WordSet("=");

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
            mZurfParseCheck = new ParseZurfCheck(this);
            mLexer = tokens;
            mLexerEnum = new Lexer.Enumerator(mLexer);
            Accept();
        }

        // Be kind to GC
        List<SyntaxExpr> NewExprList()
        {
            if (mExprCache.Count == 0)
                return new List<SyntaxExpr>();
            return mExprCache.Dequeue();
        }

        SyntaxExpr []FreeExprList(List<SyntaxExpr> expr)
        {
            var array = expr.ToArray();
            expr.Clear();
            if (mExprCache.Count < 10)
                mExprCache.Enqueue(expr);
            return array;
        }

        public SyntaxUnit Parse()
        {
            mUnit = new SyntaxUnit();

            if (Debugger.IsAttached)
            {
                // Failure causes error in dubugger
                ParseCompilationUnit();
                mZurfParseCheck.Check(mUnit);
            }
            else
            {
                try
                {
                    ParseCompilationUnit();
                    mZurfParseCheck.Check(mUnit);
                }
                catch (Exception ex1)
                {
                    var errorMessage = "Parse failure: " + ex1.Message + "\r\n\r\n" + ex1.StackTrace;
                    RejectToken(mToken, errorMessage);
                    if (mZurfParseCheck.LastToken != null)
                        RejectToken(mZurfParseCheck.LastToken, errorMessage);
                    while (mTokenName != "")
                        Accept();
                    var lexEnum = new Lexer.Enumerator(mLexer);
                    lexEnum.MoveNext();
                    RejectToken(lexEnum.Current, errorMessage);
                }
            }
            if (mTokenName != "")
                RejectToken(mToken, "Parse error: Expecting end of file");
            while (mTokenName != "")
                Accept();

            return mUnit;
        }

        /// <summary>
        /// Parse the file
        /// </summary>
        void ParseCompilationUnit()
        {
            mUnit = new SyntaxUnit();
            var qualifiers = new List<Token>();

            while (mTokenName != "")
            {
                ParseScopeStatements(null);
                if (mTokenName != "")
                {
                    RejectToken(mToken, "Unexpected symbol at top level scope");
                    Accept();
                }
            }
        }

        /// <summary>
        /// Outer keyword is "" when not in a class or other containing structure
        /// Parse using, namespace, enum, interface, struct, class, func, or field
        /// </summary>
        void ParseScopeStatements(SyntaxScope parentScope)
        {
            var qualifiers = new List<Token>();
            while (mTokenName != "" && mTokenName != "}")
            {
                while (mTokenName == "[")
                {
                    ParseInitializer(); // TBD: Store in parse tree!
                }

                // Read qualifiers
                qualifiers.Clear();
                while (sClassFuncFieldQualifiers.Contains(mTokenName))
                {
                    // "new" can be qualifier or constructor
                    if (mToken == "new" && Peek() == "(")
                        break;
                    qualifiers.Add(Accept());
                }

                var keyword = mToken;
                switch (mTokenName)
                {
                    case ";":
                        RejectQualifiers(qualifiers, "Expecting a statement after qualifier");
                        break;

                    case "{":
                    case "=>":
                        RejectQualifiers(qualifiers, "Unexpected qualifiers");
                        RejectToken(mToken, "Unexpected '" + mTokenName + "'.  Expecting a keyword, such as 'class', 'func', etc. before the start of a new scope.");
                        Accept();
                        break;

                    case "use":
                        mUnit.Using.Add(ParseUsingStatement());
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'using' statement");
                        if (parentScope != null)
                            RejectToken(keyword, "Using statements must come before the namespace");
                        break;

                    case "namespace":
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'namespace' statement");
                        if (parentScope != null && parentScope.IsType)
                            RejectToken(keyword, "Namespace statements must not be inside a class/enum/interface body");
                        var newNamespace = ParseNamespaceStatement(parentScope, mUnit);
                        if (parentScope == null && newNamespace != null)
                            parentScope = newNamespace;
                        break;

                    case "interface":
                    case "enum":
                    case "struct":
                    case "class":
                        ParseClass(parentScope, qualifiers);
                        break;

                    case "operator":
                    case "new":
                    case "func":
                    case "afunc":
                        keyword.Type = eTokenType.ReservedControl;  // Fix "new" keyword
                        ParseMethod(parentScope, qualifiers);
                        break;

                    case "this":
                        ParseIndexer(parentScope, qualifiers);
                        break;

                    case "prop":
                        ParseProperty(parentScope, qualifiers);
                        break;

                    default:
                        if (sRejectAnyStop.Contains(mTokenName))
                        {
                            RejectToken(mToken, "Unexpected token or reserved word");
                            Accept();
                            break;
                        }

                        Token fieldName = null;
                        if (AcceptMatch("default"))
                            fieldName = mPrevToken;
                        else if (!ParseIdentifier("Expecting a keyword like class or field name identifier", out fieldName))
                            break;

                        SyntaxField field;
                        if (parentScope != null && parentScope.Keyword == "enum")
                            field = ParseEnumField(parentScope, qualifiers, fieldName);
                        else
                            field = ParseField(parentScope, qualifiers, fieldName);

                        if (!mToken.Error)
                            mUnit.Fields.Add(field);
                        break;
                }
                AcceptSemicolonOrReject();
            }
        }

        /// <summary>
        /// Parse a new scope, expecting "{" as the first character
        /// </summary>
        private void ParseNewScopeStatements(SyntaxScope parentScope, string scopeKeyword, string scopeName)
        {
            if (!AcceptMatchOrReject("{"))
                return;

            var openToken = mPrevToken;
            ParseScopeStatements(parentScope);

            if (mTokenName != "}")
            {
                Reject("Expecting '}' while parsing " + scopeKeyword + " body of '" + scopeName + "'");
                RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace"
                                                        : "This scope has an error on its closing brace");
            }
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);
        }


        void RejectQualifiers(List<Token> qualifiers, string errorMessage)
        {
            RejectQualifiers(qualifiers, null, errorMessage);
        }

        // Reject tokens with errorMessage.  Reject all of them if acceptSet is null.
        void RejectQualifiers(List<Token> qualifiers, WordSet acceptSet, string errorMessage)
        {
            foreach (var token in qualifiers)
            {
                if (token == "readonly")
                {
                    RejectToken(token, "Use 'ro' instead");
                    continue;
                }
                if (acceptSet == null || !acceptSet.Contains(token))
                    RejectToken(token, errorMessage);
            }
        }

        SyntaxUsing ParseUsingStatement()
        {
            var synUsing = new SyntaxUsing();
            synUsing.Keyword = Accept();
            var tokens = new List<Token>();
            ParseQualifiedIdentifier(tokens, "Expecting a namespace identifier");
            synUsing.NamePath = tokens.ToArray();
            return synUsing;
        }

        SyntaxNamespace ParseNamespaceStatement(SyntaxScope parentScope, SyntaxUnit unit)
        {
            var keyWord = Accept();
            SyntaxNamespace newNs = null;
            SyntaxScope parentNs = parentScope;
            do
            {
                if (!ParseIdentifier("Expecting a namespace identifier", out var t1))
                    return newNs;

                newNs = new SyntaxNamespace();
                newNs.ParentScope = parentNs;
                newNs.Name = t1;
                newNs.Keyword = keyWord;
                parentNs = newNs;
            } while (AcceptMatch("."));

            newNs.Comments = mComments.ToString();
            mUnit.Namespaces.Add(newNs);

            if (parentScope == null)
            {
                parentScope = newNs;
                if (mTokenName == "{")
                {
                    Reject("The first namespace cannot start a new scope");
                    AcceptMatch("{");
                }
            }
            else
            {
                if (mTokenName != "{")
                    Reject("Expecting '{', all namespaces after the first one require a new scope");
                if (mTokenName == "{")
                    ParseNewScopeStatements(newNs, newNs.Keyword, newNs.FullName);
            }

            return newNs;
        }

        // Parse class, struct, interface, or enum
        void ParseClass(SyntaxScope parentScope, List<Token> qualifiers)
        {
            var synClass = new SyntaxType();
            synClass.Qualifiers = qualifiers.ToArray();
            synClass.ParentScope = parentScope;
            synClass.Comments = mComments.ToString();
            synClass.Keyword = Accept();

            // Parse class name and type parameters
            if (!ParseIdentifier("Expecting a type name", out synClass.Name))
                return;
            synClass.TypeParams = ParseTypeParams(sRejectFuncName);
            mUnit.Types.Add(synClass);

            // Parse base classes
            if (AcceptMatch(":"))
            {
                var baseClasses = NewExprList();
                baseClasses.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
                while (AcceptMatch(","))
                {
                    baseClasses.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
                }
                synClass.BaseClasses = FreeExprList(baseClasses);
            }

            synClass.Constraints = ParseConstraints();

            if (mTokenName == ";")
            {
                RejectToken(mToken, "Empty body not allowed");
                return;
            }
            if (AcceptMatch("="))
            {
                synClass.Alias = ParseTypeDef(sEmptyWordSet, sEmptyWordSet);
                return;
            }
            if (mTokenName != "{")
                Reject("Expecting start of " + synClass.Keyword + " body, '{'");
            if (mTokenName != "{")
                return;

            // Parse class body
            ParseNewScopeStatements(synClass, synClass.Keyword.Name, synClass.Name);
        }

        SyntaxExpr ParseTypeParams(WordSet errorStop)
        {
            if (mToken != "<")
                return SyntaxExpr.Empty;

            var typeParams = NewExprList();
            var openToken = Accept();
            do
            {
                // Parse in or out qualifier
                Token qualifier = null;
                if (sTypeDefParamQualifiers.Contains(mTokenName))
                    qualifier = Accept();
                if (!ParseIdentifier("Expecting a type name", out var name))
                    break;
                if (qualifier == null)
                    typeParams.Add(new SyntaxToken(name));
                else
                    typeParams.Add(new SyntaxUnary(name, new SyntaxToken(qualifier)));
                if (mTokenName == ",")
                    Connect(openToken, mToken);
            } while (AcceptMatch(","));

            if (AcceptMatchOrReject(">", "while parsing type parameters"))
                Connect(mPrevToken, openToken);

            return new SyntaxMulti(Token.Empty, FreeExprList(typeParams));
        }

        private SyntaxConstraint[] ParseConstraints()
        {
            if (mTokenName != "where")
                return null;

            List<SyntaxConstraint> constraints = new List<SyntaxConstraint>();
            while (mTokenName == "where")
                constraints.Add(ParseConstraint());
            return constraints.ToArray();
        }

        SyntaxConstraint ParseConstraint()
        {
            var constraint = new SyntaxConstraint();
            constraint.Keyword = Accept();
            if (!ParseIdentifier("Expecting a type name", out constraint.GenericTypeName))
                return constraint;
            if (!AcceptMatchOrReject(":", "while parsing constraint"))
                return constraint;

            var constraintTypeNames = NewExprList();
            do
            {
                if (sAllowConstraintKeywords.Contains(mToken))
                {
                    mToken.Type = eTokenType.Reserved;
                    constraintTypeNames.Add(new SyntaxToken(Accept()));
                    continue;
                }
                constraintTypeNames.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
            } while (AcceptMatch(","));
            constraint.TypeNames = FreeExprList(constraintTypeNames);
            return constraint;                       
        }

        /// <summary>
        /// Current token must already be checked for validity
        /// </summary>
        SyntaxField ParseField(SyntaxScope parentScope, List<Token> qualifiers, Token name)
        {
            var field = new SyntaxField();
            field.ParentScope = parentScope;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            field.Name = name;
            mLastField = field; // Allow us to pick up comments on this line

            // Parse type name
            field.TypeName = ParseTypeDef(sFieldDefTypeQualifiers, sEmptyWordSet);
            field.InitExpr = ParseAssignmentOrConstructor();

            return field;
        }

        /// <summary>
        /// Current token must already be checked for validity
        /// </summary>
        SyntaxField ParseEnumField(SyntaxScope parentScope, List<Token> qualifiers, Token name)
        {
            var field = new SyntaxField();
            field.ParentScope = parentScope;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            field.Name = name;
            mLastField = field; // Allow us to pick up comments on this line

            // Optionally initialize
            if (AcceptMatch("="))
            {
                // Initialize via assignment
                field.InitExpr = ParseExpr();
            }
            return field;
        }


        void ParseIndexer(SyntaxScope parentScope, List<Token> qualifiers)
        {
            var keyword = Accept();
            keyword.Type = eTokenType.ReservedControl;

            if (mTokenName != "[")
            {
                Reject("Expecting '[' after indexer keyword", sRejectIndexerParams);
                if (mTokenName != "[")
                    return;
            }

            SyntaxExpr parameters = null;
            parameters = ParseFuncParamsDef();
            var returnType = ParseTypeDef(sFuncDefReturnTypeQualifiers, sRejectFuncParam);
            ParsePropertyBody(parentScope, qualifiers, keyword, keyword, parameters, returnType);
        }

        private void ParseProperty(SyntaxScope parentScope, List<Token> qualifiers)
        {
            var propKeyword = Accept();
            if (!ParseIdentifier("Error parsing property name", out var name))
                return;
            var propField = ParseField(parentScope, qualifiers, name);
            if (!mToken.Error)
                ParsePropertyBody(parentScope, qualifiers, propKeyword, propField.Name, null, propField.TypeName);
        }

        void ParsePropertyBody(SyntaxScope parentScope, List<Token> qualifiers, Token keyword, Token name, SyntaxExpr parameters, SyntaxExpr returnType)
        {
            var synFunc = new SyntaxFunc();
            synFunc.ParentScope = parentScope;
            synFunc.Comments = mComments.ToString();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = keyword;
            synFunc.Name = name;
            synFunc.Params = parameters;
            synFunc.ReturnType = returnType;

            if (mTokenName == "=>")
            {
                synFunc.Statements = ParseStatements("Property", StatementsType.PropertyBody);
                mUnit.Funcs.Add(synFunc);
                return;
            }
            if (!AcceptMatchOrReject("{", "or '=>' to define property body"))
            {
                mUnit.Funcs.Add(synFunc);
                return;
            }
            var openBrace = mPrevToken;
            bool gotGetOrSet = false;
            while (mTokenName != "}" && mTokenName != "")
            {
                switch (mTokenName)
                {
                    case ";":
                        Accept();
                        break;
                    case "get":
                    case "set":
                        gotGetOrSet = true;
                        synFunc.Keyword = Accept();
                        synFunc.Statements = ParseStatements("property", StatementsType.MethodBody);
                        mUnit.Funcs.Add(synFunc);
                        break;
                    default:
                        if (!Reject("Expecting 'get', 'set', or '}'"))
                            Accept();
                        break;
                }
            }
            if (AcceptMatchOrReject("}", "end of property"))
            {
                if (!gotGetOrSet)
                    RejectToken(mPrevToken, "Expecting to have a 'get' or 'set' function");
                Connect(openBrace, mPrevToken);
            }
        }

        /// <summary>
        /// Func, construct, operator
        /// </summary>
        void ParseMethod(SyntaxScope parentScope, List<Token> qualifiers)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc();
            synFunc.ParentScope = parentScope;
            synFunc.Comments = mComments.ToString();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = Accept();

            switch (synFunc.Keyword)
            {
                case "new":
                    ParseNewDef(synFunc);
                    synFunc.Constraints = ParseConstraints();
                    break;
                case "afunc":
                case "func":
                    if (!ParseFuncNameDef(out synFunc.ClassName, out synFunc.Name))
                    {
                        synFunc.Name = null;
                        break;
                    }
                    ParseFuncDef(out synFunc.TypeParams, out synFunc.Params, out synFunc.ReturnType);
                    synFunc.Constraints = ParseConstraints();
                    break;
                case "operator":
                    ParseOperatorDef(synFunc);
                    break;
                default:
                    throw new Exception("Error parsing function def");
            }

            synFunc.Statements = ParseStatements("func", StatementsType.MethodBody);
            if (synFunc.Name != null)
                mUnit.Funcs.Add(synFunc);
        }

        bool ParseFuncNameDef(out SyntaxExpr className, out Token funcName)
        {
            // Try parsing a class name first (Optional, must be followed by "::")
            className = null;
            var pp = SaveParsePoint();
            if (mToken.Type == eTokenType.Identifier)
            {
                if (!ParseTypeName(out className, null)
                        || !AcceptMatch("::")) 
                {
                    className = null;
                    RestoreParsePoint(pp);
                }
            }

            // Parse function name and type parameters
            return ParseIdentifier("Expecting a function", out funcName);
        }

        private void ParseFuncDef(out SyntaxExpr typeParams, out SyntaxExpr parameters, out SyntaxExpr returnType)
        {
            typeParams = ParseTypeParams(sRejectFuncName);

            // Parse parameters
            if (mToken != "(" && mToken != "[")
                Reject("Expecting '(' or '['", sRejectFuncName);
            parameters = null;
            if (mToken == "(" || mToken == "[")
                parameters = ParseFuncParamsDef();

            // Parse return type
            returnType = null;
            if (BeginsTypeDef(sFuncDefReturnTypeQualifiers, mToken))
                returnType = ParseTypeDef(sFuncDefReturnTypeQualifiers, sRejectFuncParam);
        }

        bool BeginsTypeDef(WordSet qualifiers, Token token)
        {
            // TBD: White or black list?
            //return token != "," && token != ";" && token != ")" && token != "]" 
            //        && token != "}" && token != "=>" && token != "{";
            return token.Type == eTokenType.Identifier
               || qualifiers.Contains(token)
               || token == "func" || token == "afunc"
               || token == PTR || token == "?";
        }


        private void ParseNewDef(SyntaxFunc synFunc)
        {
            synFunc.Name = mPrevToken;
            if (mToken != "(")
                Reject("Expecting '(' after '" + synFunc.Keyword + "' token", sRejectFuncName);
            if (mToken == "(")
                synFunc.Params = ParseFuncParamsDef();
        }

        private void ParseOperatorDef(SyntaxFunc synFunc)
        {
            // Function name is an operator
            synFunc.Name = mToken;
            if (sOverloadableOperators.Contains(mTokenName))
                Accept();
            else
                Reject("Expecting an overloadable operator", sRejectFuncName);

            // Parse parameters
            if (mToken != "(" && mToken != "[")
                Reject("Expecting '(' or '['", sRejectFuncName);
            if (mToken == "(" || mToken == "[")
                synFunc.Params = ParseFuncParamsDef();

            synFunc.ReturnType = ParseTypeDef(sFuncDefReturnTypeQualifiers, sRejectFuncParam);
        }

        SyntaxExpr ParseFuncParamsDef()
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '[' or '(' while parsing function parameters");

            // Parse parameters
            var closeToken = openToken.Name == "(" ? ")" : "]";
            var parameters = NewExprList();
            if (mTokenName != closeToken)
                parameters.Add(ParseFuncParamDef());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseFuncParamDef());
            }

            // Ellipse to signify repeated parameters
            if (AcceptMatch("..."))
                mPrevToken.AddWarning("Repeated parameters not supported yet");

            if (AcceptMatchOrReject(closeToken))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(Token.Empty, FreeExprList(parameters));
        }

        SyntaxExpr ParseFuncParamDef()
        {
            if (!ParseIdentifier("Expecting a variable name", out var name, sRejectFuncParam))
                return new SyntaxError();
            var type = ParseTypeDef(sFuncDefParamTypeQualifiers, sRejectFuncParam);
            return new SyntaxUnary(name, type);
        }

        enum StatementsType
        {
            Statements,
            Switch,
            MethodBody,
            PropertyBody
        }

        /// <summary>
        /// Parse '{ statements }' or '=> statement'
        /// </summary>
        private SyntaxExpr ParseStatements(string errorMessage, StatementsType type = StatementsType.Statements)
        {
            if (mToken == SSEP)
                return ParseStatementsSeparator(errorMessage, type);

            // Require '{'
            if (!AcceptMatchOrReject("{", "or '" + SSEP + "'"))
                return new SyntaxError();

            var openToken = mPrevToken;
            var statements = NewExprList();
            while (!sStatementsDone.Contains(mTokenName))
                ParseStatement(statements);

            if (AcceptMatchOrReject("}", "while parsing " + errorMessage + " statement"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(openToken, FreeExprList(statements));
        }

        /// <summary>
        /// Parse '=> statement', including things like '=> default get'
        /// </summary>
        SyntaxExpr ParseStatementsSeparator(string errorMessage, StatementsType type)
        {
            if (mToken != SSEP)
                throw new Exception("Compiler error: Expecting '=>'");

            int maxConjoined;
            if (type == StatementsType.Statements)
                maxConjoined = 2;
            else if (type == StatementsType.Switch)
                maxConjoined = 0;
            else
                maxConjoined = 1;

            var count = 0;
            var statementKeyword = mToken;
            var statements = NewExprList();
            while (AcceptMatch(SSEP))
            {
                if (count++ >= maxConjoined)
                {
                    RejectToken(mPrevToken, "Maximum '=>' conjoined statements is " + maxConjoined);
                    break;
                }
                if (mToken == ";" || mToken == "{" || mToken == "}")
                {
                    Reject("Expecting a statement or expression after '" + SSEP + "'");
                    break;
                }
                if (type == StatementsType.MethodBody || type == StatementsType.PropertyBody)
                {
                    if (sAutoImplementMethod.ContainsKey(mTokenName))
                    {
                        Token getToken = Token.Empty;
                        Token setToken = Token.Empty;
                        Token setVisibility = Token.Empty;
                        var autoKeyword = Accept();
                        if (type == StatementsType.PropertyBody)
                        {
                            if (AcceptMatchOrReject("get", "to specify property to implement"))
                                getToken = mPrevToken;
                            if (AcceptMatch("private") || AcceptMatch("protected"))
                                setVisibility = mPrevToken;
                            if (AcceptMatch("set"))
                                setToken = mPrevToken;
                            getToken.Type = eTokenType.Reserved;
                            setToken.Type = eTokenType.Reserved;
                        }
                        statements.Add(new SyntaxBinary(autoKeyword, new SyntaxToken(getToken), new SyntaxToken(setToken)));
                        break;
                    }
                }
                if ((mToken.Type == eTokenType.Reserved || mToken.Type == eTokenType.ReservedControl)
                            && mToken != "return" && mToken != "break" // TBD: Make into wordset
                            && mToken != "continue" && mToken != "throw" && mToken != "sizeof")
                {
                    Reject("Only 'return', 'break', 'continue', and 'throw' reserved words allowed after '" + SSEP + "'.  Use '{}' instead");
                    break;
                }

                ParseStatement(statements, false);
                if (mTokenName == ";" && !mToken.Invisible)
                    RejectToken(mToken, "Not allowed to have a ';' on a statement starting with '" + SSEP + "'");
            }
            return new SyntaxMulti(CreateInvisible(statementKeyword, "=>"), FreeExprList(statements));
        }

        private void ParseStatement(List<SyntaxExpr> statements, bool requireSemicolon = true)
        {
            var keyword = mToken;
            switch (mToken)
            {
                case ";":
                    break;

                case "=>":
                    RejectToken(mToken, "Unexpected '=>'");
                    Accept();
                    break;


                case "{":
                    RejectToken(mToken, "Unnecessary scope is not allowed");
                    statements.Add(ParseStatements("scope"));
                    break;
                
                case "defer":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    break;

                case "use":
                    var useToken = Accept();
                    if (mTokenName != "@")
                    {
                        Reject("Expecting '@' then a new variable decaration");
                        break;
                    }
                    ParseNewVarStatment(statements);
                    break;

                case NEWVAR:
                    ParseNewVarStatment(statements);
                    break;

                case "while":
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements("while")));
                    break;

                case "if":
                    Accept();
                    var ifCondition = ParseExpr();
                    var ifBody = ParseStatements("if");
                    AcceptSemicolonOrReject();
                    requireSemicolon = false;
                    if (mToken != "else")
                    {
                        // IF (condition) (body)
                        statements.Add(new SyntaxBinary(keyword, ifCondition, ifBody));
                    }
                    else
                    {
                        // IF (condition) (body) (else-body)
                        Accept();
                        if (mTokenName != "if")
                        {
                            statements.Add(new SyntaxMulti(keyword, ifCondition, ifBody, ParseStatements("else")));
                        }
                        else
                        {
                            // IF (condition) (body) ( (else-if-body) )
                            var elseIfToken = mToken;
                            var elseStatements = NewExprList();
                            ParseStatement(elseStatements);
                            statements.Add(new SyntaxMulti(keyword, ifCondition, ifBody, 
                                               new SyntaxMulti(CreateInvisible(elseIfToken, "{"), FreeExprList(elseStatements))));
                        }
                    }
                    break;

                case "else":
                    Reject("Else must follow 'if' statement");
                    Accept();
                    break;

                case "for":
                    Accept();
                    if (AcceptMatch(NEWVAR))
                        mPrevToken.Type = eTokenType.CreateVariable;
                    else
                        RejectToken(mToken, "Expecting '" + NEWVAR + "'"); // TBD Remove this?
                    var forVariable = new SyntaxToken(ParseIdentifier("Expecting a loop variable", sRejectForCondition));

                    AcceptMatchOrReject("in");
                    var forCondition = ParseExpr();
                    statements.Add(new SyntaxMulti(keyword, forVariable, forCondition, ParseStatements("for")));
                    break;

                case "throw":
                case "return":
                    Accept();
                    if (sStatementEndings.Contains(mTokenName))
                        statements.Add(new SyntaxToken(keyword));
                    else
                        statements.Add(new SyntaxUnary(keyword, ParseExpr()));
                    break;

                case "continue":
                case "break":
                    statements.Add(new SyntaxToken(Accept()));
                    break;

                case "switch":
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements("switch", StatementsType.Switch)));
                    break;

                case "default":
                    statements.Add(new SyntaxToken(Accept()));
                    AcceptMatchOrReject(":");
                    requireSemicolon = false;
                    break;

                case "case":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    AcceptMatchOrReject(":");
                    requireSemicolon = false;
                    break;

                default:
                    if (sRejectAnyStop.Contains(mTokenName))
                    {
                        RejectToken(mToken, "Unexpected token or reserved word");
                        Accept();
                        break;
                    }

                    var result = ParseExpr();
                    InterceptAndReplaceGT();
                    if (sAssignOperators.Contains(mToken))
                        result = new SyntaxBinary(Accept(), result, ParseAssignment());
                    statements.Add(result);
                    break;
            }
            if (requireSemicolon)
                AcceptSemicolonOrReject();
        }


        /// <summary>
        /// Parse the right side of an assignment or constructor (when the left side is a type name)
        /// </summary>
        SyntaxExpr ParseAssignmentOrConstructor()
        {
            if (mTokenName == "(")
            {
                // Initialize via constructor
                var initToken = mToken;
                var parameters = NewExprList();
                ParseParen(parameters, true);
                return new SyntaxMulti(initToken, FreeExprList(parameters));
            }

            if (mTokenName == "=")
            {
                return new SyntaxUnary(Accept(), ParseAssignment());
            }

            return new SyntaxToken(Token.Empty);
        }

        /// <summary>
        /// Parse the right side of an assignment statement
        /// </summary>
        SyntaxExpr ParseAssignment()
        {
            if (mTokenName == "{")
                return ParseInitializer();
            return ParseExpr();
        }

        void ParseNewVarStatment(List<SyntaxExpr> statements)
        {
            mToken.Type = eTokenType.CreateVariable;
            var newVarToken = Accept();
            if (!ParseIdentifier("Expecting variable name", out var newVarName))
                return;

            SyntaxExpr type = mTokenName == "="
                ? new SyntaxToken(Token.Empty)
                : ParseTypeDef(sEmptyWordSet, sEmptyWordSet);

            SyntaxExpr initExpr = ParseAssignmentOrConstructor();

            statements.Add(new SyntaxMulti(newVarToken, new SyntaxToken(newVarName), type, initExpr));
        }


        ///  Parse expression (doesn't include ',' or '=' statements)
        SyntaxExpr ParseExpr()
        {
            return ParseLambda();
        }

        SyntaxExpr ParseLambda()
        {
            var result = ParseTernary();
            if (mTokenName == "->")
            {
                var lambdaToken = Accept();
                if (mTokenName == "{")
                    result = new SyntaxBinary(lambdaToken, result, ParseStatements("lambda"));
                else
                    result = new SyntaxBinary(lambdaToken, result, ParseTernary());
            }
            return result;
        }

        SyntaxExpr ParseTernary()
        {
            var result = ParseConditionalOr();
            if (mTokenName == "?")
            {
                var operatorToken = Accept();
                var firstConditional = ParseConditionalOr();
                if (mTokenName != ":")
                {
                    Reject("Expecting a ':' to separate expression for the ternary '?' operator");
                    return result;
                }
                Connect(mToken, operatorToken);
                Accept();
                result = new SyntaxMulti(operatorToken, result, firstConditional, ParseConditionalOr());
            }
            return result;
        }


        SyntaxExpr ParseConditionalOr()
        {
            var result = ParseConditionalAnd();
            while (mTokenName == "||")
                result = new SyntaxBinary(Accept(), result, ParseConditionalAnd());
            return result;
        }

        SyntaxExpr ParseConditionalAnd()
        {
            var result = ParseComparison();
            while (mTokenName == "&&")
                result = new SyntaxBinary(Accept(), result, ParseComparison());
            return result;
        }

        SyntaxExpr ParseComparison()
        {
            var result = ParseRange();
            InterceptAndReplaceGT();
            if (sComparisonOperators.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, ParseRange());
                InterceptAndReplaceGT();
                if (sComparisonOperators.Contains(mTokenName))
                    Reject("Compare operators are not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseRange()
        {
            var result = ParseAdd();
            if (mTokenName == ".." || mTokenName == "::")
            {
                result = new SyntaxBinary(Accept(), result, ParseAdd());
                if (mTokenName == ".." || mTokenName == "::")
                    Reject("Range operator is not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseAdd()
        {
            var result = ParseMultiply();
            while (sAddOperators.Contains(mTokenName))
                result = new SyntaxBinary(Accept(), result, ParseMultiply());
            return result;
        }

        SyntaxExpr ParseMultiply()
        {
            var result = ParseUnary();
            InterceptAndReplaceGT();
            while (sMultiplyOperators.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, ParseUnary());
                InterceptAndReplaceGT();
                if (mTokenName == "<<" || mTokenName == ">>")
                {
                    Reject("Shift operators are not associative, must use parentheses");
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// Combine >=, >>, and >>= into one token
        /// </summary>
        void InterceptAndReplaceGT()
        {
            if (mTokenName != ">")
                return;
            var peek = mLexerEnum.PeekOnLine();
            if (peek.X != mToken.X + mTokenName.Length)
                return;
            if (peek != "=" && peek != ">")
                return;
            var token = mToken;
            Accept();
            var virtualToken = mTokenName + peek;
            if (virtualToken == ">>")
            {
                peek = mLexerEnum.PeekOnLine();
                if (peek.X == token.X + virtualToken.Length && peek == "=")
                {
                    Accept();
                    virtualToken = ">>=";
                }                    
            }
            // Replace with a virtual token
            mToken = CreateInvisible(mToken, virtualToken);
            mTokenName = mToken.Name;
        }

        SyntaxExpr ParseUnary()
        {
            if (sUnaryOperators.Contains(mTokenName))
            {
                return new SyntaxUnary(Accept(), ParseUnary());
            }
            return ParsePrimary();
        }

        SyntaxExpr ParsePrimary()
        {
            if (mTokenName == "switch")
                return ParseSwitchExpression();

            if (mTokenName == "sizeof")
            {
                var sizeofToken = Accept();
                if (AcceptMatchOrReject("(", "start of sizeof operator"))
                {
                    var sizeofOpen = mPrevToken;
                    if (ParseTypeName(out var sizeofType, sEmptyWordSet))
                    {
                        if (AcceptMatchOrReject(")", "end of sizeof operator"))
                        {
                            Connect(sizeofOpen, mPrevToken);
                            return new SyntaxUnary(sizeofToken, sizeofType);
                        }
                    }
                }
                return new SyntaxError(sizeofToken);
            }

            // Prefix cast: #type(expression)
            if (mTokenName == "#")
            {
                var operatorToken = Accept();
                if (ParseTypeName(out var typeName, sEmptyWordSet)
                    && AcceptMatchOrReject("(", "start of cast expression", false))
                {
                    var openToken = mPrevToken;
                    var castExpr = ParseExpr();
                    if (AcceptMatchOrReject(")", "end of cast expression", false))
                    {
                        Connect(openToken, mPrevToken);
                        var result2 = (SyntaxExpr)new SyntaxBinary(operatorToken, typeName, castExpr);
                        result2 = PrimaryPostfix(result2);
                        return result2;
                    }
                }
                return new SyntaxError(operatorToken);
            }

            var result = ParseAtom();
            result = PrimaryPostfix(result);
            return result;
        }

        /// <summary>
        /// Primary postfix: function call '()', array '[]', member access '.', cast
        /// </summary>
        private SyntaxExpr PrimaryPostfix(SyntaxExpr result)
        {
            bool accepted;
            do
            {
                accepted = false;
                if (mTokenName == "(" || mTokenName == "[")
                {
                    // Function call or array access
                    accepted = true;
                    var openToken = mToken;
                    var parameters = NewExprList();
                    parameters.Add(result);
                    ParseParen(parameters, mTokenName == "(");
                    result = new SyntaxMulti(openToken, FreeExprList(parameters));
                }
                else if (AcceptMatch("."))
                {
                    // Member access
                    accepted = true;
                    result = new SyntaxBinary(mPrevToken, result,
                        new SyntaxToken(ParseIdentifier("Expecting identifier")));
                }
                else if (mTokenName == "<" && mPrevToken.Type == eTokenType.Identifier)
                {
                    // Possibly a type argument list.  Let's try it and find out.
                    var p = SaveParsePoint();
                    var openTypeToken = mToken;
                    var typeArgs = NewExprList();
                    if (ParseTypeArgumentList(typeArgs, null) 
                            && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Type argument list
                        accepted = true;
                        typeArgs.Insert(0, result);
                        result = new SyntaxMulti(CreateInvisible(openTypeToken, VIRTUAL_TOKEN_TYPE_ARG_LIST), FreeExprList(typeArgs));
                    }
                    else
                    {
                        // Failed, restore the enumerator back to before trying type argument list
                        FreeExprList(typeArgs);
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
                Reject("Unexpected end of file");
                return new SyntaxToken(mToken);
            }

            // Parse parentheses: expression, or lambda parameters (not a function call)
            if (mTokenName == "(")
            {
                // Parse expressions
                var openToken = mToken;
                var expressions = NewExprList();
                ParseParen(expressions, false);

                // Disallow old style casts
                if (mTokenName == "(" || mToken.Type == eTokenType.Identifier)
                    RejectToken(mToken, "Old style cast not allowed, use #type(expession)");

                // Use ")" to differentiate between function call and ordering
                return new SyntaxMulti(mPrevToken, FreeExprList(expressions));
            }

            // Number, string, identifier
            if (mToken.Type == eTokenType.Number || mToken.Type == eTokenType.Quote)
            {
                return new SyntaxToken(Accept());
            }
            if (mToken.Type == eTokenType.Identifier)
            {
                var identifier = Accept();
                if (mToken.Type == eTokenType.Quote && identifier == "tr")
                {
                    identifier.Type = eTokenType.Reserved;
                    return new SyntaxUnary(Accept(), new SyntaxToken(identifier));
                }

                return new SyntaxToken(identifier);
            }
            // Misc reserved words
            if (sReservedIdentifierVariables.Contains(mTokenName))
            {
                mToken.Type = eTokenType.Reserved;
                return new SyntaxToken(Accept());
            }

            var errorToken = mToken;
            Reject("Expecting an identifier, number, string literal, or parentheses");
            return new SyntaxToken(errorToken);
        }

        /// <summary>
        /// Read the open '(' or '[' and then parse the parameters into parameters
        /// </summary>
        void ParseParen(List<SyntaxExpr> parameters, bool isFunc)
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '(' or '[' while parsing parameters");

            // Parse parameters
            var expectedToken = openToken == "(" ? ")" : "]";
            if (mTokenName != expectedToken)
            {
                parameters.Add(isFunc ? ParseFuncCallParameter() : ParseExpr());
                while (AcceptMatch(","))
                {
                    Connect(openToken, mPrevToken);
                    parameters.Add(isFunc ? ParseFuncCallParameter() : ParseExpr());
                }
            }

            if (AcceptMatchOrReject(expectedToken, "or ','"))
                Connect(openToken, mPrevToken);
        }

        SyntaxExpr ParseFuncCallParameter()
        {
            if (mTokenName == "{")
                return ParseInitializer();

            // Allow 'ref' or 'out' qualifier
            if (sFuncCallParamQualifiers.Contains(mTokenName))
            {                 
                var qualifier = Accept();
                if (mTokenName == NEWVAR)
                    return new SyntaxBinary(qualifier, 
                            new SyntaxToken(Accept()), 
                            new SyntaxToken(ParseIdentifier("Expecting a variable name")));
                return new SyntaxUnary(qualifier, new SyntaxToken(Accept()));
            }

            // Allow NEWVAR
            //if (mTokenName == NEWVAR)


            return ParseExpr();
        }

        /// <summary>
        /// Initializer, starting with '{' or '[' for attribute
        /// </summary>
        SyntaxExpr ParseInitializer()
        {
            if (mTokenName != "{" && mTokenName != "[")
                throw new Exception("Compiler error: Expecting '{' or '[' while parsing initializer");

            var close = mTokenName == "{" ? "}" : "]";
            var openToken = Accept();
            var parameters = NewExprList();
            if (mTokenName != close)
            {
                parameters.Add(ParseInitializerPair());
                while (AcceptMatch(","))
                {
                    Connect(openToken, mPrevToken);
                    parameters.Add(ParseInitializerPair());
                }
            }

            if (AcceptMatchOrReject(close, "or ','"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(CreateInvisible(openToken, VIRTUAL_TOKEN_INITIALIZER), FreeExprList(parameters));
        }

        SyntaxExpr ParseInitializerPair()
        {
            bool isInitializer = mTokenName == "{";
            var expr = isInitializer ? ParseInitializer() : ParseExpr();
            if (mTokenName != ":")
                return expr;
            return new SyntaxBinary(Accept(), expr, mTokenName == "{" ? ParseInitializer() : ParseExpr());
        }

        SyntaxExpr ParseSwitchExpression()
        {
            mToken.Type = eTokenType.Reserved;
            var keyword = Accept();
            var match = ParseExpr();
            if (mTokenName != "{")
                Reject("expecting '}'", sRejectUntilOpenBrace);
            var openToken = mToken;
            if (!AcceptMatch("{"))
                return new SyntaxToken(keyword);

            // Parse expressions
            var parameters = NewExprList();
            if (mTokenName != "}")
            {
                parameters.Add(ParseSwitchClause());
                while (AcceptMatch(","))
                {
                    Connect(openToken, mPrevToken);
                    parameters.Add(ParseSwitchClause());
                }
            }

            // If not ended properly, reject this expression
            if (mTokenName != "}")
                Reject("Expecting '}' or ','");
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);

            return new SyntaxBinary(keyword, match, new SyntaxMulti(mPrevToken, FreeExprList(parameters)));
        }

        SyntaxExpr ParseSwitchClause()
        {
            var e1 = ParseExpr();
            if (AcceptMatchOrReject("=>", "while parsing switch statement"))
                return new SyntaxBinary(mPrevToken, e1, ParseExpr());
            return e1;
        }

        /// <summary>
        /// Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        SyntaxExpr ParseTypeDef(WordSet qualifiers, WordSet errorStop)
        {
            if (qualifiers.Contains(mTokenName))
            {
                return new SyntaxUnary(Accept(), ParseTypeDef(qualifiers, errorStop));
            }
            else if (mTokenName == "func" || mTokenName == "afunc")
            {
                return ParseLambdaDef(); 
            }
            else
            {
                if (!ParseTypeName(out var result, errorStop))
                    return new SyntaxToken(mToken);
                return result;
            }
        }

        SyntaxExpr ParseLambdaDef()
        {
            var keyword = Accept();
            keyword.Type = eTokenType.Reserved;
            ParseFuncDef(out var typeParams, out var parameters, out var returnType);
            return new SyntaxToken(mToken);
        }

        /// <summary>
        /// Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        bool ParseTypeName(out SyntaxExpr result, WordSet errorStop)
        {
            // Unary operators '^' and '?
            result = null;
            if (mToken == PTR || mToken == "?")
            {
                var token = Accept();
                if (!ParseTypeName(out var expr, errorStop))
                    return false;
                result = new SyntaxUnary(token, expr);
                return true;
            }

            if (!ParseIdentifier("Expecting a type name", out var typeName, errorStop))
                return false;

            result = new SyntaxToken(typeName);

            bool accepted;
            do
            {
                accepted = false;
                if (AcceptMatch("."))
                {
                    accepted = true;
                    var dotToken = mPrevToken;
                    if (!ParseIdentifier("Expecting a type name", out var dotTypeName, errorStop))
                        return false;
                    result = new SyntaxBinary(dotToken, result, new SyntaxToken(dotTypeName));
                }

                if (mToken == "<")
                {
                    accepted = true;
                    var openTypeToken = mToken;
                    var typeArgs = NewExprList();
                    if (!ParseTypeArgumentList(typeArgs, errorStop))
                    {
                        FreeExprList(typeArgs);
                        return false;
                    }
                    typeArgs.Insert(0, result);
                    result = new SyntaxMulti(CreateInvisible(openTypeToken, VIRTUAL_TOKEN_TYPE_ARG_LIST), FreeExprList(typeArgs));
                }
            } while (accepted);
            return true;
        }

        /// <summary>
        /// Try parsing a type argument list. 
        /// Error causes reject until errorStop unless errorStop is null.
        bool ParseTypeArgumentList(List<SyntaxExpr> typeArgs, WordSet errorStop)
        {
            var openToken = Accept();
            if (openToken.Name != "<")
                throw new Exception("Compiler error: Expecting '<' while parsing type argument list");

            // Parse the first parameter
            if (!ParseTypeName(out var p, errorStop))
                return false;
            typeArgs.Add(p);

            // Parse the rest of the parameters
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                if (!ParseTypeName(out p, errorStop))
                    return false;
                typeArgs.Add(p);
            }

            if (!AcceptMatch(">"))
            {
                if (errorStop != null)
                    Reject("Expecting '>' to end the type argument list", errorStop);
                return false;
            }
            Connect(openToken, mPrevToken);
            return true;
        }
        
        /// <summary>
        /// Parse a qualified identifier.  
        /// Error causes reject until errorStop and returns null.
        /// </summary>
        void ParseQualifiedIdentifier(List<Token> tokens, string errorMessage)
        {
            // Parse first identifier
            if (!ParseIdentifier(errorMessage, out var t1))
                return;
            tokens.Add(t1);

            // Parse the rest
            while (AcceptMatch(".")
                    &&  ParseIdentifier(errorMessage, out var t2))
            { 
                tokens.Add(t2);
            }
        }

        /// <summary>
        /// Parse an identifier.  Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        Token ParseIdentifier(string errorMessage, WordSet errorStop = null)
        {
            ParseIdentifier(errorMessage, out var token, errorStop);
            return token;
        }

        /// <summary>
        /// Parse an identifier.  Error returns false and causes
        /// reject until end of statement or extraStops hit
        /// </summary>
        bool ParseIdentifier(string errorMessage, out Token token, WordSet extraStops = null)
        {
            token = mToken;
            if (mToken.Type == eTokenType.Identifier)
            {
                Accept();
                return true;
            }

            if (extraStops == null)
                extraStops = sEmptyWordSet;

            if (mToken.Type == eTokenType.Reserved || mToken.Type == eTokenType.ReservedControl)
                Reject(errorMessage + ", must not be a reserved word", extraStops);
            else
                Reject(errorMessage + ", must begin with a letter", extraStops);
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

        // Accept match, otherwise reject until match token, then try one more time
        bool AcceptMatchOrReject(string matchToken, string message = null, bool tryToRecover = true)
        {
            if (AcceptMatch(matchToken))
                return true;
            Reject("Expecting '" + matchToken + "'" + (message == null ? "" : ", " + message), 
                        tryToRecover ? new WordSet(matchToken) : null);
            if (tryToRecover)
                return AcceptMatch(matchToken);
            return false;
        }

        void AcceptSemicolonOrReject()
        {
            if (AcceptMatch(";"))
                return;

            if (sStatementEndings.Contains(mTokenName))
                return;

            Reject("Expecting ';' or end of line");
            AcceptMatch(";");
        }


        struct ParsePoint
        {
            public Lexer.Enumerator LexerEnum;
            public Token PrevToken;
            public Token Token;
            public Token Inserted;
            public bool ParseError;
        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.LexerEnum = mLexerEnum;
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            p.Inserted = mInsertedToken;
            p.ParseError = mParseError;
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mLexerEnum = p.LexerEnum;
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mTokenName = mToken.Name;
            mInsertedToken = p.Inserted;
            mParseError = p.ParseError;
        }

        Token Peek()
        {
            var pp = SaveParsePoint();
            Accept();
            var peek = mToken;
            RestoreParsePoint(pp);
            return peek;
        }

        // Accept the current token and advance to the next, skipping all comments.
        // The new token is saved in mToken and the token name is saved in mTokenName.
        // Returns the token that was accepted.  Token is pre-maked with token type
        Token Accept()
        {
            // Already at end of file?
            if (mTokenName == "")
                return mToken;

            var prevToken = mToken;
            mPrevToken = prevToken;
            if (mInsertedToken != null)
            {
                mToken = mInsertedToken;
                mTokenName = mToken.Name;
                mInsertedToken = null;
                return mPrevToken;
            }

            // Read next token (use "" as EOF flag)
            GetNextToken();

            if (mTokenName == "//" || mTokenName == "///")
                ParseComments();

            if (mTokenName != "")
                mLastVisibleToken = mToken;

            if (mTokenName.Length == 0)
                mToken.Type = eTokenType.Normal;
            else if (mTokenName[0] == '\"')
                mToken.Type = eTokenType.Quote;
            else if (mTokenName[0] >= '0' && mTokenName[0] <= '9')
                mToken.Type = eTokenType.Number;
            else if (sReservedWords.TryGetValue(mTokenName, out var tokenType))
                mToken.Type = tokenType;
            else if (char.IsLetter(mTokenName[0]) || mTokenName[0] == '_')
                mToken.Type = eTokenType.Identifier;
            else
                mToken.Type = eTokenType.Normal;

            if (mTokenName.Length != 0 &&  (mTokenName[0] == '_' || mTokenName[mTokenName.Length-1] == '_') && mTokenName != "_")
                RejectToken(mToken, "Identifiers may not begin or end with '_'");

            if (mTokenName == "__pfail")
                throw new Exception("Parse fail test");

            // Insert a ';' after each new line
            if (prevToken.Y != mToken.Y
                    && !sEndLineSkipSemicolon.Contains(prevToken)
                    && !sBeginLineSkipSemicolon.Contains(mTokenName))
            {
                // Mark at end of white space, but before any comment
                var line = mLexer.GetLine(prevToken.Y);
                int x = prevToken.X + prevToken.Name.Length;
                while (x < line.Length && line[x] <= 32)
                    x++;

                mInsertedToken = mToken;
                mToken = new Token(";", x, prevToken.Y);
                mToken.Invisible = true;
                mTokenName = ";";
            }
            return mPrevToken;
        }

        void GetNextToken()
        {
            if (mLexerEnum.MoveNext())
                mToken = mLexerEnum.Current;
            else
                mToken = new Token("");
            mToken.Clear();
            mTokenName = mToken.Name;
        }

        void ParseComments()
        {
            mComments.Clear();
            while (mTokenName == "//" || mTokenName == "///")
            {
                var tokenType = mToken == "///" ? eTokenType.PublicComment : eTokenType.Comment;
                mToken.Type = tokenType;
                GetNextToken();
                bool isCodeComment = false;
                while (!mToken.Boln && mToken != "")
                {
                    mToken.Type = tokenType;
                    if (tokenType == eTokenType.PublicComment)
                    {
                        // TBD: Need some work to reconstruct comment spacing properly
                        mComments.Append(mToken.Name);
                        mComments.Append(" ");
                        if (mLastField != null && mLastField.Name.Y == mToken.Y)
                        {
                            mLastField.Comments += mComments.ToString();
                            mComments.Clear();
                        }
                        mLastField = null;
                    }
                    var lcComment = mTokenName.ToLower();
                    if (lcComment.StartsWith("http://") 
                            || lcComment.StartsWith("https//")
                            || lcComment.StartsWith("file://"))
                    {
                        mToken.Url = mToken.Name;
                        mToken.Underline = true;
                    }
                    if (mTokenName == "`")
                    {
                        isCodeComment = !isCodeComment;
                        mToken.Subtype = eTokenSubtype.Normal;
                    }
                    else
                    {
                        mToken.Subtype = isCodeComment ? eTokenSubtype.CodeInComment : eTokenSubtype.Normal;
                    }
                    GetNextToken();
                }

            }

        }


        // Reject the given token
        public void RejectToken(Token token, string errorMessage)
        {
            mParseError = true;
            token.AddError(errorMessage);

            if (token.Invisible)
            {
                if (!mExtraTokens.Contains(token))
                    mExtraTokens.Add(token);
            }

            // If the error is after the end of file, put it on the last visible token
            if (token.Name == "")
                mLastVisibleToken.AddError(errorMessage);
        }

        // Reject the current token, then advance until the first stopToken
        // Returns TRUE if any token was accepted
        bool Reject(string errorMessage, WordSet extraStops = null)
        {
            RejectToken(mToken, errorMessage);
            if (extraStops == null)
                extraStops = sEmptyWordSet;
            bool accepted = false;
            while (!sRejectAnyStop.Contains(mToken) && !extraStops.Contains(mToken))
            {
                mToken.Grayed = true;
                Accept();
                accepted = true;
            }
            return accepted;
        }

        void Connect(Token t1, Token t2)
        {
            Token.Connect(t1, t2);
        }

        Token CreateInvisible(Token token, string text)
        {
            var newToken = new Token(text, token.X, token.Y, eTokenType.Normal);
            newToken.Invisible = true;
            Connect(token, newToken);
            return newToken;
        }


    }

}
