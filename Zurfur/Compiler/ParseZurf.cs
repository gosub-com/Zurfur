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
        public const string VT_TYPE_ARG_LIST = "<>"; // Differentiate from '<'
        public const string PTR = "*";
        public const string REFERENCE = "^";
        public const string XOR = "~";

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
        public const string MULTI_CHAR_TOKENS = "<< <= == != && || += -= *= /= %= &= |= " + XOR + "= <<= => -> !== === :: .. ... ++ -- ";

        // Add semicolons to all lines, except for:
        static WordSet sEndLineSkipSemicolon = new WordSet("; { [ ( ,");
        static WordSet sBeginLineSkipSemicolon = new WordSet("{ } ] ) + - / % | & " + XOR + " || && == != = "
                                                + ": ? . , > << <= < => -> .. :: !== === += -= *= /= %= &= |= "
                                                + XOR + "= "
                                                // TBD: Should probably eat virtual semicolons in the parser
                                                + "where is in as extends implements implement impl");
        Token mInsertedToken;

        static readonly string sReservedWordsList = "abstract as base break case catch class const "
            + "continue default delegate do else enum event explicit extern false defer use "
            + "finally fixed for goto if implicit in interface internal is lock namespace module include "
            + "new null operator out override pub public private protected readonly ro ref mut "
            + "return unsealed unseal sealed sizeof stackalloc heapalloc static struct switch this throw true try "
            + "typeof unsafe using static virtual while dowhile asm managed unmanaged "
            + "async await astart func afunc get set yield global partial var where nameof "
            + "box boxed init dispose "
            + "trait extends implements implement impl union type fun afun def yield let cast "
            + "any dyn loop match event from to of on cofun cofunc global local val it throws atask "
            + "scope ";

        static readonly string sReservedControlWords = "using namespace module include class struct enum interface "
            + "func afunc fun afun prop get set if else switch await for while dowhile scope _";
        static WordMap<eTokenType> sReservedWords = new WordMap<eTokenType>();
        static WordSet sReservedIdentifierVariables = new WordSet("null this true false default base cast");

        static WordSet sClassFieldQualifiers = new WordSet("pub public protected private internal unsafe "
            + "static unsealed abstract virtual override new ro mut const");

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sFieldDefTypeQualifiers = new WordSet("ref");
        static WordSet sFuncDefReturnQualifiers = new WordSet("ro ref");
        static WordSet sFuncDefParamQualifiers = new WordSet("out ref mut"); // define function: fun f(a mut int)
        static WordSet sFuncCallParamQualifiers = new WordSet("out ref"); // call function: f(a ref int)
        static WordSet sTypeDefParamQualifiers = new WordSet("in out"); // type parameter IEnumerator<out>

        static WordSet sAllowConstraintKeywords = new WordSet("class struct unmanaged");
        static WordSet sAutoImplementMethod = new WordSet("default impl extern");

        public static WordSet sOverloadableOperators = new WordSet("+ - * / % [ in implicit cast");
        static WordSet sComparisonOperators = new WordSet("== != < <= > >= === !== in"); // For '>=', use VIRTUAL_TOKEN_GE
        static WordSet sAddOperators = new WordSet("+ - | " + XOR);
        static WordSet sMultiplyOperators = new WordSet("* / % & << >>"); // For '>>', use VIRTUAL_TOKEN_SHIFT_RIGHT
        static WordSet sAssignOperators = new WordSet("= += -= *= /= %= |= &= " + XOR + "= <<= >>=");
        static WordSet sUnaryOperators = new WordSet("- ! & use " + XOR + " " + PTR);

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // This seems stange because something like `a = F<T1,T2>;` is not a valid expression
        // The following symbols allow us to call functions, create types, access static members, and cast
        // For example `F<T1>()` to call a function or constructor, `F<T1>.Name` to access a static or member,
        // and #F<T1>(expression) to cast.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) .");

        static WordSet sStatementEndings = new WordSet("; => }");
        static WordSet sStatementsDone = new WordSet("} func fun namespace class struct interface enum", true);
        static WordSet sRejectAnyStop = new WordSet("=> ; { } namespace class struct interface enum if else for while throw switch case func fun prop get set", true);
        static WordSet sRejectForCondition = new WordSet("in");
        static WordSet sRejectFuncName = new WordSet("(");
        static WordSet sRejectFuncParam = new WordSet(", )");
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
            bool topScope = parentScope == null;
            var qualifiers = new List<Token>();
            while (mTokenName != "" && (mTokenName != "}" || topScope))
            {
                while (mTokenName == "[")
                {
                    ParseInitializer(); // TBD: Store in parse tree!
                    if (mTokenName == ";")
                        Accept();
                }

                // Read qualifiers
                qualifiers.Clear();
                while (sClassFieldQualifiers.Contains(mTokenName))
                {
                    qualifiers.Add(Accept());
                }

                var keyword = mToken;
                switch (mTokenName)
                {
                    case ";":
                        Accept();
                        RejectQualifiers(qualifiers, "Expecting a statement after qualifier");
                        break;

                    case "{":
                    case "=>":
                        Accept();
                        RejectQualifiers(qualifiers, "Unexpected qualifiers");
                        RejectToken(keyword, "Unexpected '" + keyword + "'.  Expecting a keyword, such as 'class', 'fun', etc. before the start of a new scope.");
                        break;

                    case "use":
                        Accept();
                        mUnit.Using.Add(ParseUsingStatement(keyword));
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'using' statement");
                        if (parentScope != null)
                            RejectToken(keyword, "Using statements must come before the namespace");
                        break;

                    case "namespace":
                        Accept();
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'namespace' statement");
                        if (parentScope != null && parentScope.IsType)
                            RejectToken(keyword, "Namespace statements must not be inside a class/enum/interface body");
                        var newNamespace = ParseNamespaceStatement(keyword, parentScope, mUnit);
                        if (parentScope == null && newNamespace != null)
                            parentScope = newNamespace;
                        break;

                    case "interface":
                    case "enum":
                    case "struct":
                    case "class":
                        Accept();
                        while (AcceptMatch("ref") || AcceptMatch("ro"))
                            qualifiers.Add(mPrevToken);
                        ParseClass(keyword, parentScope, qualifiers);
                        break;

                    case "fun":
                    case "afun":
                    //case "func":
                    //case "afunc":
                        Accept();
                        keyword.Type = eTokenType.ReservedControl;  // Fix keyword to make it control
                        ParseMethod(keyword, parentScope, qualifiers);
                        break;

                    case "prop":
                        Accept();
                        ParseProperty(keyword, parentScope, qualifiers);
                        break;

                    default:
                        if (keyword.Type != eTokenType.Identifier)
                        {
                            Accept();
                            RejectToken(keyword, "Expecting an identifier or keyword, qualifier ('pub', etc.), or reserved word such as 'class', 'enum', 'fun', etc.");
                            break;
                        }
                        if (parentScope != null && parentScope.Keyword == "enum")
                        {
                            // Enum field
                            Accept();
                            var enumFieldName = ParseEnumField(parentScope, qualifiers, keyword);
                            if (!mToken.Error)
                                mUnit.Fields.Add(enumFieldName);
                        }
                        else
                        {
                            // Class, struct, interface, or gloabal
                            var classFieldName = ParseField(parentScope, qualifiers);
                            if (!mToken.Error)
                                mUnit.Fields.Add(classFieldName);
                        }
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

        SyntaxUsing ParseUsingStatement(Token keyword)
        {
            var synUsing = new SyntaxUsing();
            synUsing.Keyword = keyword;
            var tokens = new List<Token>();
            ParseQualifiedIdentifier(tokens, "Expecting a namespace identifier");
            synUsing.NamePath = tokens.ToArray();
            return synUsing;
        }

        SyntaxNamespace ParseNamespaceStatement(Token keyword, SyntaxScope parentScope, SyntaxUnit unit)
        {
            SyntaxNamespace newNs = null;
            SyntaxScope parentNs = parentScope;
            do
            {
                if (!ParseIdentifier("Expecting a namespace identifier", out var t1))
                    return newNs;

                newNs = new SyntaxNamespace();
                newNs.ParentScope = parentNs;
                newNs.Name = t1;
                newNs.Keyword = keyword;
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
        void ParseClass(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            var synClass = new SyntaxType();
            synClass.Qualifiers = qualifiers.ToArray();
            synClass.ParentScope = parentScope;
            synClass.Comments = mComments.ToString();
            synClass.Keyword = keyword;

            // Parse class name and type parameters
            if (!ParseIdentifier("Expecting a type name", out synClass.Name))
                return;
            synClass.Name.Type = eTokenType.TypeName;
            synClass.TypeParams = ParseTypeParams();
            mUnit.Types.Add(synClass);

            // Parse extends classes
            if (AcceptMatch("extends"))
                synClass.Extends = ParseTypeDef(sEmptyWordSet, sEmptyWordSet);

            // Parse implemented classes
            if (AcceptMatch("implements"))
            {
                var baseClasses = NewExprList();
                baseClasses.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
                while (AcceptMatch(","))
                {
                    baseClasses.Add(ParseTypeDef(sEmptyWordSet, sEmptyWordSet));
                }
                synClass.Implements = FreeExprList(baseClasses);
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

        SyntaxExpr ParseTypeParams()
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
                name.Type = eTokenType.TypeName;
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
            constraint.GenericTypeName.Type = eTokenType.TypeName;

            if (!AcceptMatchOrReject("is", "while parsing constraint"))
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
        SyntaxField ParseField(SyntaxScope parentScope, List<Token> qualifiers)
        {
            var field = new SyntaxField();
            field.ParentScope = parentScope;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();

            field.Name = mToken;
            mLastField = field; // Allow us to pick up comments on this line

            // Parse type name
            field.TypeName = ParseNewVarStatment(sFieldDefTypeQualifiers);

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
            name.Type = eTokenType.DefineField;
            mLastField = field; // Allow us to pick up comments on this line

            // Optionally initialize
            if (AcceptMatch("="))
            {
                // Initialize via assignment
                field.TypeName = ParseExpr();
            }
            return field;
        }

        private void ParseProperty(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            var propKeyword = keyword;
            var propField = ParseField(parentScope, qualifiers);
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
                synFunc.Statements = ParseMethodStatements("property", StatementsType.PropertyBody);
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
                        synFunc.Statements = ParseMethodStatements("property", StatementsType.MethodBody);
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
        void ParseMethod(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc();
            synFunc.ParentScope = parentScope;
            synFunc.Comments = mComments.ToString();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = keyword;

            switch (synFunc.Keyword)
            {
                case "fun":
                case "afun":
                //case "func":
                //case "afunc":
                    if (ParseFuncNameDef(out synFunc.ClassName, out synFunc.Name))
                    {
                        ParseFuncDef(out synFunc.TypeParams, out synFunc.Params, out synFunc.ReturnType);
                        synFunc.Constraints = ParseConstraints();
                    }
                    break;
                default:
                    throw new Exception("Error parsing function def");
            }

            if (synFunc.Name == "operator[")
            {
                ParsePropertyBody(parentScope, qualifiers, synFunc.Keyword, synFunc.Name, synFunc.Params, synFunc.ReturnType);
            }
            else
            {
                synFunc.Statements = ParseMethodStatements("method body", StatementsType.MethodBody);
                if (synFunc.Name != null)
                    mUnit.Funcs.Add(synFunc);
            }
        }

        bool ParseFuncNameDef(out SyntaxExpr className, out Token funcName)
        {
            // Try parsing a class name first (Optional, must be followed by "::")
            var pp = SaveParsePoint();
            className = null;
            if (mToken.Type == eTokenType.Identifier)
            {
                if (!ParseTypeName(out className, null)
                        || !AcceptMatch("::")) 
                {
                    className = null;
                    RestoreParsePoint(pp);
                }
            }

            // Parse operator
            if (className == null && AcceptMatch("operator"))
            {
                var operatorKeyword = mPrevToken;
                if (sOverloadableOperators.Contains(mTokenName))
                {
                    var operatorName = Accept();
                    if (operatorName.Name == "[")
                        AcceptMatchOrReject("]");
                    funcName = NewVirtualToken(operatorKeyword, operatorKeyword + operatorName);
                    return true;
                }
                else
                {
                    Reject("Expecting an overloadable operator", sRejectFuncName);
                    funcName = operatorKeyword;
                    return false;
                }

            }

            if (AcceptMatch("new") || AcceptMatch("init") || AcceptMatch("dispose") || AcceptMatch("default"))
            {
                funcName = mPrevToken;
                return true;
            }

            if (ParseIdentifier("Expecting a function", out funcName))
            {
                funcName.Type = eTokenType.DefineMethod;
                return true;
            }
            return false;
        }

        private void ParseFuncDef(out SyntaxExpr typeParams, out SyntaxExpr parameters, out SyntaxExpr returnType)
        {
            typeParams = ParseTypeParams();
            parameters = ParseFuncParamsDef();
            returnType = null;
            if (BeginsTypeDef(sFuncDefReturnQualifiers, mToken))
                returnType = ParseTypeDef(sFuncDefReturnQualifiers, sRejectFuncParam);
        }

        bool BeginsTypeDef(WordSet qualifiers, Token token)
        {
            // TBD: White or black list?
            //return token != "," && token != ";" && token != ")" && token != "]" 
            //        && token != "}" && token != "=>" && token != "{";
            return token.Type == eTokenType.Identifier
               || qualifiers.Contains(token)
               || token == "fun" || token == "afun" 
               //|| token == "func" || token == "afunc"
               || token == PTR || token == "?" || token == REFERENCE;
        }

        SyntaxExpr ParseFuncParamsDef()
        {
            // Read open token, '('
            if (!AcceptMatchOrReject("("))
                return new SyntaxError();

            // Parse parameters
            var openToken = mPrevToken;
            var parameters = NewExprList();
            if (mTokenName != ")")
                parameters.Add(ParseFuncParamDef(true));
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseFuncParamDef(false));
            }

            // Ellipse to signify repeated parameters
            if (AcceptMatch("..."))
                mPrevToken.AddWarning("Repeated parameters not supported yet");

            if (AcceptMatchOrReject(")"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(Token.Empty, FreeExprList(parameters));
        }

        SyntaxExpr ParseFuncParamDef(bool firstParam)
        {
            if (!ParseIdentifier("Expecting a variable name", out var name, sRejectFuncParam))
                return new SyntaxError();
            name.Type = eTokenType.DefineParam;
            var type = ParseTypeDef(sFuncDefParamQualifiers, sRejectFuncParam);
            return new SyntaxUnary(name, type);
        }

        enum StatementsType
        {
            MethodBody,
            PropertyBody
        }

        
        SyntaxExpr ParseMethodStatements(string errorMessage, StatementsType type)
        {
            if (mToken != "=>" && mToken != "{")
                RejectToken(mToken, "Expecting '=>' or '{'");

            if (mToken == "=>")
            {
                return ParseStatementsSeparator(errorMessage, type);
            }
            return ParseStatements(errorMessage);
        }

        /// <summary>
        /// Parse '{ statements }'
        /// </summary>
        SyntaxExpr ParseStatements(string errorMessage)
        {
            // Require '{'
            if (!AcceptMatchOrReject("{"))
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
            if (mToken != "=>")
                throw new Exception("Compiler error: Expecting '=>'");
            var keyword = Accept();
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
                    var autoImpl = new SyntaxBinary(autoKeyword, new SyntaxToken(getToken), new SyntaxToken(setToken));
                    return new SyntaxUnary(keyword, autoImpl);
                }
            }
            return new SyntaxUnary(keyword, ParseExpr());                 
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
                    requireSemicolon = false;
                    break;


                case "{":
                    RejectToken(mToken, "Unnecessary scope is not allowed");
                    statements.Add(ParseStatements("scope"));
                    requireSemicolon = false;
                    break;
                
                case "defer":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    break;

                case "@": // TBD: See if we like this syntax
                case "const":
                case "var":
                case "mut":
                    if (mTokenName == "@")
                        mToken.Type = eTokenType.Reserved;
                    statements.Add(new SyntaxUnary(Accept(), ParseNewVarStatment()));
                    break;

                case "while":
                    // WHILE (condition) (body)
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements("while")));
                    break;

                case "scope":
                    // SCOPE (body)
                    statements.Add(new SyntaxUnary(Accept(), ParseStatements("scope")));
                    break;

                case "do":
                    // DO (condition) (body)
                    var doKeyword = Accept();
                    var doStatements = ParseStatements("do");
                    var doExpr = (SyntaxExpr)new SyntaxError();
                    if (AcceptMatchOrReject("while"))
                        doExpr = ParseExpr();
                    statements.Add(new SyntaxBinary(doKeyword, doExpr, doStatements));
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
                                               new SyntaxMulti(NewVirtualToken(elseIfToken, "{"), FreeExprList(elseStatements))));
                        }
                    }
                    break;

                case "else":
                    Reject("Else must follow 'if' statement");
                    Accept();
                    break;

                case "for":
                    // FOR (variable) (condition) (statements)
                    Accept();
                    AcceptMatch("@");  // TBD: See if we like this syntax
                    var forVariable = new SyntaxToken(ParseIdentifier("Expecting a loop variable", sRejectForCondition));
                    forVariable.Token.Type = eTokenType.DefineLocal;

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
                    // SWITCH (expr) (statements)
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements("switch")));
                    break;

                case "finally":
                case "default":
                    statements.Add(new SyntaxToken(Accept()));
                    AcceptMatchOrReject(":");
                    requireSemicolon = false;
                    break;

                case "catch":
                case "case":
                    var caseToken = Accept();
                    var caseExpressions = NewExprList();
                    caseExpressions.Add(ParseExpr());
                    while (AcceptMatch(","))
                        caseExpressions.Add(ParseExpr());
                    statements.Add(new SyntaxMulti(caseToken, FreeExprList(caseExpressions)));
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
        /// Parse the right side of an assignment statement
        /// </summary>
        SyntaxExpr ParseAssignment()
        {
            if (mTokenName == "{" || mToken == "[")
                return ParseInitializer();
            return ParseExpr();
        }

        SyntaxExpr ParseNewVarStatment(WordSet typeFieldQualifiers = null)
        {
            if (!ParseIdentifier("Expecting variable name", out var newVarName))
                return new SyntaxError();
            newVarName.Type = eTokenType.DefineLocal;

            SyntaxExpr typeName;
            if (mTokenName != "=")
                typeName = ParseTypeDef(typeFieldQualifiers == null ?  sEmptyWordSet : typeFieldQualifiers, sEmptyWordSet);
            else
                typeName = new SyntaxToken(new Token(""));

            SyntaxExpr initializer;
            if (mTokenName == "=")
                initializer = new SyntaxUnary(Accept(), ParseAssignment());
            else
                initializer = new SyntaxToken(new Token(""));

            return new SyntaxBinary(newVarName, typeName, initializer);
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
            var result = ParseExponentiation();
            InterceptAndReplaceGT();
            while (sMultiplyOperators.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, ParseExponentiation());
                InterceptAndReplaceGT();
                if (mTokenName == "<<" || mTokenName == ">>")
                    RejectToken(mToken, "Shift operators are not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseExponentiation()
        {
            var result = ParseUnary();
            if (mTokenName == "^")
            {
                result = new SyntaxBinary(Accept(), result, ParseUnary());
                if (mTokenName == "^")
                    Reject("Exponentiation operator is not associative, must use parentheses");
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
            mToken = NewVirtualToken(mToken, virtualToken);
            mTokenName = mToken.Name;
        }

        SyntaxExpr ParseUnary()
        {
            if (sUnaryOperators.Contains(mTokenName))
            {
                return new SyntaxUnary(Accept(), ParseUnary());
            }

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

            return ParsePrimary();
        }

        SyntaxExpr ParsePrimary()
        {
            var result = ParseAtom();

            if (result.Token == "cast")
            {
                if (!ParseTypeName(out var castType, sEmptyWordSet))
                    return new SyntaxError(result.Token);
                if (mTokenName != "(")
                    RejectToken(mToken, "Expecting '('");
                result = new SyntaxUnary(result.Token, castType);
            }

            // Primary: function call 'f()', array 'a[]', member access 'x.y', type argument f<type>
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
                else if (mTokenName == "<")
                {
                    // Possibly a type argument list.  Let's try it and find out.
                    var typeArgIdentifier = mPrevToken;
                    var p = SaveParsePoint();
                    var openTypeToken = mToken;
                    var typeArgs = NewExprList();
                    if (ParseTypeArgumentList(typeArgs, null)
                            && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Type argument list
                        if (typeArgIdentifier.Type != eTokenType.Reserved)
                            typeArgIdentifier.Type = eTokenType.TypeName;
                        accepted = true;
                        typeArgs.Insert(0, result);
                        result = new SyntaxMulti(NewVirtualToken(openTypeToken, VT_TYPE_ARG_LIST), FreeExprList(typeArgs));
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
                    RejectToken(mToken, "Old style cast not allowed");

                // Use ")" to differentiate between function call and ordering
                return new SyntaxMulti(mPrevToken, FreeExprList(expressions));
            }

            // Number, string, identifier
            if (mToken.Type == eTokenType.Number)
            {
                return new SyntaxToken(Accept());
            }
            if (mToken.Type == eTokenType.Quote)
            {
                return ParseStringLiteral(null);
            }
            if (mToken.Type == eTokenType.Identifier)
            {
                var identifier = Accept();
                if (mToken.Type == eTokenType.Quote && identifier == "tr")
                {
                    identifier.Type = eTokenType.Reserved;
                    return ParseStringLiteral(identifier);
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
        /// Prefix may be null, or 'tr'.  Next token must be quoted string
        /// </summary>
        SyntaxExpr ParseStringLiteral(Token prefix)
        {
            if (prefix != null)
                prefix.AddWarning("Not stored in parse tree");
            var quote = mToken;
            var str = "";
            while (mToken.Type == eTokenType.Quote
                   || mToken.Type == eTokenType.Identifier
                   || mToken == "{")
            {
                if (mToken.Type == eTokenType.Identifier)
                {
                    var word = Accept();
                    word.Type = eTokenType.Reserved;
                    switch (word.Name)
                    {
                        case "tab":  str += "\t";  break;
                        case "cr": str += "\r"; break;
                        case "lf": str += "\n";  break;
                        case "crlf": str += "\r\n";  break;
                        case "bs": str += "\b"; break;
                        case "ff": str += "\f";  break;
                        default:
                            word.Type = eTokenType.Identifier;
                            RejectToken(word, "Unexpected token after string literal");
                            break;
                    }
                }
                else if (mToken == "{")
                {
                    mToken.AddWarning("Interpolated strings not stored in parse tree");
                    str += "~$";
                    Accept();
                    ParseExpr();
                    AcceptMatchOrReject("}");
                }
                else
                {
                    // Quoted string
                    var s = mTokenName.Substring(1, Math.Max(0, mTokenName.Length - 2));
                    str += s;
                    if (s.Contains("~$") || s.Contains("~#"))
                        mToken.AddError("String literal may not contain ~$ or ~#");
                    Accept();
                }
            }
            quote.AddInfo(str.Replace("\r\n", "{crlf}")
                             .Replace("\r", "{cr}")
                             .Replace("\n", "{lf}")
                             .Replace("\t", "{tab}")
                             .Replace("\f", "{ff}")
                             .Replace("\b", "{bs}")
                             .Replace("~$", "{expr}"));



            return new SyntaxUnary(NewVirtualToken(quote, "\""), new SyntaxToken(new Token(str)));
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
            if (mTokenName == "{" || mToken == "[")
                return ParseInitializer();

            // Allow 'ref' or 'out' qualifier
            if (sFuncCallParamQualifiers.Contains(mTokenName))
            {                 
                var qualifier = Accept();
                if (mTokenName == "mut" || mTokenName == "var" || mToken == "@")
                {
                    var keyword = Accept();
                    var identifier = ParseIdentifier("Expecting a variable name");
                    identifier.Type = eTokenType.DefineLocal;
                    return new SyntaxBinary(qualifier,
                            new SyntaxToken(keyword),
                            new SyntaxToken(identifier));
                }
                return new SyntaxUnary(qualifier, new SyntaxToken(Accept()));
            }

            return ParseExpr();
        }

        /// <summary>
        /// Initializer, starting with '{' or '['
        /// </summary>
        SyntaxExpr ParseInitializer()
        {
            if (mTokenName != "{" && mTokenName != "[")
                throw new Exception("Compiler error: Expecting '{' or '[' while parsing initializer");

            var isMap = mTokenName == "{";
            var close = isMap ? "}" : "]";
            var openToken = Accept();
            var parameters = NewExprList();
            if (mTokenName != close)
                parameters.Add(ParseInitializerElement(isMap));
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                if (mTokenName != close)
                    parameters.Add(ParseInitializerElement(isMap));
            }
            if (AcceptMatchOrReject(close, "or ','"))
                Connect(openToken, mPrevToken);
            return new SyntaxMulti(openToken, FreeExprList(parameters));
        }

        SyntaxExpr ParseInitializerElement(bool isMap)
        {
            if (mToken == "{" || mToken == "[")
            {
                if (isMap)
                    RejectToken(mToken, "Expecting an expression, the map index must not be an array or map");
                return ParseInitializer();
            }
            var expr = ParseExpr();
            if (isMap && AcceptMatchOrReject(":", "map index"))
                return new SyntaxBinary(mPrevToken, expr, mTokenName == "{" || mTokenName == "[" ? ParseInitializer() : ParseExpr());

            return expr;
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
            else if (mTokenName == "fun" || mTokenName == "afun")
                // mTokenName == "func" || mTokenName == "afunc")                
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
            keyword.Type = eTokenType.TypeName;
            ParseFuncDef(out var typeParams, out var parameters, out var returnType);
            return new SyntaxToken(mToken);
        }

        /// <summary>
        /// Error causes reject until errorStop unless errorStop is null,
        /// in which case error checking is not performed
        /// </summary>
        bool ParseTypeName(out SyntaxExpr result, WordSet errorStop)
        {
            // Unary operators '^' and '?
            result = null;
            if (mToken == "?" || mToken == "mut" || mToken == PTR || mToken == REFERENCE)
            {
                var token = Accept();
                if (token.Type != eTokenType.Reserved)
                    token.Type = eTokenType.TypeName;
                if (!ParseTypeName(out var expr, errorStop))
                    return false;
                result = new SyntaxUnary(token, expr);
                return true;
            }

            if (!ParseIdentifier("Expecting a type name", out var typeName, errorStop))
                return false;
            typeName.Type = eTokenType.TypeName;
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
                    dotTypeName.Type = eTokenType.TypeName;
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
                    result = new SyntaxMulti(NewVirtualToken(openTypeToken, VT_TYPE_ARG_LIST), FreeExprList(typeArgs));
                }
            } while (accepted);
            return true;
        }

        /// <summary>
        /// Try parsing a type argument list. 
        /// Error causes reject until errorStop unless errorStop is null,
        /// in which case error checking is not performed. 
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
            public eTokenType TokenType;
            public Token Inserted;
            public bool ParseError;
        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.LexerEnum = mLexerEnum;
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            p.TokenType = mToken.Type;
            p.Inserted = mInsertedToken;
            p.ParseError = mParseError;
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mLexerEnum = p.LexerEnum;
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mToken.Type = p.TokenType;
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
            else if (mTokenName[0] == '\"' || mTokenName[0] == '`')
            {
                mToken.Type = eTokenType.Quote;
                if (mTokenName.Length <= 1 || !mTokenName.EndsWith(mTokenName[0].ToString()))
                        RejectToken(mToken, "Exepcting an end " + mTokenName[0] + " character");
            }
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
                    && ! (sBeginLineSkipSemicolon.Contains(mTokenName) 
                            || mTokenName.StartsWith("\"")
                            || mTokenName.StartsWith("`")))
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

            // Check for tabs
            if (mToken.Boln)
            {
                var line = mLexer.GetLine(mToken.Y);
                var i = line.IndexOf('\t');
                while (i >= 0)
                {
                    var tabToken = new Token(" ",i, mToken.Y);
                    tabToken.Invisible = true;
                    RejectToken(tabToken, "Illegal tab");
                    i = line.IndexOf('\t', i + 1);
                }
            }
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

            // TBD: Force accept here?  In cases where we don't
            //      accept anything we can hang.  But if we're sitting
            //      at an important keyword, we don't want to skip it.
            //      To see where we correct the problem, look for where
            //      the return value is used.
            //      Example: `Poperator[](key TKey) TValue => impl get`

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

        Token NewVirtualToken(Token connectedToken, string text)
        {
            var virtualToken = new Token(text, connectedToken.X, connectedToken.Y, eTokenType.Normal);
            virtualToken.Invisible = true;
            Connect(connectedToken, virtualToken);
            return virtualToken;
        }


    }

}
