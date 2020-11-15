using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.Remoting.Messaging;
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
        public const string VT_LAMBDA_BRACE = "=>{";
        public const string PTR = "*";

        ParseZurfCheck mZurfParseCheck;

        int                 mParseErrors;	// Number of errors
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken = new Token(";");
        Token               mPrevToken = new Token(";");
        StringBuilder       mComments = new StringBuilder();
        SyntaxField         mLastField;
        List<Token>         mExtraTokens = new List<Token>();
        int                 mTernaryLevel;
        Token               mTokenAfterInsertedToken;

        // Be kind to GC
        Queue<List<SyntaxExpr>>   mExprCache = new Queue<List<SyntaxExpr>>();

        string mNamespaceBaseStr = "";
        string[] mNamespaceBasePath = Array.Empty<string>();
        string[] mNamePath = Array.Empty<string>();
        SyntaxFile mSyntax;

        public int ParseErrors => mParseErrors;

        // Add semicolons to all lines, except for:
        static WordSet sEndLineSkipSemicolon = new WordSet("; { [ ( ,");
        static WordSet sBeginLineSkipSemicolon = new WordSet("{ ] ) + - / % | & || && == != = "
                            + ": ? . , > << <= < => -> .. :: !== === += -= *= /= %= &= |= =  is in as");
        static WordSet sBeginLineForceSemicolon = new WordSet("}");


        static WordSet sReservedWords = new WordSet("abstract as base break case catch class const "
            + "continue default delegate do then else enum event explicit extern true false defer use "
            + "finally fixed for goto if implicit in interface internal is lock namespace module include "
            + "new null operator out override pub public private protected readonly ro ref mut "
            + "return unsealed unseal sealed sizeof stackalloc heapalloc static struct switch this throw try "
            + "typeof type unsafe using static virtual while dowhile asm managed unmanaged "
            + "async await astart func afunc get set aset aget global partial var where nameof "
            + "box boxed init move copy clone drop error dispose own owned "
            + "trait extends implements implement impl union fun afun def yield let cast "
            + "any dyn loop select match event from to of on cofun cofunc global local val it throws atask task "
            + "scope assign @ and or not xor with cap");

        static WordSet sReservedFuncNames = new WordSet("new init drop copy move clone dispose match cast default implicit");
        static WordSet sReservedIdentifierVariables = new WordSet("null this true false default base match it");

        static WordSet sClassFieldQualifiers = new WordSet("pub public protected private internal unsafe "
            + "static unsealed abstract virtual override new ro boxed mut");

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sAllowConstraintKeywords = new WordSet("class struct unmanaged");
        static WordSet sExternalMethodBodies = new WordSet("impl extern");

        public static WordSet sOverloadableOps = new WordSet("+ - * / % [ in");
        static WordSet sCompareOps = new WordSet("== != < <= > >= === !== in");
        static WordSet sRangeOps = new WordSet(".. ::");
        static WordSet sAddOps = new WordSet("+ - |");
        static WordSet sXorOps = new WordSet("~");
        static WordSet sMultiplyOps = new WordSet("* / % &");
        static WordSet sAssignOps = new WordSet("= += -= *= /= %= |= &= ~= <<= >>=");
        static WordSet sUnaryOps = new WordSet("+ - ! & ~ use unsafe " + PTR);

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // This seems stange because something like `a = F<T1,T2>;` is not a valid expression
        // The following symbols allow us to call functions, create types, access static members, and cast
        // For example `F<T1>()` to call a function or constructor, `F<T1>.Name` to access a static or member,
        // and #F<T1>(expression) to cast.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) .");

        static WordSet sStatementEndings = new WordSet("; => }");
        static WordSet sStatementsDone = new WordSet("} func fun namespace class struct interface enum", true);
        static WordSet sRejectAnyStop = new WordSet("=> ; { } namespace class struct interface enum if else for while throw switch case func fun prop aprop get set", true);
        static WordSet sRejectForCondition = new WordSet("in");
        static WordSet sRejectFuncName = new WordSet("(");
        static WordSet sRejectFuncParam = new WordSet(", )");

        static WordMap<string> sStringLiterals = new WordMap<string>()
            { { "lf", "\n" }, { "cr", "\r"}, {"crlf", "\r\n"}, {"tab", "\t"} };


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

        public SyntaxFile Parse()
        {
            mSyntax = new SyntaxFile();

            if (Debugger.IsAttached)
            {
                // Failure causes error in dubugger
                ParseCompilationUnit();
                mZurfParseCheck.Check(mSyntax);
                mLexer.MetaTokens = mExtraTokens.ToArray();
            }
            else
            {
                try
                {
                    ParseCompilationUnit();
                    mZurfParseCheck.Check(mSyntax);
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
                finally
                {
                    mLexer.MetaTokens = mExtraTokens.ToArray();
                }
            }
            if (mTokenName != "")
                RejectToken(mToken, "Parse error: Expecting end of file");
            while (mTokenName != "")
                Accept();

            return mSyntax;
        }

        /// <summary>
        /// Parse the file
        /// </summary>
        void ParseCompilationUnit()
        {
            mSyntax = new SyntaxFile();
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
        /// Parse using, namespace, enum, interface, struct, class, func, or field
        /// </summary>
        void ParseScopeStatements(SyntaxScope parentScope)
        {
            bool topScope = parentScope == null;
            var qualifiers = new List<Token>();
            while (mTokenName != "" && (mTokenName != "}" || topScope))
            {
                var attributes = NewExprList();
                while (mTokenName == "[")
                {
                    ParseParen(attributes, false);
                    while (mTokenName == ";")
                        Accept();
                }
                FreeExprList(attributes); // TBD: Store in expression tree

                // Read qualifiers
                qualifiers.Clear();
                while (sClassFieldQualifiers.Contains(mTokenName))
                {
                    qualifiers.Add(Accept());
                }

                var keyword = mToken;
                bool ignoreSemicolon = false;
                switch (mTokenName)
                {
                    case ";":
                        ignoreSemicolon = true;
                        Accept();
                        RejectQualifiers(qualifiers, "Expecting a class/struct/fun/prop, etc. or field definition");
                        break;

                    case "{":
                    case "=>":
                        Accept();
                        RejectQualifiers(qualifiers, "Unexpected qualifiers");
                        RejectToken(keyword, "Unexpected '" + keyword + "'.  Expecting a keyword, such as 'class', 'fun', etc. before the start of a new scope.");
                        break;

                    case "use":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        mSyntax.Using.Add(ParseUsingStatement(keyword));
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'using' statement");
                        if (mNamespaceBasePath.Length != 0)
                            RejectToken(keyword, "Using statements must come before the namespace");
                        break;

                    case "namespace":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'namespace' statement");
                        if (parentScope != null)
                            RejectToken(keyword, "Namespace statements must not be inside a class/enum/interface body");
                        ParseNamespaceStatement(keyword);
                        break;

                    case "interface":
                    case "enum":
                    case "struct":
                    case "class":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        while (AcceptMatch("ref") || AcceptMatch("ro"))
                            qualifiers.Add(mPrevToken);
                        ParseClass(keyword, parentScope, qualifiers);
                        break;

                    case "fun":
                    case "afun":
                    //case "func":
                    //case "afunc":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        keyword.Type = eTokenType.ReservedControl;  // Fix keyword to make it control
                        ParseMethod(keyword, parentScope, qualifiers);
                        break;

                    case "prop":
                    case "aprop":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        ParseProperty(keyword, parentScope, qualifiers);
                        break;

                    case "const":
                    case "var":
                    case "@": // TBD: Keep this is we want to require `@` in front of class fields
                        if (mTokenName != "@")
                            mToken.Type = eTokenType.ReservedControl;
                        qualifiers.Add(Accept());
                        var classFieldVarName = ParseField(parentScope, qualifiers);
                        if (classFieldVarName != null && !mToken.Error)
                            mSyntax.Fields.Add(classFieldVarName);
                        break;

                    default:
                        if (parentScope != null && parentScope.Keyword == "enum" && keyword.Type == eTokenType.Identifier)
                        {
                            // For enum, assume the first identifier is a field
                            Accept();
                            var enumFieldName = ParseEnumField(parentScope, qualifiers, keyword);
                            if (!mToken.Error)
                                mSyntax.Fields.Add(enumFieldName);
                        }
                        else if (keyword.Type == eTokenType.Identifier)
                        {
                            // TBD: Remove this if we require `@` in front of field definitions
                            var classFieldVarName2 = ParseField(parentScope, qualifiers);
                            if (classFieldVarName2 != null && !mToken.Error)
                                mSyntax.Fields.Add(classFieldVarName2);
                        }
                        else
                        {
                            Accept();
                            RejectToken(keyword, "Expecting a qualifier ('pub', etc.), or reserved word such as 'var', 'fun', 'class', etc.");
                        }
                        break;
                }
                if (!ignoreSemicolon)
                    AcceptSemicolonOrReject();
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

        SyntaxUsing ParseUsingStatement(Token keyword)
        {
            var synUsing = new SyntaxUsing();
            synUsing.Keyword = keyword;
            var tokens = new List<Token>();
            ParseQualifiedIdentifier(tokens, "Expecting a namespace identifier");
            synUsing.NamePath = tokens.ToArray();
            return synUsing;
        }

        void ParseNamespaceStatement(Token keyword)
        {
            var namePath = new List<Token>();
            do
            {
                if (!ParseIdentifier("Expecting a namespace identifier", out var t1))
                    break;
                namePath.Add(t1);
            } while (AcceptMatch("."));

            if (namePath.Count == 0)
                return; // Rejected above

            // Check for file namespace prefix
            for (int i = 0; i < Math.Min(namePath.Count, mNamespaceBasePath.Length); i++)
            {
                if (namePath[i].Name != mNamespaceBasePath[i])
                {
                    RejectToken(namePath[i], "Expecting namespace to start with '" + mNamespaceBaseStr + "'");
                    return;
                }
            }
            // Reject if not full prefix
            if (namePath.Count < mNamespaceBasePath.Length)
            {
                RejectToken(namePath[namePath.Count-1], "Expecting namespace to start with '" + mNamespaceBaseStr + "'");
                return;
            }

            // Collect base namespace
            mNamePath = namePath.ConvertAll(token => token.Name).ToArray();
            var namePathStr = string.Join(".", mNamePath);
            if (mNamespaceBasePath.Length == 0)
            {
                mNamespaceBasePath = mNamePath;
                mNamespaceBaseStr = namePathStr;
            }
            if (!mSyntax.Namesapces.TryGetValue(namePathStr, out var ns))
            {
                ns = new SyntaxNamespace();
                mSyntax.Namesapces[namePathStr] = new SyntaxNamespace();
            }
            // Accumulate comments and keyword tokens for this namespace
            ns.Comments += " " + mComments;
            ns.Tokens.Add(keyword);
        }

        // Parse class, struct, interface, or enum
        void ParseClass(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            var synClass = new SyntaxType();
            synClass.Qualifiers = qualifiers.ToArray();
            synClass.ParentScope = parentScope;
            synClass.NamePath = mNamePath;
            synClass.Comments = mComments.ToString();
            synClass.Keyword = keyword;

            // Parse class name and type parameters
            if (!ParseIdentifier("Expecting a type name", out synClass.Name))
                return;
            synClass.Name.Type = eTokenType.TypeName;
            synClass.TypeParams = ParseTypeParams();
            mSyntax.Types.Add(synClass);

            if (AcceptMatchSkipInvisibleSemicolon("extends"))
                synClass.Extends = ParseTypeName();

            // Parse implemented classes
            if (AcceptMatchSkipInvisibleSemicolon("implements"))
            {
                var baseClasses = NewExprList();
                baseClasses.Add(ParseTypeName());
                while (AcceptMatch(","))
                {
                    baseClasses.Add(ParseTypeName());
                }
                synClass.Implements = FreeExprList(baseClasses);
            }

            synClass.Constraints = ParseConstraints();

            if (AcceptMatch("="))
            {
                synClass.Alias = ParseTypeName();
                return;
            }


            // Push new path
            var oldPath = mNamePath;
            var namePath = new List<string>(mNamePath);
            namePath.Add(synClass.Name);
            mNamePath = namePath.ToArray();

            // Simple class or struct
            if (AcceptMatch("("))
            {
                synClass.Simple = true;
                var open = mPrevToken;
                do
                {
                    var simpleField = ParseField(synClass, new List<Token>());
                    if (simpleField != null && !mToken.Error)
                    {
                        simpleField.Simple = true;
                        mSyntax.Fields.Add(simpleField);
                    }
                } while (AcceptMatch(","));
                if (AcceptMatchOrReject(")"))
                    Connect(mPrevToken, open);
            }

            if (AcceptMatch("{"))
            {
                var openToken = mPrevToken;
                ParseScopeStatements(synClass);

                if (AcceptMatchOrReject("}", "while parsing " + synClass.Keyword.Name + " body of '" + synClass.Name + "'"))
                    Connect(openToken, mPrevToken);
                else
                    RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace"
                                                            : "This scope has an error on its closing brace");
            }
            else
            {
                if (!synClass.Simple)
                    Reject("Expecting '{', '(', or '= '");
            }

            // Restore old path
            mNamePath = oldPath;
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
                if (mTokenName == "in" || mTokenName == "out")
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
            List<SyntaxConstraint> constraints = new List<SyntaxConstraint>();
            while (AcceptMatchSkipInvisibleSemicolon("where"))
                constraints.Add(ParseConstraint(mPrevToken));
            return constraints.ToArray();
        }

        SyntaxConstraint ParseConstraint(Token keyword)
        {
            var constraint = new SyntaxConstraint();
            constraint.Keyword = keyword;
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
                constraintTypeNames.Add(ParseTypeName());
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
            field.NamePath = mNamePath;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            field.Name = mToken;
            mLastField = field; // Allow us to pick up comments on this line

            if (!AcceptMatch("default"))
            {
                if (!ParseIdentifier("Expecting field name", out var newVarName))
                    return null;
                newVarName.Type = eTokenType.DefineField;
            }

            if (mTokenName != "=")
                field.TypeName = ParseTypeName();

            if (mTokenName == "=")
                field.Initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

            return field;
        }

        /// <summary>
        /// Current token must already be checked for validity
        /// </summary>
        SyntaxField ParseEnumField(SyntaxScope parentScope, List<Token> qualifiers, Token name)
        {
            var field = new SyntaxField();
            field.ParentScope = parentScope;
            field.NamePath = mNamePath;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            field.Name = name;
            name.Type = eTokenType.DefineField;
            mLastField = field; // Allow us to pick up comments on this line

            // Optionally initialize
            if (AcceptMatch("="))
            {
                // Initialize via assignment
                field.Initializer = ParseExpr();
            }
            return field;
        }

        private void ParseProperty(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            // Property name
            if (!ParseIdentifier("Expecting a property name", out var propertyName))
                return;
            propertyName.Type = eTokenType.DefineField;

            var typeName = ParseTypeName();
            if (mToken.Error)
                return;

            // Look ahead for problems
            if (mTokenName != "=>" && mTokenName != "{" && mTokenName != "get" && mTokenName != "=")
            {
                Reject("Expecting '{', '=>', or 'get'"); // NOTE: "=" rejected below
                return;
            }
            if (mTokenName == "=")
            {
                RejectToken(mToken, "Expecting '{', '=>', or 'get'.  Only auto generated properties can have an initialization expression.");
                ParseRightSideOfAssignment(); // Throw it away
            }

            // Auto property
            if (AcceptMatch("get"))
            {
                var field = new SyntaxField();
                field.Name = propertyName;
                field.TypeName = typeName;
                field.GetToken = mPrevToken;
                if (AcceptMatch("private") || AcceptMatch("protected"))
                    field.GetSetVisibilityToken = mPrevToken;
                if (AcceptMatch("set"))
                    field.SetToken = mPrevToken;
                if (mTokenName == "=")
                    field.Initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());
                return;
            }


            var synFunc = new SyntaxFunc();
            synFunc.ParentScope = parentScope;
            synFunc.NamePath = mNamePath;
            synFunc.Comments = mComments.ToString();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = keyword;
            synFunc.Name = propertyName;
            synFunc.ReturnType = typeName;

            if (mTokenName == "=>")
                synFunc.Statements = ParseStatementsSeparator();
            else
                synFunc.Statements = ParseStatements("'" + synFunc.Name + "' property");

            mSyntax.Funcs.Add(synFunc);
        }


        /// <summary>
        /// Func, construct, operator
        /// </summary>
        void ParseMethod(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc();
            synFunc.ParentScope = parentScope;
            synFunc.NamePath = mNamePath;
            synFunc.Comments = mComments.ToString();
            synFunc.Keyword = keyword;

            if (!ParseFuncNameDef(out synFunc.ClassName, out synFunc.Name)
                    || synFunc.Name == null)
            {
                return;
            }
            synFunc.TypeParams = ParseTypeParams();
            synFunc.Params = ParseFuncParamsDef();

            if (mTokenName == "class" || mTokenName == "struct")
                RejectToken(mToken, "Use ':' or ':class' instead");

            if (AcceptMatchSkipInvisibleSemicolon(":"))
                synFunc.ReturnType = ParseFuncParamsDef();
            else
                synFunc.ReturnType = ParseTypeName(false);

            if (mTokenName == "error")
                qualifiers.Add(Accept());

            synFunc.Constraints = ParseConstraints();

            if (mTokenName == "=>")
                synFunc.Statements = ParseStatementsSeparator();
            else
                synFunc.Statements = ParseStatements("'" + synFunc.Name + "' method");


            synFunc.Qualifiers = qualifiers.ToArray();
            mSyntax.Funcs.Add(synFunc);
        }

        bool ParseFuncNameDef(out SyntaxExpr className, out Token funcName)
        {
            // Try parsing a class name first (Optional, must be followed by "::")
            className = null;
            if (mToken.Type == eTokenType.Identifier)
            {
                var pp = SaveParsePoint();
                mParseErrors = 0;
                className = ParseTypeName();
                if (mParseErrors != 0 || !AcceptMatch("::")) 
                {
                    className = null;
                    RestoreParsePoint(pp);
                }
                mParseErrors = pp.ParseErrors;
            }

            // Parse operator
            if (className == null && AcceptMatch("operator"))
            {
                var operatorKeyword = mPrevToken;
                if (sOverloadableOps.Contains(mTokenName))
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

            if (sReservedFuncNames.Contains(mTokenName))
            {
                funcName = Accept();
                return true;
            }

            if (ParseIdentifier("Expecting a function", out funcName))
            {
                funcName.Type = eTokenType.DefineMethod;
                return true;
            }
            return false;
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
                parameters.Add(ParseFuncParamDef());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseFuncParamDef());
            }

            // Ellipse to signify repeated parameters
            if (AcceptMatch("..."))
                mPrevToken.AddWarning("Repeated parameters not supported yet");

            if (AcceptMatchOrReject(")", " or ','"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(Token.Empty, FreeExprList(parameters));
        }

        SyntaxExpr ParseFuncParamDef()
        {
            if (!ParseIdentifier("Expecting a variable name", out var name, sRejectFuncParam))
                return new SyntaxError();
            name.Type = eTokenType.DefineParam;
            var type = ParseTypeName();

            // Default parameter
            if (mTokenName == "=")
            {
                Accept();
                return new SyntaxBinary(name, type, ParseExpr());
            }
            return new SyntaxUnary(name, type);
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

            if (AcceptMatchOrReject("}", "while parsing " + errorMessage))
                Connect(openToken, mPrevToken);
            else
                RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace"
                                                        : "This scope has an error on its closing brace");

            return new SyntaxMulti(openToken, FreeExprList(statements));
        }

        /// <summary>
        /// Parse '=> expression', including things like '=> extern get'
        /// </summary>
        SyntaxExpr ParseStatementsSeparator()
        {
            if (mToken != "=>")
                throw new Exception("Compiler error: Expecting '=>'");
            var keyword = Accept();
            if (sExternalMethodBodies.ContainsKey(mTokenName))
            {
                var getToken = Token.Empty;
                var setToken = Token.Empty;
                var extrnalToken = Accept();
                if (AcceptMatch("get"))
                    getToken = mPrevToken;
                if (AcceptMatch("set"))
                    setToken = mPrevToken;
                return new SyntaxUnary(keyword, new SyntaxBinary(extrnalToken,
                            new SyntaxToken(getToken), new SyntaxToken(setToken)));
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
                    statements.Add(ParseStatements("'{' scope"));
                    requireSemicolon = false;
                    break;
                
                case "defer":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    break;

                case "@":
                case "const":
                    bool allowUnderscore = mTokenName == "@";
                    statements.Add(new SyntaxUnary(Accept(), ParseNewVarStatment(allowUnderscore)));
                    break;

                case "while":
                    // WHILE (condition) (body)
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements("'while' statement")));
                    break;

                case "scope":
                    // SCOPE (body)
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxUnary(Accept(), ParseStatements("'scope' statement")));
                    break;

                case "do":
                    // DO (condition) (body)
                    var doKeyword = Accept();
                    var doStatements = ParseStatements("'do' statement");
                    var doExpr = (SyntaxExpr)new SyntaxError();
                    if (AcceptMatchOrReject("while"))
                        doExpr = ParseExpr();
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxBinary(doKeyword, doExpr, doStatements));
                    break;                        

                case "if":
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    var ifCondition = ParseExpr();
                    var ifBody = ParseStatements("'if' statement");
                    requireSemicolon = false;
                    if (!AcceptMatchSkipInvisibleSemicolon("else"))
                    {
                        // No else clause, generate: IF (condition) (body)
                        statements.Add(new SyntaxBinary(keyword, ifCondition, ifBody));
                        break;
                    }
                    mPrevToken.Type = eTokenType.ReservedControl;
                    if (mTokenName != "if")
                    {
                        // Parse else clause, generate: // IF (condition) (body) (else-body)
                        statements.Add(new SyntaxMulti(keyword, ifCondition, ifBody, ParseStatements("'else' statement")));
                    }
                    else
                    {
                        // Parse else if clause, generate: IF (condition) (body) ( (else-if-body) )
                        mToken.Type = eTokenType.ReservedControl;
                        var elseIfToken = mToken;
                        var elseStatements = NewExprList();
                        ParseStatement(elseStatements);
                        statements.Add(new SyntaxMulti(keyword, ifCondition, ifBody, 
                                            new SyntaxMulti(NewVirtualToken(elseIfToken, "{"), FreeExprList(elseStatements))));
                    }
                    break;

                case "else":
                    mToken.Type = eTokenType.ReservedControl;
                    Reject("Else must follow 'if' statement");
                    Accept();
                    break;

                case "for":
                    // FOR (variable) (condition) (statements)
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    if (!AcceptMatch("@"))
                        RejectToken(mToken, "Expecting '@'");
                    var forVariable = new SyntaxToken(ParseIdentifier("Expecting a loop variable", sRejectForCondition));
                    forVariable.Token.Type = eTokenType.DefineLocal;

                    AcceptMatchOrReject("in");
                    var forCondition = ParseExpr();
                    statements.Add(new SyntaxMulti(keyword, forVariable, forCondition, ParseStatements("'for' statement")));
                    break;

                case "throw":
                case "return":
                    Accept();
                    if (sStatementEndings.Contains(mTokenName))
                    {
                        statements.Add(new SyntaxToken(keyword));
                        break;
                    }
                    var expressions = NewExprList();
                    do
                    {
                        expressions.Add(new SyntaxUnary(keyword, ParseExpr()));
                    } while (AcceptMatch(","));
                    statements.Add(new SyntaxMulti(keyword, FreeExprList(expressions)));
                    break;

                case "continue":
                case "break":
                    statements.Add(new SyntaxToken(Accept()));
                    break;

                case "switch":
                    // SWITCH (expr) (statements)
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements("'switch' statement")));
                    break;

                case "get":
                case "set":
                case "finally":
                case "default":
                    keyword.Type = eTokenType.ReservedControl;
                    Accept();
                    if (keyword.Name == "get" && AcceptMatch("=>"))
                    {
                        // get => expression
                        statements.Add(new SyntaxUnary(keyword, ParseExpr()));
                    }
                    else
                    {
                        AcceptMatchOrReject(":", keyword.Name == "get" ? "or '=>'" : "");
                        statements.Add(new SyntaxToken(keyword));
                    }
                    requireSemicolon = false;
                    break;

                case "error":
                case "catch":
                case "case":
                    mToken.Type = eTokenType.ReservedControl;
                    var caseToken = Accept();
                    var caseExpressions = NewExprList();
                    caseExpressions.Add(ParseConditionalOr());
                    while (AcceptMatch(","))
                        caseExpressions.Add(ParseConditionalOr());
                    statements.Add(new SyntaxMulti(caseToken, FreeExprList(caseExpressions)));
                    AcceptMatchOrReject(":", "or ','");
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
                    if (sAssignOps.Contains(mToken))
                        result = new SyntaxBinary(Accept(), result, ParseRightSideOfAssignment());
                    statements.Add(result);
                    break;
            }
            if (requireSemicolon)
                AcceptSemicolonOrReject();
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
            var virtualToken = token.Name + peek;
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
            mToken = NewVirtualToken(token, virtualToken);
            mTokenName = mToken.Name;
        }

        SyntaxExpr ParseNewVarStatment(bool allowUnderscore)
        {
            Token newVarName;
            if (allowUnderscore && mTokenName == "_")
                newVarName = Accept();
            else if (!ParseIdentifier("Expecting variable name", out newVarName))
                return new SyntaxError();

            if (newVarName.Name != "_")
                newVarName.Type = eTokenType.DefineLocal;

            SyntaxExpr typeName;
            if (mTokenName != "=")
                typeName = ParseTypeName();
            else
                typeName = new SyntaxToken(new Token(""));

            SyntaxExpr initializer;
            if (mTokenName == "=")
                initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());
            else
                initializer = new SyntaxToken(new Token(""));

            return new SyntaxBinary(newVarName, typeName, initializer);
        }

        /// <summary>
        /// Parse the right side of an assignment statement
        /// </summary>
        SyntaxExpr ParseRightSideOfAssignment()
        {
            var result = ParseExpr();
            if (sAssignOps.Contains(mTokenName))
                Reject("Assignment operator is not associative, must use separate statement)");
            return result;
        }

        ///  Parse expression (doesn't include ',' or '=' statements)
        SyntaxExpr ParseExpr()
        {
            return ParsePair();
        }

        SyntaxExpr ParsePair()
        {
            var result = ParseLambda();
            if (mTokenName == ":")
            {
                result = new SyntaxBinary(Accept(), result, ParseLambda());
                if (mTokenName == ":")
                    Reject("Pair operator ':' is not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseLambda()
        {
            var result = ParseTernary();

            if (mTokenName == "=>")
            {
                if (result.Token.Name != "@")
                    RejectToken(mToken, "Left side must be new variable expression, e.g. '@a' or '@(a,b)'");

                var lambdaToken = Accept();
                if (mTokenName == "{")
                    result = new SyntaxBinary(lambdaToken, result, ParseStatements("lambda statements"));
                else
                    result = new SyntaxBinary(lambdaToken, result, ParseTernary());

                if (mTokenName == "=>")
                    Reject("Lambda operator '=>' is not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseTernary()
        {
            var result = ParseConditionalOr();
            while (mTokenName == "?")
            {
                if (mTernaryLevel != 0)
                    RejectToken(mToken, "Ternary expressions may not be nested");
                mTernaryLevel++;
                mToken.Type = eTokenType.BoldSymbol;
                var operatorToken = Accept();
                var firstConditional = ParseRange();
                if (mTokenName != ":")
                {
                    mTernaryLevel--;
                    Reject("Expecting a ':' to separate expression for the ternary '?' operator");
                    return result;
                }
                mToken.Type = eTokenType.BoldSymbol;
                Connect(mToken, operatorToken);
                Accept();
                result = new SyntaxMulti(operatorToken, result, firstConditional, ParseRange());
                mTernaryLevel--;

                if (mTokenName == "?")
                    RejectToken(mToken, "Ternary operator is not associative");
                else if (mTokenName == ":")
                    RejectToken(mToken, "Ternary operator already has an else clause.");
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
            if (sCompareOps.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, ParseRange());
                InterceptAndReplaceGT();
                if (sCompareOps.Contains(mTokenName))
                    Reject("Compare operators are not associative, must use parentheses");
            }
            return result;
        }


        SyntaxExpr ParseRange()
        {
            var result = sRangeOps.Contains(mTokenName) ? new SyntaxToken(Token.Empty) : ParseAdd();
            if (sRangeOps.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, 
                    mTokenName == ")" || mTokenName == "]" ? new SyntaxToken(Token.Empty) : ParseAdd());
                if (sRangeOps.Contains(mTokenName))
                    Reject("Range operator is not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseAdd()
        {
            var result = ParseXor();
            while (sAddOps.Contains(mTokenName))
                result = new SyntaxBinary(Accept(), result, ParseXor());
            return result;
        }

        SyntaxExpr ParseXor()
        {
            var result = ParseMultiply();
            while (sXorOps.Contains(mTokenName))
                result = new SyntaxBinary(Accept(), result, ParseMultiply());
            return result;
        }

        SyntaxExpr ParseMultiply()
        {
            var result = ParseShift();
            while (sMultiplyOps.Contains(mTokenName))
                result = new SyntaxBinary(Accept(), result, ParseShift());
            return result;
        }

        SyntaxExpr ParseShift()
        {
            var result = ParseIsAs();
            InterceptAndReplaceGT();
            while (mTokenName == "<<" || mTokenName == ">>")
            {
                result = new SyntaxBinary(Accept(), result, ParseIsAs());
                InterceptAndReplaceGT();
                if (mTokenName == "<<" || mTokenName == ">>")
                    RejectToken(mToken, "Shift operators are not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseIsAs()
        {
            var result = ParseNewVarCapture();
            if (mTokenName == "is" || mTokenName == "as")
                result = new SyntaxBinary(Accept(), result, ParseTypeName());
            return result;
        }

        SyntaxExpr ParseNewVarCapture()
        {
            var result = ParseUnary();
            if (mTokenName == "@")
            {
                result = new SyntaxBinary(Accept(), result, ParseNewVarExpr());
                if (mTokenName == "@")
                    Reject("New variable operator '@' is not associative");
            }
            return result;
        }

        SyntaxExpr ParseUnary()
        {
            if (sUnaryOps.Contains(mTokenName))
            {
                if (mTokenName == "+")
                    RejectToken(mToken, "Unary '+' operator is not allowed");
                return new SyntaxUnary(Accept(), ParseUnary());
            }
            if (mTokenName == "@")
            {
                var result = new SyntaxUnary(Accept(), ParseNewVarExpr());
                if (mTokenName == "@")
                    Reject("New variable operator '@' is not associative");
                return result;
            }

            if (mTokenName == "cast")
            {
                var castToken = Accept();
                if (!AcceptMatchOrReject("("))
                    return new SyntaxError(mToken);
                var castOpenToken = mPrevToken;
                var castType = ParseTypeName();
                if (AcceptMatchOrReject(")"))
                    Connect(mPrevToken, castOpenToken);
                return new SyntaxBinary(castToken, castType, ParseUnary());
            }

            if (mTokenName == "sizeof" || mTokenName == "typeof")
            {
                var sizeofToken = Accept();
                if (!AcceptMatchOrReject("("))
                    return new SyntaxError(mToken);
                var sizeofOpen = mPrevToken;
                var sizeofType = ParseTypeName();
                if (AcceptMatchOrReject(")"))
                    Connect(sizeofOpen, mPrevToken);
                return new SyntaxUnary(sizeofToken, sizeofType);
            }

            if (mTokenName == "class" || mTokenName == "struct")
            {
                return ParseAnonymousClass(Accept());
            }

            return ParsePrimary();
        }
        private SyntaxExpr ParseNewVarExpr()
        {
            var newVarList = NewExprList();
            if (AcceptMatch("("))
            {
                var open = mPrevToken;
                do
                {
                    if (ParseIdentifier("", out var varName))
                    {
                        varName.Type = eTokenType.DefineLocal;
                        newVarList.Add(new SyntaxToken(mPrevToken));
                    }
                } while (AcceptMatch(","));

                if (AcceptMatchOrReject(")"))
                    Connect(open, mPrevToken);
            }
            else
            {
                if (mTokenName != "=>" && ParseIdentifier("", out var varName))
                {
                    varName.Type = eTokenType.DefineLocal;
                    newVarList.Add(new SyntaxToken(mPrevToken));
                }
            }

            return new SyntaxMulti(Token.Empty, FreeExprList(newVarList));
        }

        SyntaxExpr ParsePrimary()
        {
            var result = ParseAtom();

            // Primary: function call 'f()', array 'a[]', member access 'x.y', type argument f<type>
            bool accepted;
            do
            {
                accepted = false;
                if (mTokenName == "(" || mTokenName == "[")
                {
                    // Function call or array access
                    accepted = true;
                    result = ParseParen(mToken, mTokenName == "(", result);
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

                    mParseErrors = 0;
                    var typeArgs = ParseTypeArgumentList();
                    if (mParseErrors == 0 && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Yes, it is a type argument list.  Keep it
                        mParseErrors = p.ParseErrors;
                        if (typeArgIdentifier.Type != eTokenType.Reserved)
                            typeArgIdentifier.Type = eTokenType.TypeName;
                        accepted = true;
                        result = new SyntaxBinary(NewVirtualToken(openTypeToken, VT_TYPE_ARG_LIST), result, typeArgs);
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

            // Parse parentheses: (expression) - not a function call
            if (mTokenName == "(" || mToken == "[")
            {
                // Use ")" or "]" to differentiate between function call and ordering
                var open = mToken;
                var close = mTokenName == "(" ? ")" : "]";
                var result = ParseParen(NewVirtualToken(mToken, close));

                // Disallow old style casts (and other)
                if (close == ")" && (mTokenName == "(" || mToken.Type == eTokenType.Identifier))
                {
                    var message = "Old style cast not allowed";
                    RejectToken(mPrevToken, message);
                    Reject(message);
                }
                return result;
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
        /// Parse interpolated string: "string" expr "continue"
        /// Prefix may be null, or 'tr'.  Next token must be quoted string.
        /// </summary>
        SyntaxExpr ParseStringLiteral(Token prefix)
        {
            const string STR_REPLACE = "`~";

            var quote = mToken;
            var str = "";
            bool isInterpolated = false;
            while (mToken.Type == eTokenType.Quote
                   || mToken.Type == eTokenType.Identifier
                   || mToken == "("
                   || mToken == "\\")
            {
                if (mToken.Type == eTokenType.Quote)
                {
                    // Quoted string
                    var s = mTokenName.Substring(1, Math.Max(0, mTokenName.Length - 2));
                    str += s;
                    if (s.Contains(STR_REPLACE))
                    {
                        // TBD: Only do this when string is interpolated
                        mToken.AddError($"Interpolated string literals may not contain {STR_REPLACE}");
                    }
                    Accept();
                }
                else if (mToken == "(" || mToken.Type == eTokenType.Identifier)
                {
                    isInterpolated = true;
                    str += STR_REPLACE;
                    ParsePrimary();
                }
                else if (mToken == "\\")
                {
                    // cr, lf, crlf, tab, etc.
                    mToken.Type = eTokenType.Reserved;
                    Accept();
                    if (sStringLiterals.Contains(mTokenName))
                    {
                        mToken.Type = eTokenType.Reserved;
                        str += sStringLiterals[mTokenName];
                        Accept();
                    }
                    else if (mToken.Type == eTokenType.Number)
                    {
                        // Decimal escape
                        mToken.Type = eTokenType.Reserved;
                        Accept();
                        // TBD: Add decimal escape sequence
                    }
                    else if (mToken.Type == eTokenType.Identifier && mTokenName[0] == 'x')
                    {
                        // Hexadecimal escape
                        mToken.Type = eTokenType.Reserved;
                        Accept();
                        // TBD: Add hex escape sequence
                    }
                    else
                    {
                        RejectToken(mToken, "Expecting string literal escape, 'lf', 'cr', 'tab', etc.");
                    }
                }
                else
                {
                    throw new Exception("Error parsing '" + mTokenName + "' in string literal");
                }
            }
            // TBD: Show escape sequences, etc.
            quote.AddInfo(str.Replace(STR_REPLACE, "{}")
                .Replace("\r\n", "{\\crlf}")
                .Replace("\r", "{\\cf}")
                .Replace("\n", "{\\lf}")
                .Replace("\t", "{\\tab}"));

            if (isInterpolated)
            {
                if (mTokenName == "+")
                    RejectToken(mToken, "Cannot use '+' to append to an interpolated string literal, must add panentheses");
                else if (sCompareOps.Contains(mTokenName))
                    RejectToken(mToken, "Cannot directly compare interpolated string, must add parentheses");
            }

            return new SyntaxUnary(NewVirtualToken(quote, "\""), new SyntaxToken(new Token(str)));
        }

        SyntaxExpr ParseParen(Token keyword, bool isFuncCall = false, SyntaxExpr first = null)
        {
            var parameters = NewExprList();
            if (first != null)
                parameters.Add(first);
            ParseParen(parameters, isFuncCall);
            return new SyntaxMulti(keyword, FreeExprList(parameters));
        }

        /// <summary>
        /// Read the open '(' or '[' and then parse the parameters into parameters
        /// </summary>
        void ParseParen(List<SyntaxExpr> parameters, bool isFuncCall)
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '(' or '[' while parsing parameters");

            // Parse parameters
            var expectedToken = openToken == "(" ? ")" : "]";
            if (mTokenName != expectedToken)
            {
                parameters.Add(isFuncCall ? ParseFuncCallParameter() : ParseExpr());
                while (AcceptMatch(","))
                {
                    Connect(openToken, mPrevToken);
                    parameters.Add(isFuncCall ? ParseFuncCallParameter() : ParseExpr());
                }
            }

            if (AcceptMatchOrReject(expectedToken, "or ','"))
                Connect(openToken, mPrevToken);
        }

        SyntaxExpr ParseFuncCallParameter()
        {
            // Allow 'ref' or 'out' qualifier
            if (mTokenName == "out" || mTokenName == "ref")
            {                 
                var qualifier = Accept();
                if (mTokenName == "mut" /* || mTokenName == "var" */ || mToken == "@")
                {
                    var keyword = Accept();
                    if (keyword == "mut")
                    {
                        if (!AcceptMatch("@"))
                            RejectToken(mToken, "Expecting '@'");
                    }
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

        SyntaxExpr ParseTypeName(bool required = true)
        {
            // TBD: Some of these are really qualifiers
            if (mToken == "?" || mToken == "mut" || mToken == PTR
                || mToken == "ref" || mToken == "in" || mToken == "out" || mToken == "ro"
                || mToken == "[")
            {
                var token = Accept();
                if (token.Type != eTokenType.Reserved)
                    token.Type = eTokenType.TypeName;
                if (token.Name == "[")
                    if (AcceptMatchOrReject("]"))
                        mPrevToken.Type = eTokenType.TypeName;
                return new SyntaxUnary(token, ParseTypeName());
            }

            if (mToken == "fun" || mToken == "afun")
            {
                // Type arguments
                var tokenFun = Accept();
                tokenFun.Type = eTokenType.Reserved;
                var typeArgs = ParseTypeArgumentList("(");

                // Return type
                var funReturnType = ParseTypeName(false);

                return new SyntaxBinary(tokenFun, typeArgs,  funReturnType);
            }

            if (AcceptMatch("class") || AcceptMatch("struct"))
            {
                return ParseAnonymousClass(mPrevToken);
            }

            if (required && mToken.Type != eTokenType.Identifier)
            {
                ParseIdentifier("Expecting a type name", out var typeNameX);
                return new SyntaxError(mToken);
            }
            if (mToken.Type != eTokenType.Identifier)
                return new SyntaxToken(Token.Empty);

            var typeName = Accept();
            typeName.Type = eTokenType.TypeName;
            SyntaxExpr result = new SyntaxToken(typeName);

            bool accepted;
            do
            {
                accepted = false;
                if (AcceptMatch("."))
                {
                    accepted = true;
                    var dotToken = mPrevToken;
                    if (!ParseIdentifier("Expecting a type name", out var dotTypeName))
                        return new SyntaxError(mToken);
                    dotTypeName.Type = eTokenType.TypeName;
                    result = new SyntaxBinary(dotToken, result, new SyntaxToken(dotTypeName));
                }

                if (mToken == "<")
                {
                    accepted = true;
                    var openTypeToken = mToken;
                    var typeArgs = ParseTypeArgumentList();
                    result = new SyntaxBinary(NewVirtualToken(openTypeToken, VT_TYPE_ARG_LIST), result, typeArgs);
                }
            } while (accepted);
            return result;
        }

        /// <summary>
        /// Try parsing a type argument list. 
        /// Error causes reject until errorStop unless errorStop is null,
        /// in which case error checking is not performed. 
        SyntaxExpr ParseTypeArgumentList(string openSymbol = "<")
        {
            if (!AcceptMatchOrReject(openSymbol))
                return new SyntaxError(mToken);

            List<SyntaxExpr> typeArgs = new List<SyntaxExpr>();
            var closeSymbol = openSymbol == "<" ? ">" : ")";
            var openToken = mPrevToken;
            if (mTokenName != closeSymbol)
            {
                do
                {
                    Connect(openToken, mPrevToken);
                    typeArgs.Add(ParseTypeName());
                } while (AcceptMatch(","));
            }

            if (!AcceptMatch(closeSymbol))
            {
                FreeExprList(typeArgs);
                Reject("Expecting '>' to end the type argument list");
                return new SyntaxError();
            }
            Connect(openToken, mPrevToken);
            return new SyntaxMulti(NewVirtualToken(openToken, VT_TYPE_ARG_LIST), FreeExprList(typeArgs));
        }

        SyntaxExpr ParseAnonymousClass(Token keyword)
        {
            keyword.Type = eTokenType.Reserved;
            if (!AcceptMatchOrReject("("))
                return new SyntaxError(keyword);
            var open = mPrevToken;

            var fields = NewExprList();
            if (mTokenName != ")")
            {
                do
                {
                    fields.Add(ParseNewVarStatment(false));
                } while (AcceptMatch(","));
            }

            if (AcceptMatchOrReject(")"))
                Connect(mPrevToken, open);
            return new SyntaxMulti(keyword, FreeExprList(fields));
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

        bool AcceptMatchSkipInvisibleSemicolon(string match)
        {
            // Eat the invisible ";" if the token after it matches
            if (mToken.Invisible && mTokenName == ";" && mTokenAfterInsertedToken != null && mTokenAfterInsertedToken.Name == match)
                Accept();
            return AcceptMatch(match);
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
            public int ParseErrors;
            public int ExtraTokenCount;
        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.LexerEnum = mLexerEnum;
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            p.TokenType = mToken.Type;
            p.Inserted = mTokenAfterInsertedToken;
            p.ParseErrors = mParseErrors;
            p.ExtraTokenCount = mExtraTokens.Count;
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mLexerEnum = p.LexerEnum;
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mToken.Type = p.TokenType;
            mTokenName = mToken.Name;
            mTokenAfterInsertedToken = p.Inserted;
            mParseErrors = p.ParseErrors;
            while (mExtraTokens.Count > p.ExtraTokenCount)
                mExtraTokens.RemoveAt(mExtraTokens.Count-1);
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
        // Returns the token that was accepted.  Token type is set.
        Token Accept()
        {
            // Already at end of file?
            if (mTokenName == "")
                return mToken;

            var prevToken = mToken;
            mPrevToken = prevToken;
            if (mTokenAfterInsertedToken != null)
            {
                mToken = mTokenAfterInsertedToken;
                mTokenName = mToken.Name;
                mTokenAfterInsertedToken = null;
                return mPrevToken;
            }

            // Read next token (use "" as EOF flag)
            GetNextToken();

            if (mTokenName == "//" || mTokenName == "///")
                ParseComments();

            // Set token type
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
            else if (sReservedWords.Contains(mTokenName))
                mToken.Type = eTokenType.Reserved;
            else if (char.IsLetter(mTokenName[0]) || mTokenName[0] == '_')
                mToken.Type = eTokenType.Identifier;
            else if (mTokenName == ":")
                mToken.Type = eTokenType.BoldSymbol;
            else
                mToken.Type = eTokenType.Normal;

            if (mToken.OnlyTokenOnLine && (mTokenName == "{" || mTokenName == "}"))
                mToken.Shrink = true;

            if (mTokenName.Length != 0 &&  (mTokenName[0] == '_' || mTokenName[mTokenName.Length-1] == '_') && mTokenName != "_")
                RejectToken(mToken, "Identifiers may not begin or end with '_'");

            if (mTokenName == "__pfail")
                throw new Exception("Parse fail test");

            // Insert a ';' after each new line
            if (prevToken.Y != mToken.Y
                    && (!sEndLineSkipSemicolon.Contains(prevToken) 
                            || sBeginLineForceSemicolon.Contains(mTokenName))
                    && !(sBeginLineSkipSemicolon.Contains(mTokenName) 
                            || mTokenName.StartsWith("\"")
                            || mTokenName.StartsWith("`")))
            {
                // Mark at end of token, before any comment
                int x = prevToken.X + prevToken.Name.Length;

                mTokenAfterInsertedToken = mToken;
                mToken = new Token(";", x, prevToken.Y, eTokenBits.Invisible);
                mTokenName = ";";
            }
            return mPrevToken;
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

        // Lowest level function for getting the next token.  Ensure there are
        // no tabs and no space or `;` at then end of the line.
        void GetNextToken()
        {
            if (!mLexerEnum.MoveNext())
                return;

            mToken = mLexerEnum.Current;
            mToken.Clear();
            mTokenName = mToken.Name;

            // Check for tabs or white space at end of line
            if (mToken.Boln)
            {
                // Illegal tabs
                var line = mLexer.GetLine(mToken.Y);
                var i = line.IndexOf('\t');
                while (i >= 0)
                {
                    RejectToken(new Token(" ", i, mToken.Y, eTokenBits.Invisible),
                        "Illegal tab");
                    i = line.IndexOf('\t', i + 1);
                }
                // ; at end of line
                if (line.Length != 0 && line[line.Length - 1] == ';')
                {
                    RejectToken(new Token(" ", line.Length - 1, mToken.Y, eTokenBits.Invisible),
                        "Illegal semi-colon at end of line");
                }
                // Space at end of line (don't record error if our cursor is ther)
                if (line.Length != 0 && char.IsWhiteSpace(line[line.Length - 1]) && mLexer.Cursor.Y != mToken.Y)
                {
                    RejectToken(new Token(" ", line.Length - 1, mToken.Y, eTokenBits.Invisible),
                        "Illegal white space at end of line");
                }
            }
        }


        // Reject the given token
        public void RejectToken(Token token, string errorMessage)
        {
            mParseErrors++;
            token.AddError(errorMessage);
            RecordInvisibleToken(token);
        }

        // Call this after adding info to an invisible token
        public void RecordInvisibleToken(Token token)
        {
            // Make sure invisible tokens with errors are recorded
            if (token.Invisible)
            {
                if (!mExtraTokens.Contains(token))
                    mExtraTokens.Add(token);
            }
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
            var virtualToken = new Token(text, connectedToken.X, connectedToken.Y, eTokenBits.Invisible);
            Connect(connectedToken, virtualToken);
            return virtualToken;
        }


    }

}
