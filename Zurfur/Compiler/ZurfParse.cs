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
        // These tokens are ambiguous, so get replaced while parsing
        public const string VIRTUAL_TOKEN_TYPE_ARG_LIST = "<>";
        const string VIRTUAL_TOKEN_SHIFT_RIGHT = ">>";
        const string VIRTUAL_TOKEN_GE = ">=";
        const string VIRTUAL_TOKEN_LAMBDA = "()";
        const string PTR = "^";

        ZurfParseCheck mZurfParseCheck;

        bool				mParseError;	// Flag is set whenever a parse error occurs
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken = new Token(";", -1, 0);
        Token               mPrevToken = new Token(";", -1, 0);
        List<string>        mComments = new List<string>();

        SyntaxUnit mUnit;

        // NOTE: >> and >= are omitted and handled at parser level.
        //       TBD: Need to handle >>= as well
        public const string TokenSymbols = "<< <= == != && || += -= *= /= %= &= |= ~= <<= => === :: ..";

        // Add semicolons to all lines, except for:
        static WordSet sEndLineSkipSemicolon = new WordSet("; { ( [ ,");
        static WordSet sBeginLineSkipSemicolon = new WordSet("; { + - * / % | & ~ || && == != = "
                                                    + ": ? . , > << <= < => .. :: === += -= *= /= %= &= |= ~= implements else");
        Token mInsertedSemicolon;


        static readonly string sReservedWordsList = "abstract as base break case catch class const "
            + "continue default delegate do else enum event explicit extern false defer use "
            + "finally fixed for goto if implicit in interface internal is extends lock namespace module include "
            + "new null operator out override params pub public private protected readonly ro ref "
            + "return sealed sealed1 sizeof stackalloc heapalloc static struct switch this throw true try "
            + "typeof unsafe using static virtual volatile while asm managed unmanaged implements "
            + "async await astart func afunc get set yield global partial var where nameof construct destruct cast";
        static readonly string sReservedControlWords = "using namespace module include class struct interface func "
            + "afunc prop get set construct destruct operator if else switch case await for";
        static WordMap<eTokenType> sReservedWords = new WordMap<eTokenType>();
        static WordSet sReservedIdentifierVariables = new WordSet("null this true");


        static WordMap<int> sFieldQualifiers = new WordMap<int>() { { "readonly", 1 }, { "ro", 1 } };
        static WordMap<int> sClassFuncFieldQualifiers = new WordMap<int>()
        {
            { "pub", 1 }, { "public", 1 }, { "protected", 1 }, { "private", 1 }, { "internal", 1 },
            { "unsafe", 2 }, { "extern", 3 }, { "static", 4 },  {"const", 4 },
            { "sealed", 6 }, { "sealed1", 6 },
            { "abstract", 8 }, { "virtual", 8},  { "override", 8 }, { "new", 8 },
            { "volatile", 12 }
        };

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sFieldDefTypeQualifiers = new WordSet("ref");
        static WordSet sFuncDefReturnTypeQualifiers = new WordSet("ref ro");
        static WordSet sFuncDefParamTypeQualifiers = new WordSet("out ref ro");
        static WordSet sTypeDefQualifiers = new WordSet("ref ro");
        static WordSet sTypeDefParamQualifiers = new WordSet("in out");
        static WordSet sFuncCallParamQualifiers = new WordSet("out ref");

        static WordSet sAllowConstraintKeywords = new WordSet("class struct unmanaged");

        public static WordSet sOverloadableOperators = new WordSet("+ - * /");
        static WordSet sComparisonOperators = new WordSet("== === != < <= > >="); // For '>=', use VIRTUAL_TOKEN_GE
        static WordSet sAddOperators = new WordSet("+ - | ~");
        static WordSet sMultiplyOperators = new WordSet("* / % & << >>"); // For '>>', use VIRTUAL_TOKEN_SHIFT_RIGHT
        static WordSet sAssignOperators = new WordSet("= += -= *= /= %= |= &= ~= <<= >>=");
        static WordSet sUnaryOperators = new WordSet("+ - ! ~ & @ " + PTR);

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
        static WordSet sRejectFuncParamsParen = new WordSet(")");
        static WordSet sRejectFuncParamsBracket = new WordSet("]");
        static WordSet sRejectFuncParam = new WordSet(", )");
        static WordSet sRejectParen = new WordSet(")");
        static WordSet sRejectBracket = new WordSet("]");

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
                ParseCompilationUnit();
                mZurfParseCheck.Check(mUnit);
                return mUnit;
            }

            try
            {
                ParseCompilationUnit();
            }
            catch (Exception ex1)
            {
                var errorMessage = "Parse error: " + ex1.Message + "\r\n\r\n" + ex1.StackTrace;
                do
                {
                    RejectToken(mToken, errorMessage);
                    Accept();
                } while (mToken != "");
            }

            try
            {
                mZurfParseCheck.Check(mUnit);
            }
            catch (Exception ex2)
            {
                var errorMessage = "Parse check error: " + ex2.Message + "\r\n\r\n" + ex2.StackTrace;
                RejectToken(mToken, errorMessage);
                var lexEnum = new Lexer.Enumerator(mLexer);
                lexEnum.MoveNext();
                RejectToken(lexEnum.Current, errorMessage);
            }

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
                // Read qualifiers
                qualifiers.Clear();
                var attributes = ParseAttributes();
                ParseQualifiers(qualifiers, sClassFuncFieldQualifiers);
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
                        mUnit.Namespaces.Add(ParseNamespaceStatement(mUnit, comments));
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'namespace' statement");
                        if (parentClass != null)
                            RejectToken(keyword, "Namespace statements must not be inside a class body");
                        SkipSemicolon();
                        break;

                    case "enum":
                        ParseClass(parentClass, qualifiers, comments);
                        break;

                    case "interface":
                        ParseClass(parentClass, qualifiers, comments);
                        break;

                    case "struct":
                        ParseClass(parentClass, qualifiers, comments);
                        break;

                    case "class":
                        ParseClass(parentClass, qualifiers, comments);
                        break;

                    case "operator":
                    case "construct":
                    case "func":
                    case "afunc":
                        mUnit.Funcs.Add(ParseMethod(parentClass, qualifiers, comments));
                        break;

                    case "this":
                        ParseIndexer(parentClass, qualifiers, comments);
                        break;

                    case "prop":
                        var propKeyword = Accept();
                        var propField = ParseField(parentClass, qualifiers, comments);
                        ParsePropertyBody(parentClass, qualifiers, comments, propKeyword, propField.Name, null, propField.TypeName);
                        break;

                    default:
                        if (sRejectAnyStop.Contains(mTokenName))
                        {
                            RejectToken(mToken, "Unexpected token or reserved word");
                            Accept();
                            break;
                        }

                        ParseQualifiers(qualifiers, sFieldQualifiers);
                        if (mToken.Type != eTokenType.Identifier)
                        {
                            RejectToken(mToken, "Expecting a field name identifier or a keyword: using, namespace, class, struct, interface, enum, func, or a field name identifier.");
                            Accept();
                            break;
                        }

                        // Fields must have the type name, enum must not have type name
                        // TBD: Move to ZurfParseCheck
                        bool isInEnum = parentClass != null && parentClass.Keyword.Name == "enum";
                        if (!isInEnum && mTokenName == "=")
                            RejectToken(mToken, "Expecting a type name");
                        if (isInEnum && mTokenName != "=")
                            RejectToken(mToken, "Expecting '=', enum field must not have a type name");

                        mUnit.Fields.Add(ParseField(parentClass, qualifiers, comments));
                        SkipSemicolon();
                        break;
                }
            }
        }

        void ParseQualifiers(List<Token> acceptedQualifiers, WordMap<int> qualifiers)
        {
            int sortOrder = 0;
            while (qualifiers.TryGetValue(mTokenName, out var newSortOrder))
            {
                // Verify no duplicates, TBD: Move to ZurfParseCheck
                foreach (var token in acceptedQualifiers)
                    if (token == mTokenName)
                        RejectToken(mToken, "Cannot have duplicate qualifiers");

                // Verify sort order, TBD: Move to ZurfParseCheck
                acceptedQualifiers.Add(mToken);
                if (newSortOrder == sortOrder)
                    RejectToken(mToken, "The '" + mToken + "' qualifier cannot be combined with '" + mPrevToken + "'");
                if (newSortOrder < sortOrder)
                    RejectToken(mToken, "The '" + mToken + "' qualifier must come before '" + mPrevToken + "'");
                sortOrder = newSortOrder;
                Accept();
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

        SyntaxNamespace ParseNamespaceStatement(SyntaxUnit unit, string []comments)
        {
            var nspace = new SyntaxNamespace();
            nspace.Comments = comments;
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
                if (!AcceptMatch("]"))
                    RejectToken(mToken, "Expecting ']', end of attributes");
            }
            return new SyntaxExprMulti(openToken, attributes.ToArray());
        }

        // Parse class, struct, interface, or enum
        void ParseClass(SyntaxClass parentClass, List<Token> qualifiers, string []comments)
        {
            var synClass = new SyntaxClass();
            synClass.Namespace = mUnit.CurrentNamespace;
            synClass.ParentClass = parentClass;
            synClass.Comments = comments;
            synClass.Keyword = Accept();
            var classIdentifier = mToken;

            while (sTypeDefQualifiers.ContainsKey(mTokenName))
                qualifiers.Add(Accept());
            synClass.Qualifiers = qualifiers.ToArray();

            // Parse class name and type parameters
            if (!ParseIdentifier("Expecting a type name", out synClass.Name))
                return;
            synClass.TypeParams = ParseTypeParams(sRejectFuncName);

            // Parse base type
            if (AcceptMatch("extends"))
            {
                synClass.BaseClass = ParseTypeDef(sEmptyWordSet, sEmptyWordSet);
            }

            // Parse implements
            if (AcceptMatch("implements"))
            {
                var imp = new List<SyntaxExpr>();
                do
                {
                    imp.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
                } while (AcceptMatch(","));
                synClass.Implements = imp.ToArray();
            }

            // Parse constraints
            if (mTokenName == "where")
            {
                List<SyntaxConstraint> constraints = new List<SyntaxConstraint>();
                while (mTokenName == "where")
                    constraints.Add(ParseConstraint());
                synClass.Constraints = constraints.ToArray();
            }
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

        SyntaxTypeParam []ParseTypeParams(WordSet errorStop)
        {
            if (mToken != "<")
                return Array.Empty<SyntaxTypeParam>();

            var typeParams = new List<SyntaxTypeParam>();
            var openToken = Accept();
            do
            {
                // Parse in or out qualifier
                var param = new SyntaxTypeParam();
                if (sTypeDefParamQualifiers.Contains(mTokenName))
                    param.Qualifier = Accept();
                if (!ParseIdentifier("Expecting a type name", out param.Name))
                    break;
                typeParams.Add(param);
                if (mTokenName == ",")
                    Connect(openToken, mToken);
            } while (AcceptMatch(","));

            if (AcceptMatch(">"))
                Connect(mPrevToken, openToken);
            else
                Reject("Expecing end of type parameters: '>'", errorStop);
            return typeParams.ToArray();
        }

        SyntaxConstraint ParseConstraint()
        {
            var constraint = new SyntaxConstraint();
            constraint.Keyword = Accept();
            if (!ParseIdentifier("Expecting a type name", out constraint.GenericTypeName))
                return constraint;
            if (!AcceptMatch("is"))
            {
                RejectToken(mToken, "Expecting 'is'");
                return constraint;
            }
            var constraintTypeNames = new List<SyntaxExpr>();
            do
            {
                if (sAllowConstraintKeywords.Contains(mToken))
                {
                    mToken.Type = eTokenType.Reserved;
                    constraintTypeNames.Add(new SyntaxExprToken(Accept()));
                    continue;
                }
                constraintTypeNames.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
            } while (AcceptMatch(","));
            constraint.TypeNames = constraintTypeNames.ToArray();
            return constraint;                       
        }

        SyntaxField ParseField(SyntaxClass parentClass, List<Token> qualifiers, string []comments)
        {        
            var field = new SyntaxField();
            field.Namespace = mUnit.CurrentNamespace;
            field.ParentClass = parentClass;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = comments;
            field.Name = ParseIdentifier("Expecting field or property name");

            // Parse type name
            field.TypeName = ParseTypeDef(sFieldDefTypeQualifiers, sEmptyWordSet);

            // Optionally initialize
            var initToken = mToken;
            if (AcceptMatch("="))
            {
                // Initialize via assignment
                field.InitToken = initToken;
                field.InitExpr = ParseExpr();
            }
            else if (mToken == "(")
            {
                // Initialize via constructor
                field.InitToken = initToken;
                var parameters = new List<SyntaxExpr>();
                ParseParen(parameters, true);
                field.InitExpr = new SyntaxExprMulti(initToken, parameters.ToArray());
            }
            return field;
        }

        void ParseIndexer(SyntaxClass parentClass, List<Token> qualifiers, string[] comments)
        {
            var keyword = Accept();
            keyword.Type = eTokenType.ReservedControl;

            if (mTokenName != "[")
                Reject("Expecting '[' after indexer keyword", sRejectIndexerParams);

            SyntaxFuncParam []parameters = null;
            if (mTokenName == "[")
                parameters = ParseFuncParamsDef();

            var returnType = ParseTypeDef(sFuncDefReturnTypeQualifiers, sRejectFuncParam);

            ParsePropertyBody(parentClass, qualifiers, comments, keyword, keyword, parameters, returnType);
        }

        void ParsePropertyBody(SyntaxClass parentClass, List<Token> qualifiers, string[] comments, Token keyword, Token name, SyntaxFuncParam []parameters, SyntaxExpr returnType)
        {
            var synFunc = new SyntaxFunc();
            synFunc.Namespace = mUnit.CurrentNamespace;
            synFunc.ParentClass = parentClass;
            synFunc.Comments = comments;
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = keyword;
            synFunc.Name = name;
            synFunc.Params = parameters;
            synFunc.ReturnType = returnType;

            if (mTokenName == "=>")
            {
                Accept();
                synFunc.Statements = new SyntaxExprUnary(synFunc.Keyword, ParseExpr());
                mUnit.Funcs.Add(synFunc);
                return;
            }
            if (!AcceptMatch("{"))
            {
                Reject("Expecting '{' or '=>' to define property body");
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
            if (AcceptMatch("}"))
            {
                Connect(openBrace, mPrevToken);
            }
            else
            {
                RejectToken(mToken, "Expecting '}' end of property");
            }
        }

        /// <summary>
        /// Func, construct, operator
        /// </summary>
        SyntaxFunc ParseMethod(SyntaxClass parentClass, List<Token> qualifiers, string []comments)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc();
            synFunc.Namespace = mUnit.CurrentNamespace;
            synFunc.ParentClass = parentClass;
            synFunc.Comments = comments;
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = Accept();

            switch (synFunc.Keyword)
            {
                case "afunc":
                case "func":
                    ParseFuncNameDef(out synFunc.ClassName, out synFunc.Name);
                    ParseFuncDef(out synFunc.TypeParams, out synFunc.Params, out synFunc.ReturnType);
                    break;
                case "construct": ParseConstructDef(synFunc); break;
                case "operator": ParseOperatorDef(synFunc); break;
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
                case "{":
                    return ParseStatements();
                case "=>":
                    return new SyntaxExprUnary(Accept(), ParseExpr());
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

        private void ParseFuncDef(out SyntaxTypeParam []typeParams, out SyntaxFuncParam []parameters, out SyntaxExpr returnType)
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

        private void ParseConstructDef(SyntaxFunc synFunc)
        {
            synFunc.Name = mToken;
            if (mToken != "(")
                Reject("Expecting '(' after 'construct' token", sRejectFuncName);

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

        SyntaxFuncParam []ParseFuncParamsDef()
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '[' or '(' while parsing function parameters");

            // Parse parameters
            var closeToken = openToken.Name == "(" ? ")" : "]";
            var parameters = new List<SyntaxFuncParam>();
            if (mTokenName != closeToken)
                parameters.Add(ParseFuncParamDef());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseFuncParamDef());
            }

            if (mTokenName != closeToken)
                Reject("Expecting '" + closeToken + "'", closeToken == ")" ? sRejectFuncParamsParen : sRejectFuncParamsBracket);
            if (AcceptMatch(closeToken))
                Connect(openToken, mPrevToken);
            return parameters.ToArray();
        }

        SyntaxFuncParam ParseFuncParamDef()
        {
            var synParam = new SyntaxFuncParam();
            if (!ParseIdentifier("Expecting a variable name", out synParam.Name, sRejectFuncParam))
                return synParam;
            synParam.TypeName = ParseTypeDef(sFuncDefParamTypeQualifiers, sRejectFuncParam);
            return synParam;
        }

        private SyntaxExpr ParseStatements()
        {
            // Read open token, '{'
            if (!AcceptMatch("{"))
            {
                RejectToken(mToken, "Expecting '{'");
                return new SyntaxExprToken(mToken);
            }
            var openToken = mPrevToken;
            var statements = new List<SyntaxExpr>();
            while (!sStatementsDone.Contains(mTokenName))
            {
                ParseStatement(statements);
            }
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);
            else
                RejectToken(mToken, "Expecting '}'");

            return new SyntaxExprMulti(openToken, statements.ToArray());
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
                case "if":
                    Accept();
                    var ifCondition = ParseExpr();
                    var ifBody = ParseStatements();
                    if (mToken != "else" || keyword != "if")
                    {
                        // IF/WHILE (condition) (body)
                        statements.Add(new SyntaxExprBinary(keyword, ifCondition, ifBody));
                    }
                    else
                    {
                        // IF/WHILE (condition) (body) (else-body)
                        Accept();
                        statements.Add(new SyntaxExprMulti(keyword, ifCondition, ifBody, ParseStatements()));
                    }
                    break;

                case "else":
                    Reject("Else must follow 'if' statement");
                    Accept();
                    break;

                case "for":
                    Accept();
                    var forVariable = ParseExpr();
                    if (!AcceptMatch("in"))
                        Reject("Expecting 'in' keyword", sRejectForCondition);
                    if (AcceptMatch("in"))
                        break;
                    var forCondition = ParseExpr();
                    statements.Add(new SyntaxExprMulti(keyword, forVariable, forCondition, ParseStatements()));
                    break;

                case "return":
                    Accept();
                    if (mTokenName == ";" || mTokenName == "}")
                        statements.Add(new SyntaxExprToken(keyword));
                    else
                        statements.Add(new SyntaxExprUnary(keyword, ParseExpr()));
                    SkipSemicolon();
                    break;

                case "throw":
                    Accept();
                    statements.Add(new SyntaxExprUnary(keyword, ParseExpr()));
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
                        result = new SyntaxExprBinary(Accept(), result, ParseExpr());
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
                    result = new SyntaxExprBinary(lambdaToken, result, ParseStatements());
                else
                    result = new SyntaxExprBinary(lambdaToken, result, ParseTernary());
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
                result = new SyntaxExprMulti(operatorToken, result, firstConditional, ParseConditionalOr());
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
            if (InterceptAndReplaceToken(">", VIRTUAL_TOKEN_GE) || sComparisonOperators.Contains(mTokenName))
            {
                result = new SyntaxExprBinary(Accept(), result, ParseRange());
                if (InterceptAndReplaceToken(">", VIRTUAL_TOKEN_GE) || sComparisonOperators.Contains(mTokenName))
                    Reject("Compare operators are not associative, must use parentheses");
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
                    Reject("Range operator is not associative, must use parentheses");
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
            while (InterceptAndReplaceToken(">", VIRTUAL_TOKEN_SHIFT_RIGHT) || sMultiplyOperators.Contains(mTokenName))
            {
                result = new SyntaxExprBinary(Accept(), result, ParseExpon());
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
            if (sUnaryOperators.Contains(mTokenName))
            {
                if (mTokenName == "@")
                    mToken.Type = eTokenType.CreateVariable;
                return new SyntaxExprUnary(Accept(), ParseUnary());
            }
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
                    ParseParen(parameters, mTokenName == "(");
                    result = new SyntaxExprMulti(openToken, parameters.ToArray());
                }
                else if (AcceptMatch("."))
                {
                    // Member access
                    accepted = true;
                    result = new SyntaxExprBinary(mPrevToken, result,
                        new SyntaxExprToken(ParseIdentifier("Expecting identifier")));
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
                        result = new SyntaxExprMulti(CreateInvisible(openTypeToken, VIRTUAL_TOKEN_TYPE_ARG_LIST), typeArguments.ToArray());
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
                return new SyntaxExprToken(mToken);
            }

            // Parse parentheses: expression, cast, or lambda parameters (not a function call)
            if (mTokenName == "(")
            {
                // Parse expressions
                var openToken = mToken;
                var expressions = new List<SyntaxExpr>();
                ParseParen(expressions, false);

                if (expressions.Count != 1 && mTokenName != "=>")
                {
                    RejectToken(mPrevToken, expressions.Count == 0
                        ? "Empty expression not allowed unless followed by lambda '=>' token"
                        : "Multi-parameter expression not allowed unless followed by lambda '=>' token");
                    foreach (var e in expressions)
                        Grayout(e);
                }


                SyntaxExpr result = expressions.Count == 1 ? expressions[0] 
                                    : new SyntaxExprMulti(CreateInvisible(mPrevToken, VIRTUAL_TOKEN_LAMBDA), expressions.ToArray());
                if (mToken.Type != eTokenType.Identifier && mTokenName != "(")
                    return result;

                // Parse a cast: The closing ')' is followed by '(' or identifier
                // Use ')' to differentiate from function call which is '('
                // TBD: Remove cast syntax, require keyword?
                if (!VerifyCastExpression(result))
                {
                    RejectToken(openToken, "Cast has an illegal character in it.");
                    RejectToken(mPrevToken, "Cast has an illegal character in it.");
                    Grayout(result);
                }
                return new SyntaxExprBinary(mPrevToken, result, ParsePrimary());
            }

            // Parse number or string
            if (char.IsDigit(mTokenName, 0) || mTokenName[0] == '"')
                return new SyntaxExprToken(Accept());

            // Parse variable name
            if (char.IsLetter(mTokenName, 0))
            {
                if (sReservedIdentifierVariables.Contains(mTokenName))
                    return new SyntaxExprToken(Accept());
                return new SyntaxExprToken(ParseIdentifier("Expecting an identifier"));
            }
            var errorToken = mToken;
            Reject("Expecting an identifier, number, string literal, or parentheses");
            return new SyntaxExprToken(errorToken);
        }

        bool VerifyCastExpression(SyntaxExpr expr)
        {
            bool ok = true;
            if (expr.Token.Type != eTokenType.Identifier
                && expr.Token != VIRTUAL_TOKEN_TYPE_ARG_LIST
                && expr.Token != PTR
                && expr.Token != "]"
                && expr.Token != ".")
            {
                var message = "Cast expression must be a type name, and may not contain '" + expr.Token + "'";
                if (expr.Token == "<")
                    message += ".  NOTE: '<' cannot be interpreted as a type argument list in this context";
                RejectToken(expr.Token, message);
                ok = false;
            }
            foreach (var e in expr)
                if (!VerifyCastExpression(e))
                    ok = false;
            return ok;
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

        /// <summary>
        /// Read the open '(' or '[' and then parse the parameters into parameters
        /// </summary>
        void ParseParen(List<SyntaxExpr> parameters, bool isFunc)
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
            parameters.Add(isFunc ? ParseFuncCallParameter() : ParseExpr());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(isFunc ? ParseFuncCallParameter() : ParseExpr());
            }

            // If not ended properly, reject this expression
            if (mTokenName != expectedToken)
                Reject("Expecting '" + expectedToken + "' or ','",
                    openToken == "(" ? sRejectParen : sRejectBracket);

            if (AcceptMatch(expectedToken))
                Connect(openToken, mPrevToken);
        }

        SyntaxExpr ParseFuncCallParameter()
        {
            if (mTokenName == "{")
                return ParseInitializer();

            // Allow 'ref' or 'out' qualifier
            Token qualifier = null;
            if (sFuncCallParamQualifiers.Contains(mTokenName))
                qualifier = Accept();

            var expr = ParseExpr();
            if (qualifier != null)
                expr = new SyntaxExprUnary(qualifier, expr);
            if (AcceptMatch(":"))
            {
                // Parse named parameter
                var colonToken = mPrevToken;
                if (expr.Count != 0 || expr.Token.Type != eTokenType.Identifier)
                    RejectToken(colonToken, "Expecting one identifier before the named parameter");
                if (qualifier != null)
                    RejectToken(qualifier, "Qualifier cannot come before a named parameter");

                // Allow 'ref' or 'out' qualifier
                if (sFuncCallParamQualifiers.Contains(mTokenName))
                    qualifier = Accept();

                var exprRight = ParseExpr();
                if (qualifier != null)
                    exprRight = new SyntaxExprUnary(qualifier, exprRight);
                expr = new SyntaxExprBinary(colonToken, expr, exprRight);
            }
            return expr;
        }

        SyntaxExpr ParseInitializer()
        {
            if (!AcceptMatch("{"))
                return null;

            var openBrace = mPrevToken;
            openBrace.AddWarning("Initializers are not yet stored in the parse tree");
            while (mTokenName != "}" && mTokenName != "")
                Accept();

            if (AcceptMatch("}"))
                Connect(openBrace, mPrevToken);
            return new SyntaxExprToken(openBrace);
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
                return new SyntaxExprUnary(Accept(), ParseTypeDef(qualifiers, errorStop));
            }
            else if (mTokenName == "func" || mTokenName == "afunc")
            {
                return ParseLambdaDef(); // TBD: Store in arse tree
            }
            else
            {
                if (!ParseTypeName(out var result, errorStop))
                    return new SyntaxExprToken(mToken);
                return result;
            }
        }

        SyntaxExpr ParseLambdaDef()
        {
            var keyword = Accept();
            keyword.Type = eTokenType.Reserved;
            keyword.AddWarning("Lambda definitions are not yet stored in the parse tree. ");
            ParseFuncDef(out var typeParams, out var parameters, out var returnType);
            return new SyntaxExprToken(mToken);
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
                result = new SyntaxExprUnary(token, expr);
                return true;
            }

            if (!ParseIdentifier("Expecting a type name", out var typeName, errorStop))
                return false;

            result = new SyntaxExprToken(typeName);

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
                    result = new SyntaxExprBinary(dotToken, result, new SyntaxExprToken(dotTypeName));
                }

                if (mToken == "<")
                {
                    accepted = true;
                    var openTypeToken = mToken;
                    if (!TryParseTypeArgumentList(out var expr, errorStop))
                        return false;
                    expr.Insert(0, result);
                    result = new SyntaxExprMulti(CreateInvisible(openTypeToken, VIRTUAL_TOKEN_TYPE_ARG_LIST), expr.ToArray());
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
            var identifier = new SyntaxExprToken(t1);
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
                tokens.Add(new SyntaxExprToken(t2));
            }
            return new SyntaxExprMulti(dotToken, tokens.ToArray());
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

        struct ParsePoint
        {
            public Lexer.Enumerator LexerEnum;
            public Token PrevToken;
            public Token Token;
            public bool ParseError;
        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.LexerEnum = mLexerEnum;
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            p.ParseError = mParseError;
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mLexerEnum = p.LexerEnum;
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mTokenName = mToken.Name;
            mParseError = p.ParseError;
        }

        // Accept the current token and advance to the next, skipping all comments.
        // The new token is saved in mToken and the token name is saved in mTokenName.
        // Returns the token that was accepted.  Token is pre-maked with token type
        Token Accept()
        {
            var prevToken = mToken;
            mPrevToken = prevToken;

            // Already at end of file?
            if (mTokenName == "")
                return mPrevToken;

            if (mInsertedSemicolon != null)
            {
                mToken = mInsertedSemicolon;
                mTokenName = mToken.Name;
                mInsertedSemicolon = null;
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

            // Insert a ';' after each new line
            if (prevToken.Y != mToken.Y
                && !sEndLineSkipSemicolon.Contains(prevToken)
                && !sBeginLineSkipSemicolon.Contains(mTokenName))
            {
                mInsertedSemicolon = mToken;
                mToken = CreateInvisible(prevToken, ";");
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

            // If the token is invisible, copy the error to the largest visible token
            // TBD: For invisible ';', the error should be moved to after the token
            if (token.Invisible)
            {
                var connected = token.GetInfo<Token[]>();
                if (connected != null)
                {
                    Token lastVisible = null;
                    foreach (var c in connected)
                    {
                        if (c.Invisible)
                            continue;
                        if (lastVisible == null)
                            lastVisible = c;
                        else if (c.Y > lastVisible.Y || (c.Y == lastVisible.Y && c.X > lastVisible.X))
                            lastVisible = c;
                    }
                    if (lastVisible != null)
                    {
                        lastVisible.Error = true;
                        lastVisible.AddInfo(errorMessage);
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
            var newToken = new Token(text, token.Y, token.X, type);
            newToken.Invisible = true;
            Connect(token, newToken);
            return newToken;
        }


    }

}
