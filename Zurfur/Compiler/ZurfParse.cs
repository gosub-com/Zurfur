using System;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Base class for the parser
    /// </summary>
    class ZurfParse
    {
        // TODO:
        // cast syntax review
        // interface - implicit down conversion, explicit duck typing, no cast to object type, statatic functions
        // Show vertical connectors in editor
        // Tabs vs spaces
        // Invisible ";"
        // @identifier type

        // These tokens are ambiguous, so get replaced while parsing
        public const string VIRTUAL_TOKEN_TYPE_ARG_LIST = "<>";
        const string VIRTUAL_TOKEN_SHIFT_RIGHT = ">>";
        const string VIRTUAL_TOKEN_GE = ">=";
        const string VIRTUAL_TOKEN_LAMBDA = "()";
        const string VIRTUAL_TOKEN_INITIALIZER = "{}";

        public const string PTR = "^";
        public const string XOR = "~";
        public const string NEWVAR = "@";

        ZurfParseCheck mZurfParseCheck;

        bool				mParseError;	// Flag is set whenever a parse error occurs
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken = new Token(";", -1, -1);
        Token               mPrevToken = new Token(";", -1, -1);
        List<string>        mComments = new List<string>();

        SyntaxUnit mUnit;

        // NOTE: >> and >= are omitted and handled at parser level.
        //       TBD: Need to handle >>= as well
        public const string TokenSymbols = "<< <= == != && || += -= *= /= %= &= |= " + XOR + "= <<= => === :: .. ...";

        // Add semicolons to all lines, except for:
        static WordSet sEndLineSkipSemicolon = new WordSet("; { [ ( ,");
        static WordSet sBeginLineSkipSemicolon = new WordSet("; { } ] ) + - * / % | & " + XOR + " || && == != = "
                                                    + ": ? . , > << <= < => .. :: === += -= *= /= %= &= |= " + XOR + "= else where is");
        Token mInsertedToken;

        static readonly string sReservedWordsList = "abstract as to base break case catch class const "
            + "continue default delegate do else enum event explicit extern false defer use "
            + "finally fixed for goto if implicit in interface internal is lock namespace module include "
            + "new null operator out override pub public private protected readonly ro ref mut "
            + "return sealed sealed1 sizeof stackalloc heapalloc static struct switch this throw true try "
            + "typeof unsafe using static virtual volatile while dowhile asm managed unmanaged "
            + "async await astart func afunc get set yield global partial var where nameof cast";
        static readonly string sReservedControlWords = "using namespace module include class struct enum interface "
            + "func afunc prop get set operator if else switch await for while dowhile _";
        static WordMap<eTokenType> sReservedWords = new WordMap<eTokenType>();
        static WordSet sReservedIdentifierVariables = new WordSet("null this true false default");

        static WordSet sClassFuncFieldQualifiers = new WordSet("pub public protected private internal unsafe "
            + "extern static const sealed sealed1 abstract virtual override new volatile ref ro readonly");

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sFieldDefTypeQualifiers = new WordSet("ref");
        static WordSet sFuncDefReturnTypeQualifiers = new WordSet("ro ref");
        static WordSet sFuncDefParamTypeQualifiers = new WordSet("out ref");
        static WordSet sTypeDefParamQualifiers = new WordSet("in out");
        static WordSet sFuncCallParamQualifiers = new WordSet("out ref");

        static WordSet sAllowConstraintKeywords = new WordSet("class struct unmanaged");

        public static WordSet sOverloadableOperators = new WordSet("+ - * / in");
        static WordSet sComparisonOperators = new WordSet("== != < <= > >= === in"); // For '>=', use VIRTUAL_TOKEN_GE
        static WordSet sAddOperators = new WordSet("+ - | " + XOR);
        static WordSet sMultiplyOperators = new WordSet("* / % & << >>"); // For '>>', use VIRTUAL_TOKEN_SHIFT_RIGHT
        static WordSet sAssignOperators = new WordSet("= += -= *= /= %= |= &= " + XOR + "= <<= >>=");
        static WordSet sUnaryOperators = new WordSet("+ - ! & " + XOR + " " + PTR);

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // This seems stange because something like `a = F<T1,T2>;` is not a valid expression
        // The followign symbols allow us to call functions, create types, access static members, and cast
        // For example `F<T1>()` to call a function or constructor, `F<T1>.Name` to access a static or member,
        // and (F<T1>)Name to cast.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) .");

        static WordSet sStatementsDone = new WordSet("} func using namespace module class struct interface enum", true);
        static WordSet sRejectAnyStop = new WordSet("; { } using namespace module class struct interface enum if else for while throw switch case func afunc prop get set", true);
        static WordSet sRejectForCondition = new WordSet("in");
        static WordSet sRejectFuncName = new WordSet("( [");
        static WordSet sRejectIndexerParams = new WordSet("[");
        static WordSet sRejectUntilOpenBrace = new WordSet("{");
        static WordSet sRejectUntilColon = new WordSet(":");
        static WordSet sRejectFuncParam = new WordSet(", )");

        static ZurfParse()
        {
            sReservedWords.AddWords(sReservedWordsList, eTokenType.Reserved);
            sReservedWords.AddWords(sReservedControlWords, eTokenType.ReservedControl);
        }

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public ZurfParse(Lexer tokens)
        {
            mZurfParseCheck = new ZurfParseCheck(this);
            mLexer = tokens;
            mLexerEnum = new Lexer.Enumerator(mLexer);
            Accept();
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
        void ParseScopeStatements(SyntaxClass parentClass)
        {
            var qualifiers = new List<Token>();
            while (mTokenName != "" && mTokenName != "}")
            {
                var attributes = ParseAttributes();

                // Read qualifiers
                qualifiers.Clear();
                while (sClassFuncFieldQualifiers.Contains(mTokenName))
                {
                    // "new" can be qualifier or constructor
                    if (mToken == "new" && Peek() == "(")
                        break;
                    qualifiers.Add(Accept());
                }


                var comments = mComments.ToArray();
                var keyword = mToken;
                switch (mTokenName)
                {
                    case ";":
                        RejectQualifiers(qualifiers, "Expecting a statement after qualifier");
                        SkipSemicolon();
                        break;

                    case "{":
                        RejectQualifiers(qualifiers, "Unexpected qualifiers");
                        RejectToken(mToken, "Unexpected start of scope.  Expecting a keyword, such as 'class', 'func', etc. before the start of a new scope.");
                        Accept();
                        break;

                    case "using":
                        mUnit.Using.Add(ParseUsingStatement());
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'using' statement");
                        if (parentClass != null)
                            RejectToken(keyword, "Using statements must not be inside a class body");
                        else if (mUnit.Namespaces.Count != 0)
                            RejectToken(keyword, "Using statements must come before the namespace");
                        SkipSemicolon();
                        break;

                    case "namespace":
                        mUnit.Namespaces.Add(ParseNamespaceStatement(mUnit));
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'namespace' statement");
                        if (parentClass != null)
                            RejectToken(keyword, "Namespace statements must not be inside a class body");
                        SkipSemicolon();
                        break;

                    case "enum":
                        ParseClass(parentClass, qualifiers);
                        break;

                    case "interface":
                        ParseClass(parentClass, qualifiers);
                        break;

                    case "struct":
                        ParseClass(parentClass, qualifiers);
                        break;

                    case "class":
                        ParseClass(parentClass, qualifiers);
                        break;

                    case "operator":
                    case "new":
                    case "func":
                    case "afunc":
                        keyword.Type = eTokenType.ReservedControl;  // Fix "new" keyword
                        mUnit.Funcs.Add(ParseMethod(parentClass, qualifiers));
                        break;

                    case "this":
                        ParseIndexer(parentClass, qualifiers);
                        break;

                    case "prop":
                        var propKeyword = Accept();
                        var propField = ParseField(parentClass, qualifiers);
                        ParsePropertyBody(parentClass, qualifiers, propKeyword, propField.Name, null, propField.TypeName);
                        break;

                    default:
                        if (sRejectAnyStop.Contains(mTokenName))
                        {
                            RejectToken(mToken, "Unexpected token or reserved word");
                            Accept();
                            break;
                        }

                        if (mToken.Type != eTokenType.Identifier)
                        {
                            RejectToken(mToken, "Expecting a field name identifier or a keyword: using, namespace, class, struct, interface, enum, func, or a field name identifier.");
                            Accept();
                            break;
                        }

                        mUnit.Fields.Add(ParseField(parentClass, qualifiers));
                        SkipSemicolon();
                        break;
                }
            }
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
            synUsing.QualifiedIdentifiers = TryParseQualifiedIdentifier("Expecting a namespace identifier");
            return synUsing;
        }

        SyntaxNamespace ParseNamespaceStatement(SyntaxUnit unit)
        {
            var nspace = new SyntaxNamespace();
            nspace.Comments = mComments.ToArray();
            nspace.Keyword = Accept();
            nspace.QualifiedIdentifiers = TryParseQualifiedIdentifier("Expecting a namespace identifier");
            return nspace;
        }

        /// <summary>
        /// Returns null if there are no attributes
        /// </summary>
        SyntaxExpr ParseAttributes()
        {
            if (mTokenName != "[")
                return null;
            var openToken = mToken;
            var attributes = new List<SyntaxExpr>();
            while (AcceptMatch("["))
            {
                if (mToken == "]")
                {
                    RejectToken(mToken, "Attribute list may not be empty");
                }
                else
                {
                    attributes.Add(ParseExpr());
                    while (AcceptMatch(","))
                        attributes.Add(ParseExpr());
                }
                if (AcceptMatchOrReject("]", "end of attributes"))
                    Connect(openToken, mPrevToken);
            }
            return new SyntaxMulti(openToken, attributes.ToArray());
        }

        // Parse class, struct, interface, or enum
        void ParseClass(SyntaxClass parentClass, List<Token> qualifiers)
        {
            var synClass = new SyntaxClass();
            synClass.Qualifiers = qualifiers.ToArray();
            synClass.Namespace = mUnit.CurrentNamespace;
            synClass.ParentClass = parentClass;
            synClass.Comments = mComments.ToArray();
            synClass.Keyword = Accept();
            var classIdentifier = mToken;

            // Parse class name and type parameters
            if (!ParseIdentifier("Expecting a type name", out synClass.Name))
                return;
            synClass.TypeParams = ParseTypeParams(sRejectFuncName);

            // Parse base classes
            if (AcceptMatch(":"))
            {
                var baseClasses = new List<SyntaxExpr>();
                baseClasses.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
                while (AcceptMatch(","))
                {
                    baseClasses.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
                }
                synClass.BaseClasses = baseClasses.ToArray();
            }

            synClass.Constraints = ParseConstraints();

            if (mTokenName == ";")
            {
                RejectToken(mToken, "Empty body not allowed");
                SkipSemicolon();
                mUnit.Classes.Add(synClass);
                return;
            }
            if (AcceptMatch("="))
            {
                synClass.Alias = ParseTypeDef(sEmptyWordSet, sEmptyWordSet);
                SkipSemicolon();
                mUnit.Classes.Add(synClass);
                return;
            }
            if (mTokenName != "{")
                Reject("Expecting start of " + synClass.Keyword + " body, '{'");
            if (mTokenName != "{")
            {
                mUnit.Classes.Add(synClass);
                return;
            }

            // Parse class body
            var openToken = Accept();
            ParseScopeStatements(synClass);

            if (mTokenName != "}")
            {
                Reject("Expecting '}' while parsing " + synClass.Keyword.Name + " body"
                    + (classIdentifier.Type == eTokenType.Identifier ? " of '" + classIdentifier + "'" : ""));
                RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace"
                                                        : "This scope has an error on its closing brace");
            }
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);

            mUnit.Classes.Add(synClass);
        }

        SyntaxExpr ParseTypeParams(WordSet errorStop)
        {
            if (mToken != "<")
                return SyntaxExpr.Empty;

            var typeParams = new List<SyntaxExpr>();
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

            return new SyntaxMulti(Token.Empty, typeParams.ToArray());
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

            var constraintTypeNames = new List<SyntaxExpr>();
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
            constraint.TypeNames = constraintTypeNames.ToArray();
            return constraint;                       
        }

        SyntaxField ParseField(SyntaxClass parentClass, List<Token> qualifiers)
        {
            var field = new SyntaxField();
            field.Namespace = mUnit.CurrentNamespace;
            field.ParentClass = parentClass;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToArray();
            field.Name = ParseIdentifier("Expecting field or property name");

            // Parse type name (but not for enum)
            bool isEnum = parentClass != null && parentClass.Keyword.Name == "enum";
            if (!isEnum)
                field.TypeName = ParseTypeDef(sFieldDefTypeQualifiers, sEmptyWordSet);

            // Optionally initialize
            var initToken = mToken;
            if (AcceptMatch("="))
            {
                // Initialize via assignment
                field.InitToken = initToken;
                field.InitExpr = ParseExpr();
            }
            else if (mToken == "(" && !isEnum)
            {
                // Initialize via constructor
                field.InitToken = initToken;
                var parameters = new List<SyntaxExpr>();
                ParseParen(parameters, true);
                field.InitExpr = new SyntaxMulti(initToken, parameters.ToArray());
            }
            return field;
        }

        void ParseIndexer(SyntaxClass parentClass, List<Token> qualifiers)
        {
            var keyword = Accept();
            keyword.Type = eTokenType.ReservedControl;

            if (mTokenName != "[")
                Reject("Expecting '[' after indexer keyword", sRejectIndexerParams);

            SyntaxExpr parameters = null;
            if (mTokenName == "[")
                parameters = ParseFuncParamsDef();

            var returnType = ParseTypeDef(sFuncDefReturnTypeQualifiers, sRejectFuncParam);

            ParsePropertyBody(parentClass, qualifiers, keyword, keyword, parameters, returnType);
        }

        void ParsePropertyBody(SyntaxClass parentClass, List<Token> qualifiers, Token keyword, Token name, SyntaxExpr parameters, SyntaxExpr returnType)
        {
            var synFunc = new SyntaxFunc();
            synFunc.Namespace = mUnit.CurrentNamespace;
            synFunc.ParentClass = parentClass;
            synFunc.Comments = mComments.ToArray();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = keyword;
            synFunc.Name = name;
            synFunc.Params = parameters;
            synFunc.ReturnType = returnType;

            if (mTokenName == "=>")
            {
                Accept();
                synFunc.Statements = new SyntaxUnary(synFunc.Keyword, ParseExpr());
                mUnit.Funcs.Add(synFunc);
                return;
            }
            if (!AcceptMatchOrReject("{", "or '=>' to define property body"))
            {
                mUnit.Funcs.Add(synFunc);
                return;
            }
            var openBrace = mPrevToken;
            while (mTokenName != "}" && mTokenName != "")
            {
                switch (mTokenName)
                {
                    case ";":
                        Accept();
                        break;
                    case "get":
                    case "set":
                        synFunc.Keyword = Accept();
                        if (mToken != "set" && mToken != "get" && mToken != "}")
                            synFunc.Statements = ParseMethodBody();
                        mUnit.Funcs.Add(synFunc);
                        break;
                    default:
                        if (!Reject("Expecting 'get', 'set', or '}'"))
                            Accept();
                        break;
                }
            }
            if (AcceptMatchOrReject("}", "end of property"))
                Connect(openBrace, mPrevToken);
        }

        /// <summary>
        /// Func, construct, operator
        /// </summary>
        SyntaxFunc ParseMethod(SyntaxClass parentClass, List<Token> qualifiers)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc();
            synFunc.Namespace = mUnit.CurrentNamespace;
            synFunc.ParentClass = parentClass;
            synFunc.Comments = mComments.ToArray();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = Accept();

            switch (synFunc.Keyword)
            {
                case "afunc":
                case "func":
                    ParseFuncNameDef(out synFunc.ClassName, out synFunc.Name);
                    ParseFuncDef(out synFunc.TypeParams, out synFunc.Params, out synFunc.ReturnType);
                    synFunc.Constraints = ParseConstraints();
                    break;
                case "new":
                    ParseNewDef(synFunc);
                    synFunc.Constraints = ParseConstraints();
                    break;
                case "operator":
                    ParseOperatorDef(synFunc);
                    break;
                default:
                    throw new Exception("Error parsing function def");
            }

            synFunc.Statements = ParseMethodBody();
            return synFunc;
        }

        private SyntaxExpr ParseMethodBody()
        {
            switch (mTokenName)
            {
                case ";":
                    SkipSemicolon();
                    return null;
                case "}":
                    return null;
                case "{":
                    return ParseStatements();
                case "=>":
                    return new SyntaxUnary(Accept(), ParseExpr());
                default:
                    Reject("Expecting start of method body, '{', '=>', or ';'");
                    if (mToken == "{")
                        return ParseStatements();
                    return null;
            }
        }

        bool ParseFuncNameDef(out SyntaxExpr className, out Token funcName)
        {
            // Try parsing a class name first (Optional, must be followed by "::")
            var pp = SaveParsePoint();
            className = ParseTypeDef(sEmptyWordSet, sEmptyWordSet);
            if (!AcceptMatch("::"))
            {
                className = null;
                RestoreParsePoint(pp);
            }

            // Parse function name and type parameters
            return ParseIdentifier("Expecting a type name", out funcName);
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

        private void ParseNewDef(SyntaxFunc synFunc)
        {
            synFunc.Name = mToken;
            if (mToken != "(")
                Reject("Expecting '(' after 'new' token", sRejectFuncName);

            // Parse parameters
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
            var parameters = new List<SyntaxExpr>();
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

            return new SyntaxMulti(Token.Empty, parameters.ToArray());
        }

        SyntaxExpr ParseFuncParamDef()
        {
            if (!ParseIdentifier("Expecting a variable name", out var name, sRejectFuncParam))
                return new SyntaxToken(Token.Empty);
            var type = ParseTypeDef(sFuncDefParamTypeQualifiers, sRejectFuncParam);
            return new SyntaxUnary(name, type);
        }

        private SyntaxExpr ParseStatements()
        {
            // Read open token, '{'
            if (!AcceptMatchOrReject("{"))
                return new SyntaxToken(Token.Empty);

            var openToken = mPrevToken;
            var statements = new List<SyntaxExpr>();
            while (!sStatementsDone.Contains(mTokenName))
                ParseStatement(statements);

            if (AcceptMatchOrReject("}"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(openToken, statements.ToArray());
        }

        private void ParseStatement(List<SyntaxExpr> statements)
        {
            var keyword = mToken;
            switch (mToken)
            {
                case ";": SkipSemicolon(); break;

                case "{":
                    statements.Add(ParseStatements());
                    break;

                case "while":
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements()));
                    break;

                case "if":
                    Accept();
                    var ifCondition = ParseExpr();
                    var ifBody = ParseStatements();
                    if (mToken == "else")
                    {
                        // IF/WHILE (condition) (body) (else-body)
                        Accept();
                        statements.Add(new SyntaxMulti(keyword, ifCondition, ifBody, ParseStatements()));
                    }
                    else
                    {
                        // IF/WHILE (condition) (body)
                        statements.Add(new SyntaxBinary(keyword, ifCondition, ifBody));
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
                    statements.Add(new SyntaxMulti(keyword, forVariable, forCondition, ParseStatements()));
                    break;

                case "return":
                    Accept();
                    if (mTokenName == ";" || mTokenName == "}")
                        statements.Add(new SyntaxToken(keyword));
                    else
                        statements.Add(new SyntaxUnary(keyword, ParseExpr()));
                    SkipSemicolon();
                    break;

                case "throw":
                    Accept();
                    statements.Add(new SyntaxUnary(keyword, ParseExpr()));
                    SkipSemicolon();
                    break;

                case "continue":
                case "break":
                    statements.Add(new SyntaxToken(Accept()));
                    SkipSemicolon();
                    break;

                case "switch":
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements()));
                    break;

                case "default":
                    statements.Add(new SyntaxToken(Accept()));
                    AcceptMatchOrReject(":");
                    break;

                case "case":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    AcceptMatchOrReject(":");
                    break;

                default:
                    if (sRejectAnyStop.Contains(mTokenName))
                    {
                        RejectToken(mToken, "Unexpected token or reserved word");
                        Accept();
                        break;
                    }

                    var result = ParseExpr();
                    if (sAssignOperators.Contains(mToken))
                        result = new SyntaxBinary(Accept(), result, ParseExpr());
                    statements.Add(result);
                    SkipSemicolon();
                    break;
            }
        }

        ///  Parse expression (doesn't include ',' or '=' statements)
        SyntaxExpr ParseExpr()
        {
            return ParseLambda();
        }

        SyntaxExpr ParseLambda()
        {
            var result = ParseTernary();
            if (mTokenName == "=>")
            {
                var lambdaToken = Accept();
                if (mTokenName == "{")
                    result = new SyntaxBinary(lambdaToken, result, ParseStatements());
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
            if (InterceptAndReplaceToken(">", VIRTUAL_TOKEN_GE) || sComparisonOperators.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, ParseRange());
                if (InterceptAndReplaceToken(">", VIRTUAL_TOKEN_GE) || sComparisonOperators.Contains(mTokenName))
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
            while (InterceptAndReplaceToken(">", VIRTUAL_TOKEN_SHIFT_RIGHT) || sMultiplyOperators.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, ParseUnary());
                if (mTokenName == "<<" || InterceptAndReplaceToken(">", VIRTUAL_TOKEN_SHIFT_RIGHT))
                {
                    Reject("Shift operators are not associative, must use parentheses");
                    break;
                }
            }
            return result;
        }

        bool InterceptAndReplaceToken(string match, string replace)
        {
            if (mTokenName != match)
                return false;

            var peek = mLexerEnum.PeekOnLine();
            if (peek.X != mToken.X + mTokenName.Length || mTokenName + peek.Name != replace)
                return false;

            // Replace with a virtual token
            Accept();  // Skip match
            mToken = CreateInvisible(mToken, replace);
            mTokenName = mToken.Name;
            return true;
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
            if (mTokenName == NEWVAR)
            {
                mToken.Type = eTokenType.CreateVariable;
                return new SyntaxUnary(Accept(), new SyntaxToken(ParseIdentifier("Expecting variable name")));
            }

            if (mTokenName == "switch")
                return ParseSwitchExpression();

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
                    ParseParen(parameters, mTokenName == "(");
                    result = new SyntaxMulti(openToken, parameters.ToArray());
                }
                else if (AcceptMatch("."))
                {
                    // Experimental cast style: Identifier.(type)
                    if (mTokenName == "(" || AcceptMatch("to") || AcceptMatch("as") || AcceptMatch("cast") || AcceptMatch("is"))
                    {
                        var openToken = mToken;
                        if (AcceptMatchOrReject("(", "start of cast", false)
                            && ParseTypeName(out var typeName, sEmptyWordSet)
                            && AcceptMatchOrReject(")", "end of cast", false))
                        {
                            // Parse a cast: Use ')' to differentiate from function call which is '('
                            Connect(mPrevToken, openToken);
                            result = new SyntaxBinary(mPrevToken, typeName, result);
                            accepted = true;
                        }
                        else
                        {
                            return new SyntaxToken(openToken); // Parse error, doesn't matter
                        }
                    }
                    else
                    {
                        // Member access
                        accepted = true;
                        result = new SyntaxBinary(mPrevToken, result,
                            new SyntaxToken(ParseIdentifier("Expecting identifier")));
                    }
                }
                else if (mTokenName == "<" && mPrevToken.Type == eTokenType.Identifier)
                {
                    // Possibly a type argument list.  Let's try it and find out.
                    var p = SaveParsePoint();
                    var openTypeToken = mToken;
                    if (TryParseTypeArgumentList(out var typeArguments, null) && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Type argument list
                        accepted = true;
                        typeArguments.Insert(0, result);
                        result = new SyntaxMulti(CreateInvisible(openTypeToken, VIRTUAL_TOKEN_TYPE_ARG_LIST), typeArguments.ToArray());
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
                Reject("Unexpected end of file");
                return new SyntaxToken(mToken);
            }

            // Parse parentheses: expression, cast, or lambda parameters (not a function call)
            if (mTokenName == "(")
            {
                // Parse expressions
                var openToken = mToken;
                var expressions = new List<SyntaxExpr>();
                ParseParen(expressions, false);

                // Expression order parentheses are thrown away, lambda is kept
                SyntaxExpr result = expressions.Count == 1 ? expressions[0] 
                                    : new SyntaxMulti(CreateInvisible(mPrevToken, VIRTUAL_TOKEN_LAMBDA), expressions.ToArray());

                if (mToken.Type != eTokenType.Identifier && mTokenName != "(")
                    return result;

                mPrevToken.AddWarning("Try the new experimental casting style: 'Expression.(type)' or 'Expression.to(type)'. This style may be removed");
                // Parse a cast: The closing ')' is followed by '(' or identifier
                // Use ')' to differentiate from function call which is '('
                return new SyntaxBinary(mPrevToken, result, ParsePrimary());
            }

            // Number, string, identifier
            if (mToken.Type == eTokenType.Number
                || mToken.Type == eTokenType.Quote
                || mToken.Type == eTokenType.Identifier)
            {
                return new SyntaxToken(Accept());
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

            if (mToken.Type == eTokenType.Identifier && Peek() == ":")
            {
                // TBD: Use different syntax for named parameters?
                Accept();
                mToken.AddWarning("Named parameters not stored in the parse tree, and might not be supported in the future");
                Accept();
            }

            // Allow 'ref' or 'out' qualifier
            Token qualifier = null;
            if (sFuncCallParamQualifiers.Contains(mTokenName))
                qualifier = Accept();

            var expr = ParseExpr();
            if (qualifier != null)
                expr = new SyntaxUnary(qualifier, expr);
            return expr;
        }

        SyntaxExpr ParseInitializer()
        {
            // Read open brace, '{'
            var openToken = Accept();
            if (openToken != "{")
                throw new Exception("Compiler error: Expecting '{' while parsing initializer");

            // Parse expressions
            var parameters = new List<SyntaxExpr>();
            if (mTokenName != "}")
            {
                parameters.Add(mTokenName == "{" ? ParseInitializer() : ParseExpr());
                while (AcceptMatch(","))
                {
                    Connect(openToken, mPrevToken);
                    parameters.Add(mTokenName == "{" ? ParseInitializer() : ParseExpr());
                }
            }

            if (AcceptMatchOrReject("}", "or ','"))
                Connect(openToken, mPrevToken);
            if (parameters.Count == 0)
                RejectToken(openToken, "Initializer list may not be empty");

            return new SyntaxMulti(CreateInvisible(openToken, VIRTUAL_TOKEN_INITIALIZER), parameters.ToArray());
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
            var parameters = new List<SyntaxExpr>();
            if (mTokenName != "}")
            {
                parameters.Add(ParseExpr());
                while (AcceptMatch(","))
                {
                    Connect(openToken, mPrevToken);
                    parameters.Add(ParseExpr());
                }
            }

            // If not ended properly, reject this expression
            if (mTokenName != "}")
                Reject("Expecting '}' or ','");
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);

            return new SyntaxBinary(keyword, match, new SyntaxMulti(mPrevToken, parameters.ToArray()));
        }

        bool BeginsTypeDef(WordSet qualifiers, Token token)
        {
            return token.Type == eTokenType.Identifier
               || qualifiers.Contains(token)
               || token == "func" || token == "afunc"
               || token == PTR || token == "[";
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
            // Unary operators '*' and '[]', short for Pointer<type> and Array<type>
            // Treat qualifiers `in`, `out`, `ref`, `ro` similar to unary operators
            result = null;
            if (mToken == PTR || mToken == "[")
            {
                var token = Accept();
                if (token.Name == "[" && !AcceptMatch("]"))
                {
                    // Unaray array operator is always '[]', so swallow the ']'
                    if (errorStop != null)
                        Reject("Expecting ']'", errorStop);
                    return false;
                }
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
                    if (!TryParseTypeArgumentList(out var expr, errorStop))
                        return false;
                    expr.Insert(0, result);
                    result = new SyntaxMulti(CreateInvisible(openTypeToken, VIRTUAL_TOKEN_TYPE_ARG_LIST), expr.ToArray());
                }
            } while (accepted);
            return true;
        }

        /// <summary>
        /// Try parsing a type argument list. 
        /// Error causes reject until errorStop unless errorStop is null.
        bool TryParseTypeArgumentList(out List<SyntaxExpr> arguments, WordSet errorStop)
        {
            var openToken = Accept();
            if (openToken.Name != "<")
                throw new Exception("Compiler error: Expecting '<' while parsing type argument list");

            // Parse the first parameter
            arguments = new List<SyntaxExpr>();
            if (!ParseTypeName(out var p, errorStop))
                return false;
            arguments.Add(p);

            // Parse the rest of the parameters
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                if (!ParseTypeName(out p, errorStop))
                    return false;
                arguments.Add(p);
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
        
        void SkipSemicolon()
        {
            if (AcceptMatch(";"))
                return;
            if (mToken == "}")
                return;

            Reject("Expecting ';' or end of line");
            AcceptMatch(";");
        }

        /// <summary>
        /// Parse a qualified identifier.  
        /// Error causes reject until errorStop and returns null.
        /// </summary>
        SyntaxExpr TryParseQualifiedIdentifier(string errorMessage, WordSet errorStop = null)
        {
            // Parse first identifier
            if (!ParseIdentifier(errorMessage, out var t1, errorStop))
                return null;
            var identifier = new SyntaxToken(t1);
            if (mTokenName != ".")
                return identifier;

            // Parse the rest
            var dotToken = mToken;
            var tokens = new List<SyntaxExpr>();
            tokens.Add(identifier);
            while (AcceptMatch("."))
            {
                if (!ParseIdentifier(errorMessage, out var t2, errorStop))
                    return null;
                tokens.Add(new SyntaxToken(t2));
            }
            return new SyntaxMulti(dotToken, tokens.ToArray());
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

            // Read next token, and skip comments
            var firstComment = true;
            do
            {
                // Read next token (set EOF flag if no more tokens on line)
                if (mLexerEnum.MoveNext())
                    mToken = mLexerEnum.Current;
                else
                    mToken = new Token("", 0, 0, eTokenType.Reserved);

                // Keep track of public comments
                if (mToken.Type == eTokenType.PublicComment)
                {
                    mToken.Type = eTokenType.PublicComment;
                    if (firstComment)
                        mComments.Clear();
                    firstComment = false;
                    mComments.Add(mToken.Name);
                }

            } while (mToken.Type == eTokenType.Comment || mToken.Type == eTokenType.PublicComment);

            // Reset token info
            mToken.Clear();
            mTokenName = mToken.Name;
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
                mInsertedToken = mToken;
                mToken = CreateInvisible(prevToken, ";");
                mToken.Location = new TokenLoc(mToken.X+1, mToken.Y);
                mTokenName = ";";
            }
            return mPrevToken;
        }
      
        // Reject the given token
        public void RejectToken(Token token, string errorMessage)
        {
            mParseError = true;
            token.AddError(errorMessage);

            // If the error is after the end of file, put it on the last visible token
            if (token.Name == "")
                mPrevToken.AddError(errorMessage);

            // If the token is invisible, copy the connected visible token(s)
            if (token.Invisible)
            {
                var connected = token.GetInfo<Token[]>();
                if (connected != null)
                {
                    Token bestFit = null;
                    bool endExpression = token == ";" || token == VIRTUAL_TOKEN_LAMBDA;
                    foreach (var c in connected)
                    {
                        if (c.Invisible)
                            continue;
                        if (bestFit == null)
                            bestFit = c;
                        else if (endExpression && (c.Y > bestFit.Y || (c.Y == bestFit.Y && c.X > bestFit.X)))
                            bestFit = c; // End of expression
                        else if (!endExpression && (c.Y < bestFit.Y || (c.Y == bestFit.Y && c.X < bestFit.X)))
                            bestFit = c; // Beginning of expression
                    }
                    if (bestFit != null)
                    {
                        bestFit.Error = true;
                        bestFit.AddInfo(errorMessage);
                    }
                }
            }

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

        Token CreateInvisible(Token token, string text, eTokenType type = eTokenType.Normal)
        {
            var newToken = new Token(text, token.X, token.Y, type);
            newToken.Invisible = true;
            Connect(token, newToken);
            return newToken;
        }


    }

}
