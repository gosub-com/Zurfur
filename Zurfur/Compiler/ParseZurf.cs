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
        public const string VT_TYPE_ARG = "$"; // Differentiate from '<' (must be 1 char long)
        // TBD: Allow pragmas to be set externally
        static WordSet sPragmas = new WordSet("ShowParse ShowMeta ShowSemi NoParse");


        ParseZurfCheck mZurfParseCheck;

        int                 mParseErrors;	// Number of errors
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken = new Token(";");
        Token               mPrevToken = new Token(";");
        StringBuilder       mComments = new StringBuilder();
        SyntaxField         mLastField;
        List<Token>         mMetaTokens = new List<Token>();
        int                 mTernaryLevel;
        Token               mTokenAfterInsertedToken;
        bool                mShowMeta;
        bool                mShowSemi;

        // Be kind to GC
        Queue<List<SyntaxExpr>>   mExprCache = new Queue<List<SyntaxExpr>>();

        string mNamespaceBaseStr = "";
        string[] mNamespaceBasePath = Array.Empty<string>();
        string[] mNamePath = new string[0];
        SyntaxFile mSyntax;

        public int ParseErrors => mParseErrors;

        // Add semicolons to all lines, except for:
        static WordSet sEndLineSkipSemicolon = new WordSet("{ [ ( ,");
        static WordSet sBeginLineForceSemicolon = new WordSet("} namespace pub fun afun extern imp static");
        static WordSet sBeginLineSkipSemicolon = new WordSet("\" { [ ] ( ) , . + - * / % | & || && == != "
                            + ": ? > << <= < => -> .. :: !== ===  is in as");

        static WordSet sReservedWords = new WordSet("abstract as base break case catch class const "
            + "continue default delegate do then else enum event explicit extern true false defer use "
            + "finally fixed for goto if implicit in interface internal is lock namespace module include "
            + "new null operator out override pub public private protected readonly ro ref dref mut "
            + "return unsealed unseal sealed sizeof stackalloc heapalloc static struct switch this throw try "
            + "typeof type unsafe using static virtual while dowhile asm managed unmanaged "
            + "async await astart func afunc get set aset aget global partial var where nameof "
            + "box boxed init move copy clone drop error dispose own owned "
            + "trait extends implements implement impl imp union fun afun def yield let cast "
            + "any dyn loop select match event from to of on cofun cofunc global local val it throws atask task "
            + "scope assign @ # and or not xor with cap exit pragma");

        static WordSet sClassFieldQualifiers = new WordSet("pub public protected private internal unsafe "
            + "static unsealed abstract virtual override new ro");
        static WordSet sPostFieldQualifiers = new WordSet("pub protected init set get mut");

        static WordSet sReservedUserFuncNames = new WordSet("new drop cast default implicit");
        static WordSet sReservedIdentifierVariables = new WordSet("null this true false default base match it new cast dref");
        static WordSet sTypeQualifiers = new WordSet("in out");
        static WordSet sTypeSymbols = new WordSet("? * ^ [ ref mut");

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sAllowConstraintKeywords = new WordSet("unmanaged");

        public static WordSet sOverloadableOps = new WordSet("+ - * / % [ in");
        static WordSet sCompareOps = new WordSet("== != < <= > >= === !== in");
        static WordSet sRangeOps = new WordSet(".. ::");
        static WordSet sAddOps = new WordSet("+ - |");
        static WordSet sXorOps = new WordSet("~");
        static WordSet sMultiplyOps = new WordSet("* / % &");
        static WordSet sAssignOps = new WordSet("= += -= *= /= %= |= &= ~= <<= >>=");
        static WordSet sUnaryOps = new WordSet("+ - ! & ~ use unsafe ref");
        static WordSet sAllowedAfterInterpolatedString = new WordSet("; , ) ] }");
        static WordSet sNoSubCompoundStatement = new WordSet("if else while for do switch scope");

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // This seems stange because something like `a = F<T1,T2>;` is not a valid expression
        // The following symbols allow us to call functions, create types, access static members, and cast
        // For example `F<T1>()` to call a function or constructor, `F<T1>.Name` to access a static or member,
        // and #F<T1>(expression) to cast.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) .");

        static WordSet sStatementEndings = new WordSet("; => }");
        static WordSet sStatementsDone = new WordSet("} namespace class struct interface enum", true);
        static WordSet sRejectAnyStop = new WordSet("=> ; { }", true);
        static WordSet sRejectForCondition = new WordSet("in");
        static WordSet sRejectFuncName = new WordSet("(");
        static WordSet sRejectFuncParam = new WordSet(", )");
        static WordSet sRejectTypeName = new WordSet("( )");

        WordSet sStringLiteralEscapes = new WordSet("{ \" }");
        static WordMap<string> sStringLiterals = new WordMap<string>()
            { { "n", "\n" }, { "r", "\r"}, {"rn", "\r\n"}, {"t", "\t"}, {"b", "\b" } };

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public ParseZurf(Lexer lexer)
        {
            mLexer = lexer;
            mLexerEnum = new Lexer.Enumerator(lexer);
            mSyntax = new SyntaxFile();
            mSyntax.Lexer = lexer;
            mZurfParseCheck = new ParseZurfCheck(this);
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
            mLexer.EndToken.Clear();
            if (Debugger.IsAttached)
            {
                // Failure causes error in dubugger
                ParseCompilationUnit();
                mZurfParseCheck.Check(mSyntax);
                mLexer.MetaTokens = mMetaTokens.ToArray();
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
                    mLexer.MetaTokens = mMetaTokens.ToArray();
                }
            }
            if (mTokenName != "")
                RejectToken(mToken, "Parse error: Expecting end of file");
            while (mTokenName != "")
                Accept();

            mLexer.ShowMetaTokens = mShowSemi || mShowMeta;

            return mSyntax;
        }
        class NoCompilePragmaException : Exception { }

        /// <summary>
        /// Parse the file
        /// </summary>
        void ParseCompilationUnit()
        {
            try
            {
                Accept();
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
            catch (NoCompilePragmaException)
            {

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
                while (AcceptMatch("#"))
                {
                    if (CheckIdentifier("Expecting an attribute"))
                        attributes.Add(ParseExpr());
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
                switch (mTokenName)
                {
                    case ";":
                        if (qualifiers.Count != 0)
                            RejectToken(keyword, "Expecting a class/struct/fun/prop, etc. or field definition");
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
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'use' statement");
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
                    //case "type":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        while (AcceptMatch("ref") || AcceptMatch("ro") || AcceptMatch("boxed"))
                            qualifiers.Add(mPrevToken);
                        ParseClass(keyword, parentScope, qualifiers);
                        break;

                    case "fun":
                    case "afun":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        keyword.Type = eTokenType.ReservedControl;  // Fix keyword to make it control
                        AddMethod(ParseMethod(keyword, parentScope, qualifiers));
                        break;

                    case "prop":
                    case "aprop":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        ParseProperty(keyword, parentScope, qualifiers);
                        break;

                    case "@":
                        ParseFieldFull(Accept(), parentScope, qualifiers);
                        break;

                    case "const":
                        qualifiers.Add(Accept());
                        AddField(ParseFieldSimple(parentScope, qualifiers));
                        break;

                    default:
                        if (parentScope != null && parentScope.Keyword == "enum" && keyword.Type == eTokenType.Identifier)
                        {
                            // For enum, assume the first identifier is a field
                            Accept();
                            AddField(ParseEnumField(parentScope, qualifiers, keyword));
                        }
                        else
                        {
                            Accept();
                            RejectToken(keyword, "Expecting a field, function, property, struct ('@', 'fun', 'prop', 'struct', 'const', etc.) or qualifier ('pub', etc.)");
                        }
                        break;
                }
                AcceptSemicolonOrReject();
            }
        }

        void AddType(SyntaxType type)
        {
            if (type == null)
                return; // Error already marked while parsing definition
            if (mNamespaceBasePath.Length == 0)
            {
                RejectToken(type.Name, "The namespace must be defined before the method");
                return;
            }
            mSyntax.Types.Add(type);
        }
        void AddField(SyntaxField field)
        {
            if (field == null)
                return; // Error already marked while parsing definition
            if (mNamespaceBasePath.Length == 0)
            {
                RejectToken(field.Name, "The namespace must be defined before the field");
                return;
            }
            mSyntax.Fields.Add(field);
        }

        void AddMethod(SyntaxFunc method)
        {
            if (method == null)
                return; // Error already marked while parsing definition
            if (mNamespaceBasePath.Length == 0)
            {
                RejectToken(method.Name, "The namespace must be defined before the method");
                return;
            }
            mSyntax.Methods.Add(method);
        }

        // Reject tokens with errorMessage.  Reject all of them if acceptSet is null.
        void RejectQualifiers(List<Token> qualifiers, string errorMessage)
        {
            if (qualifiers.Count != 0)
                RejectToken(qualifiers[qualifiers.Count-1], errorMessage);
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
                }
            }
            // Reject if not full prefix
            if (namePath.Count < mNamespaceBasePath.Length)
            {
                RejectToken(namePath[namePath.Count-1], "Expecting namespace to start with '" + mNamespaceBaseStr + "'");
            }

            // Collect base namespace
            mNamePath = namePath.ConvertAll(token => token.Name).ToArray();
            var namePathStr = string.Join(".", mNamePath);
            if (mNamespaceBasePath.Length == 0)
            {
                mNamespaceBasePath = mNamePath;
                mNamespaceBaseStr = namePathStr;
            }
            if (!mSyntax.Namespaces.TryGetValue(namePathStr, out var ns))
            {
                ns = new SyntaxNamespace();
                mSyntax.Namespaces[namePathStr] = ns;
            }
            // Accumulate comments and keyword tokens for this namespace
            ns.Comments += " " + mComments;
            ns.Path = namePath.ToArray();
        }

        // Parse class, struct, interface, or enum
        void ParseClass(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            var synClass = new SyntaxType(keyword);
            synClass.Qualifiers = qualifiers.ToArray();
            synClass.ParentScope = parentScope;
            synClass.NamePath = mNamePath;
            synClass.Comments = mComments.ToString();

            // Parse class name and type parameters
            (synClass.Name, synClass.TypeArgs) = GetSimpleNameWithTypeArgs(ParseTypeName());
            if (synClass.Name.Type != eTokenType.TypeName)
                return; // TBD: Could try to recover (user probably editing class name)

            AddType(synClass);

            if (AcceptMatch("="))
            {
                synClass.Alias = ParseType();
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
                    var simpleField = ParseFieldSimple(synClass, new List<Token>());
                    if (simpleField != null)
                    {
                        simpleField.Simple = true;
                        AddField(simpleField);
                    }
                } while (AcceptMatch(","));
                if (AcceptMatchOrReject(")"))
                    Connect(mPrevToken, open);
            }

            if (AcceptMatchSkipMetaSemicolon("extends"))
                synClass.Extends = ParseType();

            // Parse implemented classes
            if (AcceptMatchSkipMetaSemicolon("implements"))
            {
                var baseClasses = NewExprList();
                baseClasses.Add(ParseType());
                while (AcceptMatch(","))
                {
                    baseClasses.Add(ParseType());
                }
                synClass.Implements = FreeExprList(baseClasses);
            }

            synClass.Constraints = ParseConstraints();

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

        private SyntaxConstraint[] ParseConstraints()
        {
            List<SyntaxConstraint> constraints = new List<SyntaxConstraint>();
            while (AcceptMatchSkipMetaSemicolon("where"))
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
                constraintTypeNames.Add(ParseType());
            } while (AcceptMatch(","));
            constraint.TypeNames = FreeExprList(constraintTypeNames);
            return constraint;                       
        }

        /// <summary>
        /// Current token must already be checked for validity
        /// </summary>
        SyntaxField ParseFieldSimple(SyntaxScope parentScope, List<Token> qualifiers)
        {
            var field = new SyntaxField(mToken);
            field.ParentScope = parentScope;
            field.NamePath = mNamePath;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            mLastField = field; // Allow us to pick up comments on this line

            if (!ParseIdentifier("Expecting field name", out var newVarName))
                return null;
            newVarName.Type = eTokenType.DefineField;

            if (mTokenName != "=")
                field.TypeName = ParseType();

            if (mTokenName == "=")
                field.Initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

            return field;
        }

        private void ParseFieldFull(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            // Variable name
            if (!ParseIdentifier("Expecting field name", out var newVarName))
                return;
            newVarName.Type = eTokenType.DefineField;

            // Type name
            var errors = mParseErrors;
            var typeName = ParseType();
            if (mParseErrors != errors)
                return;

            // Post field qualifiers
            if (keyword != "const")
                while (sPostFieldQualifiers.Contains(mTokenName))
                    //qualifiers.Add(Accept());
                    Accept();

            // Initializer
            SyntaxExpr initializer = null;
            if (mTokenName == "=")
                initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

            var field = new SyntaxField(newVarName);
            field.ParentScope = parentScope;
            field.NamePath = mNamePath;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            field.TypeName = typeName;
            field.Initializer = initializer;
            mLastField = field; // Allow us to pick up comments on this line
            AddField(field);
        }


        /// <summary>
        /// Current token must already be checked for validity
        /// </summary>
        SyntaxField ParseEnumField(SyntaxScope parentScope, List<Token> qualifiers, Token name)
        {
            var field = new SyntaxField(name);
            field.ParentScope = parentScope;
            field.NamePath = mNamePath;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
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

            var typeName = ParseType();
            if (mToken.Error)
                return;

            var synFunc = new SyntaxFunc(keyword);
            synFunc.IsProperty = true;
            synFunc.ParentScope = parentScope;
            synFunc.NamePath = mNamePath;
            synFunc.Comments = mComments.ToString();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Name = propertyName;
            synFunc.MethodSignature = typeName;

            // Body
            if (AcceptMatchSkipMetaSemicolon("extern") || AcceptMatchSkipMetaSemicolon("imp"))
            {
                qualifiers.Add(mPrevToken);
                if (mPrevToken.Name == "imp")
                {
                    if (mTokenName == "get")
                    {
                        qualifiers.Add(Accept());
                        if (mTokenName == "set")
                            qualifiers.Add(Accept());
                    }
                    else
                    {
                        RejectToken(mToken, "Expecting 'get'");
                    }
                }
            }
            else if (mTokenName == "{" || IsMatchPastMetaSemicolon("return"))
            {
                synFunc.Statements = ParseCompoundStatement(keyword, false);
            }
            else
            {
                Reject("Expecting property body '{', 'return', 'extern', or 'imp'");
                if (mTokenName == "{")
                    synFunc.Statements = ParseCompoundStatement(keyword);
            }

            AddMethod(synFunc);
        }


        /// <summary>
        /// Func, construct, operator
        /// </summary>
        SyntaxFunc ParseMethod(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc(keyword);
            synFunc.ParentScope = parentScope;
            synFunc.NamePath = mNamePath;
            synFunc.Comments = mComments.ToString();

            // Parse method name: operator, reserved, or extension.name<args...>
            var validMethodName = false;
            SyntaxExpr typeArgs;
            if (AcceptMatch("operator"))
            {
                // Operator
                var operatorKeyword = mPrevToken;
                typeArgs = new SyntaxToken(mLexer.EndToken);
                if (sOverloadableOps.Contains(mTokenName))
                {
                    var operatorName = Accept();
                    if (operatorName.Name == "[")
                        AcceptMatchOrReject("]");
                    synFunc.Name = NewVirtualToken(operatorKeyword, operatorKeyword + operatorName);
                    validMethodName = true;
                }
                else
                {
                    Reject("Expecting an overloadable operator", sRejectFuncName);
                    synFunc.Name = operatorKeyword;
                }
            }
            else if (sReservedUserFuncNames.Contains(mTokenName))
            {
                // Reserved function
                synFunc.Name = Accept();
                typeArgs = new SyntaxToken(mLexer.EndToken);
                validMethodName = true;
            }
            else
            {
                // Regular function name: extension.name<args...>
                validMethodName = ParseMethodName(out synFunc.ExtensionType, out synFunc.Name, out typeArgs);
            }
            synFunc.TypeArgs = typeArgs;


            if (mTokenName == "^")
                qualifiers.Add(Accept());
            if (mTokenName == "mut")
                qualifiers.Add(Accept());
            if (mTokenName == "ref")
                qualifiers.Add(Accept());

            synFunc.MethodSignature = ParseMethodSignature(keyword, true);

            synFunc.Constraints = ParseConstraints();

            // Body
            if (AcceptMatchSkipMetaSemicolon("extern") || AcceptMatchSkipMetaSemicolon("imp"))
            {
                qualifiers.Add(mPrevToken);
            }
            else if (mTokenName == "{" || IsMatchPastMetaSemicolon("return"))
            {
                synFunc.Statements = ParseCompoundStatement(keyword, false);
            }
            else
            {
                Reject("Expecting property body '{', 'return', 'extern', or 'imp'");
                if (mTokenName == "{")
                    synFunc.Statements = ParseCompoundStatement(keyword);
            }

            synFunc.Qualifiers = qualifiers.ToArray();
            
            if (!validMethodName)
                return null;
            return synFunc;
        }

        /// <summary>
        /// returns SyntaxExpr:
        ///     [0] - Parameters (name, type)
        ///     [1] - Returns (name, type) possibly blank for each
        ///     [2] - error/exit token
        /// </summary>
        private SyntaxExpr ParseMethodSignature(Token keyword, bool allowInitializer)
        {
            var funcParams = ParseMethodParams(allowInitializer);

            if (mTokenName == "class" || mTokenName == "struct")
                RejectToken(mToken, "Use '->' instead of anonymous class");

            SyntaxExpr returnParams;
            if (AcceptMatchSkipMetaSemicolon("->"))
                returnParams = ParseMethodParams(false);
            else
                returnParams = new SyntaxUnary(mLexer.EndToken, new SyntaxUnary(mLexer.EndToken, ParseTypeWithQualifiers(false)));

            SyntaxToken qualifier;
            if (mTokenName == "error" || mTokenName == "exit")
                qualifier = new SyntaxToken(Accept());
            else
                qualifier = new SyntaxToken(mLexer.EndToken);

            return new SyntaxMulti(keyword, funcParams, returnParams, qualifier);
        }

        /// <summary>
        /// Returns true if we are a valid method name
        /// </summary>
        bool ParseMethodName(out SyntaxExpr className, out Token funcName, out SyntaxExpr typeArgs)
        {
            className = null;
            typeArgs = new SyntaxToken(mLexer.EndToken);

            funcName = mToken;
            if (!CheckIdentifier("Expecting a function or type name", sRejectTypeName))
                return false;

            // The last type name after the "." is the function name and type args.
            // Everything before that is the extension method class.
            var result = ParseTypeName();
            Token prevDot = null;
            while (mTokenName == ".")
            {
                var nextDot = Accept();
                if (prevDot == null)
                    className = result;
                else
                    className = new SyntaxBinary(prevDot, className, result);
                prevDot = nextDot;

                if (!CheckIdentifier("Expecting a function or type name", sRejectTypeName))
                    return false;

                result = ParseTypeName();
            }

            (funcName, typeArgs) = GetSimpleNameWithTypeArgs(result);
            var validMethodName = funcName.Type == eTokenType.TypeName;
            if (validMethodName)
                funcName.Type = eTokenType.DefineMethod;
            return validMethodName;
        }

        /// <summary>
        /// Get a simple name with type arguments, anything else is marked with an error.
        /// No sub-embedded types, just this: TypeName<Arg1, Arg2, ...>
        /// RETURNS: (name, type args)
        /// </summary>
        (Token, SyntaxExpr) GetSimpleNameWithTypeArgs(SyntaxExpr type)
        {
            // Type name, no type args
            if (type.Count == 0 || type.Token != VT_TYPE_ARG)
            {
                if (type.Token.Type != eTokenType.TypeName)
                    RejectToken(type.Token, "Expecting a type name identifier");
                if (type.Count != 0)
                    RejectToken(type[0].Token, "Unexpected sub type");
                return (type.Token, new SyntaxToken(mLexer.EndToken));
            }
            // Type args, verify no other stuff in there
            var typeArgList = NewExprList();
            bool first = true;
            foreach (var typeArg in type)
            {
                if (typeArg.Count != 0)
                    RejectToken(typeArg.Token, "Parameter list may not include type parameters or member access");
                else if (typeArg.Token.Type != eTokenType.TypeName)
                    RejectToken(typeArg.Token, "Expecting a type name identifier");
                else if (!first)
                    typeArgList.Add(typeArg);
                first = false;
            }
            return (type[0].Token, new SyntaxMulti(type.Token, FreeExprList(typeArgList)));
        }

        SyntaxExpr ParseMethodParams(bool allowInitializer)
        {
            // Read open token, '('
            if (!AcceptMatchOrReject("("))
                return new SyntaxError();

            // Parse parameters
            var openToken = mPrevToken;
            var parameters = NewExprList();
            if (mTokenName != ")")
                parameters.Add(ParseMethodParam(allowInitializer));
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseMethodParam(allowInitializer));
            }

            // Ellipse to signify repeated parameters
            //if (AcceptMatch("..."))
            //    mPrevToken.AddWarning("Repeated parameters not supported yet");

            if (AcceptMatchOrReject(")", " or ','"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(mLexer.EndToken, FreeExprList(parameters));
        }

        SyntaxExpr ParseMethodParam(bool allowInitializer)
        {
            if (!ParseIdentifier("Expecting a variable name", out var name, sRejectFuncParam))
                return new SyntaxError();
            name.Type = eTokenType.DefineParam;
            var type = ParseTypeWithQualifiers();

            // Default parameter
            if (allowInitializer && AcceptMatch("="))
            {
                return new SyntaxBinary(name, type, ParseExpr());
            }
            return new SyntaxUnary(name, type);
        }
        
        SyntaxExpr ParseCompoundStatement(Token keyword, bool requireNextLine = true)
        {
            if (mTokenName == "{")
                return ParseStatements("'" + keyword.Name + "' statement");

            // If on the invisible meta semi-colon
            if (mToken == ";" && mToken.Meta)
            {
                // Ensure next line is indented
                if (mTokenAfterInsertedToken.X < mLexer.GetLineTokens(keyword.Y)[0].X + 2)
                {
                    RejectToken(mToken, "Expecting '{' or next line of compound statement must be indented at least two spaces");
                    return new SyntaxError();
                }
                Accept();
            }
            else
            {
                if (requireNextLine)
                {
                    Reject("Expecting '{' or end of line");
                    return new SyntaxError();
                }
            }

            if (mTokenName == "}")
            {
                RejectToken(mPrevToken, "Expecting '{' or non-empty statement");
                return new SyntaxError();
            }
            else if (mTokenName == ";")
            {
                RejectToken(mToken, "Expecting '{' or non-empty statement");
                return new SyntaxError();
            }
            else if (sNoSubCompoundStatement.Contains(mTokenName))
            {
                RejectToken(mToken, "Compound statement may not embed a '" + mTokenName + "' statement");
                return new SyntaxError();
            }
            var semicolon = mPrevToken;

            var statement = NewExprList();
            ParseStatement(statement, false);

            if (mTokenName == ";" && !mToken.Meta)
                RejectToken(mToken, "Compound statement may not have another statement on the same line, use braces");
            else if (mTokenName != ";")
                RejectToken(mToken, "Expecting ';' or end of line");

            return new SyntaxMulti(NewVirtualToken(semicolon, "{"), FreeExprList(statement));
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
        private void ParseStatement(List<SyntaxExpr> statements, bool requireSemicolon = true)
        {
            var keyword = mToken;
            switch (mToken)
            {
                case ";":
                case "}":
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
                case "unsafe":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    break;

                case "@":
                case "const":
                    bool allowUnderscore = mTokenName == "@";
                    statements.Add(new SyntaxUnary(Accept(), ParseNewVarStatment(allowUnderscore)));
                    break;

                case "while":
                    // WHILE (condition) (body)
                    requireSemicolon = false;
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), 
                                    ParseCompoundStatement(keyword)));
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
                    requireSemicolon = false;
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    statements.Add(new SyntaxBinary(keyword, ParseExpr(),
                                    ParseCompoundStatement(keyword)));
                    break;

                case "else":
                    requireSemicolon = false;
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    if (AcceptMatch("if"))
                    {
                        mPrevToken.Type = eTokenType.ReservedControl;
                        statements.Add(new SyntaxBinary(keyword, ParseExpr(),
                                        ParseCompoundStatement(keyword)));
                    }
                    else
                    {
                        statements.Add(new SyntaxUnary(keyword,
                                        ParseCompoundStatement(keyword)));
                    }
                    break;

                case "for":
                    // FOR (variable) (condition) (statements)
                    requireSemicolon = false;
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    if (!AcceptMatch("@"))
                        RejectToken(mToken, "Expecting '@'");
                    var forVariable = new SyntaxToken(ParseIdentifier("Expecting a loop variable", sRejectForCondition));
                    forVariable.Token.Type = eTokenType.DefineLocal;
                    AcceptMatchOrReject("in");
                    var forCondition = ParseExpr();
                    statements.Add(new SyntaxMulti(keyword, forVariable, forCondition, 
                                    ParseCompoundStatement(keyword)));
                    break;

                case "throw":
                case "return":
                    keyword.Type = eTokenType.ReservedControl;
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
                    keyword.Type = eTokenType.ReservedControl;
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
                    if (keyword == "get" || keyword == "set")
                        keyword.Type = eTokenType.ReservedControl;
                    Accept();
                    AcceptMatchOrReject(":");
                    statements.Add(new SyntaxToken(keyword));
                    requireSemicolon = false;
                    break;

                case "error":
                case "catch":
                case "case":
                    Accept();
                    var caseExpressions = NewExprList();
                    if (keyword != "error" || mTokenName != ":")
                        caseExpressions.Add(ParseConditionalOr());
                    while (AcceptMatch(","))
                        caseExpressions.Add(ParseConditionalOr());
                    statements.Add(new SyntaxMulti(keyword, FreeExprList(caseExpressions)));
                    AcceptMatchOrReject(":", "or ','");
                    requireSemicolon = false;
                    break;

                case "fun":
                case "afun":
                case "func":
                case "afunc":
                    Accept();
                    keyword.Type = eTokenType.ReservedControl;  // Fix keyword to make it control
                    var synFunc = ParseMethod(keyword, null, new List<Token>());
                    if (synFunc != null)
                        statements.Add(synFunc);
                    break;

                default:
                    if ((sReservedWords.Contains(mTokenName) || mTokenName == "")
                        && !sReservedIdentifierVariables.Contains(mTokenName))
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
                typeName = ParseType();
            else
                typeName = new SyntaxToken(mLexer.EndToken);

            SyntaxExpr initializer;
            if (mTokenName == "=")
                initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());
            else
                initializer = new SyntaxToken(mLexer.EndToken);

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
            var result = sRangeOps.Contains(mTokenName) ? new SyntaxToken(mLexer.EndToken) : ParseAdd();
            if (sRangeOps.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, 
                    mTokenName == ")" || mTokenName == "]" ? new SyntaxToken(mLexer.EndToken) : ParseAdd());
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
                result = new SyntaxBinary(Accept(), result, ParseType());
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
                return ParseTypeFunc(true);
            }

            if (mTokenName == "sizeof" || mTokenName == "typeof")
            {
                return ParseTypeFunc(false);
            }

            if (mTokenName == "class" || mTokenName == "struct")
            {
                return ParseAnonymousClass(Accept());
            }

            return ParsePrimary();
        }


        private SyntaxExpr ParseTypeFunc(bool parseUnaryAfter)
        {
            var castToken = Accept();
            if (!AcceptMatchOrReject("("))
                return new SyntaxError(mToken);
            var castOpenToken = mPrevToken;
            var castType = ParseType();
            if (AcceptMatchOrReject(")"))
                Connect(mPrevToken, castOpenToken);
            if (parseUnaryAfter)
                return new SyntaxBinary(castToken, castType, ParseUnary());
            else
                return new SyntaxUnary(castToken, castType);
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
                // Allow parameterless lambda capture "@ => 0", but not parameterless capture "f()@ + 2"
                if (mTokenName != "=>" && ParseIdentifier("Expecting variable name or '=>'", out var varName))
                {
                    varName.Type = eTokenType.DefineLocal;
                    newVarList.Add(new SyntaxToken(mPrevToken));
                }
            }

            return new SyntaxMulti(mLexer.EndToken, FreeExprList(newVarList));
        }

        SyntaxExpr ParsePrimary()
        {
            var result = ParseAtom();

            // Primary: function call 'f()', array 'a[]', type argument f<type>
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
                else if (mTokenName == ".")
                {
                    accepted = true;
                    result = new SyntaxBinary(Accept(), result,
                        new SyntaxToken(ParseIdentifier("Expecting identifier")));
                }
                else if (mTokenName == "<")
                {
                    // Possibly a type argument list.  Let's try it and find out.
                    var typeArgIdentifier = mPrevToken;
                    var p = SaveParsePoint();
                    var openTypeToken = mToken;

                    mParseErrors = 0;
                    var typeArgs = ParseTypeArgumentList(result);
                    if (mParseErrors == 0 && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Yes, it is a type argument list.  Keep it
                        mParseErrors = p.ParseErrors;
                        if (typeArgIdentifier.Type != eTokenType.Reserved)
                            typeArgIdentifier.Type = eTokenType.TypeName;
                        accepted = true;
                        SetType(result);
                        result = typeArgs;
                    }
                    else
                    {
                        // Failed, restore the enumerator back to before trying type argument list
                        RestoreParsePoint(p);
                    }
                }
            } while (accepted);

            return result;

            // Type argument list as part of primary expression
            void SetType(SyntaxExpr expr)
            {
                expr.Token.Type = eTokenType.TypeName;
                foreach (var e in expr)
                    SetType(e);
            }

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
            if (mTokenName == "\"")
            {
                return ParseStringLiteral(null);
            }
            if (mToken.Type == eTokenType.Identifier)
            {
                var identifier = Accept();
                if (mTokenName == "\"" && identifier == "tr")
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
        /// Parse interpolated string: "string {expr} continue{\rn}"
        /// Prefix may be null, or 'tr'.  Next token must be quote symbol.
        /// TBD: Store "tr" in the parse tree
        /// </summary>
        SyntaxExpr ParseStringLiteral(Token prefix)
        {
            const string STR_REPLACE_TEMP = "\uE123\uF123";
            const string STR_REPLACE_FINAL = "{?}"; // TBD: Need something less common?

            var quote = mToken;
            var literalTokens = new List<Token>();
            var literalSb = new StringBuilder();
            var literalExpr = NewExprList();
            var isInterpolated = false;

            var scoopStartY = -1;
            var scoopStartX = -1;
            BeginScoop(mToken);
            literalTokens.Add(Accept());

            // Scoop until something ends the literal
            while (true)
            {
                if (mToken.Boln || mToken.Meta && mTokenName == ";")
                {
                    EndScoop(mToken);
                    RejectToken(mToken.Boln ? mPrevToken : mToken, "Expecting end quote before end of line");
                    if (AcceptMatchSkipMetaSemicolon("\""))
                    {
                        // Special case: continue string on next line even though this is an error
                        BeginScoop(mPrevToken);
                        continue;
                    }
                    break; // End quoted string
                }

                if (sStringLiteralEscapes.Contains(mToken))
                {
                    EndScoop(mToken);
                    Accept();
                    if (ParseDouble(mPrevToken.Name))
                    {
                        BeginScoop(mPrevToken);
                        continue;
                    }

                    if (mPrevToken.Name == "\"")
                    {
                        // Accept end quote, then continue on next line if possible
                        literalTokens.Add(mPrevToken);
                        if (mToken.Boln && AcceptMatch("\""))
                        {
                            BeginScoop(mPrevToken);
                            literalTokens.Add(mPrevToken);
                            continue;  // Continue quoted string on next line
                        }
                        break; // End quoted string
                    }

                    if (mPrevToken.Name == "{")
                    {
                        if (mTokenName == "\\")
                            ParseEscapes();
                        else if (mTokenName != "\"") // String not allowed in string (user is probably typing)
                            ParseInterpolatedExpression();

                        if (mTokenName != "}")
                            Reject("Expecting '}' to end string interpolation", new WordSet("} \""));
                        AcceptMatch("}");

                        BeginScoop(mPrevToken);
                        continue; // Continue quoted string
                    }
                    if (mPrevToken.Name == "}")
                    {
                        RejectToken(mPrevToken, "Expecting another '}' in string literal");
                        BeginScoop(mPrevToken);
                        continue;  // Continue parsing quoted string
                    }
                }

                literalTokens.Add(Accept());
            }
            EndScoop(mToken);

            // Check for error
            var str = literalSb.ToString();
            if (isInterpolated)
            {
                var containsReplacement = str.Contains(STR_REPLACE_FINAL);
                foreach (var token in literalTokens)
                {
                    if (containsReplacement && STR_REPLACE_FINAL.Contains(token.Name))
                        RejectToken(token, $"Interpolated string literals may not contain '{STR_REPLACE_FINAL}'");
                }
            }

            // Show final string
            str = str.Replace(STR_REPLACE_TEMP, STR_REPLACE_FINAL);
            var strPrint = "'" + str
                .Replace("\r\n", "{\\rn}")
                .Replace("\r", "{\\r}")
                .Replace("\n", "{\\n}")
                .Replace("\t", "{\\t}")
                .Replace("\b", "{\\b}")+ "'";

            foreach (var token in literalTokens)
            {
                if (token.Error)
                    continue;
                token.AddInfo(strPrint);
                token.Type = eTokenType.Quote;
            }

            return new SyntaxUnary(quote, new SyntaxMulti(new Token(str), FreeExprList(literalExpr)));

            // Called with beginning quote
            void BeginScoop(Token beginScoop)
            {
                scoopStartX = beginScoop.X + beginScoop.Name.Length;
                scoopStartY = beginScoop.Y;
            }

            // Called with end quote
            void EndScoop(Token endToken)
            {
                if (scoopStartX < 0)
                    return;
                var x = endToken.X;
                if (endToken.Y != scoopStartY)
                    x = mLexer.GetLine(scoopStartY).Length;
                var len = Math.Max(0, x - scoopStartX);
                literalSb.Append(mLexer.GetLine(scoopStartY).Substring(scoopStartX, len));
                scoopStartX = -1;
            }

            bool ParseDouble(string literalName)
            {
                if (mTokenName == literalName && !mToken.Boln)
                {
                    literalTokens.Add(mPrevToken);
                    literalSb.Append(literalName);
                    if (mPrevToken.X + 1 != mToken.X)
                        RejectToken(mToken, "Unexpected space before this token");
                    literalTokens.Add(Accept());
                    return true;
                }
                return false;
            }

            void ParseEscapes()
            {
                while (AcceptMatch("\\"))
                {
                    mPrevToken.Type = eTokenType.Reserved;
                    if (!sStringLiterals.Contains(mTokenName))
                    {
                        RejectToken(mToken, "Expecting string literal constant, 'r', 'n', 'rn', 't', etc.");
                        if (mToken.Type == eTokenType.Identifier)
                            Accept();
                        continue;
                    }

                    mToken.Type = eTokenType.Reserved;
                    literalSb.Append(sStringLiterals[mToken.Name]);
                    Accept();
                }
            }

            void ParseInterpolatedExpression()
            {
                isInterpolated = true;
                literalExpr.Add(ParseExpr());
                literalSb.Append(STR_REPLACE_TEMP);
            }

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

        SyntaxExpr ParseTypeWithQualifiers(bool required = true)
        {
            while (sTypeQualifiers.Contains(mTokenName))
            {
                mToken.AddWarning("Type qualifier not recorded in parse tree yet");
                Accept();
            }
            return ParseType(required);
        }

        SyntaxExpr ParseType(bool required = true)
        {
            // TBD: Some of these are really qualifiers
            if (sTypeSymbols.Contains(mTokenName))
            {
                var token = Accept();
                if (token.Type != eTokenType.Reserved)
                    token.Type = eTokenType.TypeName;
                if (token.Name == "[")
                    if (AcceptMatchOrReject("]"))
                        mPrevToken.Type = eTokenType.TypeName;
                return new SyntaxUnary(token, ParseType());
            }

            if (mToken == "fun" || mToken == "afun")
            {
                // Type arguments
                var funKeyword = mToken;
                funKeyword.Type = eTokenType.Identifier;
                var (typeName, typeArgs) = GetSimpleNameWithTypeArgs(ParseTypeName());
                funKeyword.Type = eTokenType.Reserved;
                foreach (var ta in typeArgs)
                    RejectToken(ta.Token, "Generic type args on lambda not supported YET!");
                return ParseMethodSignature(typeName, false);
            }

            if (AcceptMatch("class") || AcceptMatch("struct"))
            {
                return ParseAnonymousClass(mPrevToken);
            }

            if (required && mToken.Type != eTokenType.Identifier)
            {
                ParseIdentifier("Expecting a type name", out var typeNameX, sRejectTypeName);
                return new SyntaxError(mToken);
            }
            if (mToken.Type != eTokenType.Identifier)
                return new SyntaxToken(mLexer.EndToken);

            var result = ParseTypeName();
            while (mTokenName == ".")
                result = new SyntaxBinary(Accept(), result, ParseTypeName());
            return result;
        }

        /// <summary>
        /// Type name (starting with identifier) and optional type args:  TypeName<Arg...>
        /// </summary>
        SyntaxExpr ParseTypeName()
        {
            if (!ParseIdentifier("Expecting a type name", out var typeName, sRejectTypeName))
                return new SyntaxError(mToken);
            typeName.Type = eTokenType.TypeName;
            var result = (SyntaxExpr)new SyntaxToken(typeName);
            if (mTokenName == "<")
                result = ParseTypeArgumentList(result);
            return result;
        }

        /// <summary>
        /// Parse type argument list: <Arg...>
        /// </summary>
        SyntaxExpr ParseTypeArgumentList(SyntaxExpr left)
        {
            if (!AcceptMatchOrReject("<"))
                return new SyntaxError(mToken);
            var openToken = mPrevToken;

            List<SyntaxExpr> typeArgs = NewExprList();
            typeArgs.Add(left);
            typeArgs.Add(ParseType());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                typeArgs.Add(ParseType());
            }

            if (!AcceptMatch(">"))
            {
                FreeExprList(typeArgs);
                Reject("Expecting '>' to end the type argument list", sRejectTypeName);
                return left; // Allow syntax error recovery
            }
            Connect(openToken, mPrevToken);
            return new SyntaxMulti(NewVirtualToken(openToken, VT_TYPE_ARG), FreeExprList(typeArgs));
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

            if (CheckIdentifier(errorMessage, extraStops))
            {
                Accept();
                return true;
            }
            return false;
        }

        bool CheckIdentifier(string errorMessage, WordSet extraStops = null)
        {
            if (mToken.Type == eTokenType.Identifier)
                return true;

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

        bool IsMatchPastMetaSemicolon(string match)
        {
            return mTokenName == match
                || mToken.Meta && mTokenName == ";" && mTokenAfterInsertedToken != null && mTokenAfterInsertedToken.Name == match;
        }

        bool AcceptMatchSkipMetaSemicolon(string match)
        {
            // Eat the meta ";" if the token after it matches
            if (mToken.Meta && mTokenName == ";" && mTokenAfterInsertedToken != null && mTokenAfterInsertedToken.Name == match)
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
            public int MetaTokenCount;
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
            p.MetaTokenCount = mMetaTokens.Count;
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
            while (mMetaTokens.Count > p.MetaTokenCount)
                mMetaTokens.RemoveAt(mMetaTokens.Count-1);
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
            else if (mTokenName[0] == '\"')
                mToken.Type = eTokenType.Quote;
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

            // Insert a ';' after each new line
            if (prevToken.Y != mToken.Y
                    && (!sEndLineSkipSemicolon.Contains(prevToken) 
                            || sBeginLineForceSemicolon.Contains(mTokenName))
                    &&  !sBeginLineSkipSemicolon.Contains(mTokenName) )
            {
                // Mark at end of token, before any comment
                int x = prevToken.X + prevToken.Name.Length;

                mTokenAfterInsertedToken = mToken;
                mToken = new Token(";", x, prevToken.Y, eTokenBits.Meta);
                if (mShowSemi)
                    mMetaTokens.Add(mToken);
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
                            || lcComment.StartsWith("https://")
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
            ScanLow();
            while (mTokenName == "pragma")
            {
                var pragmaToken = mToken;
                pragmaToken.Type = eTokenType.ReservedControl;
                ScanLow();
                if (mTokenName != "" && char.IsLetterOrDigit(mTokenName[0]) && mToken.Y == pragmaToken.Y)
                {
                    ParsePragma();
                    ScanLow();
                }
                else
                {
                    RejectToken(pragmaToken, "Expecting an identifier on the same line as 'pragma'");
                }
            }

            // Check for tabs or white space at end of line
            if (mToken.Boln)
            {
                // Illegal tabs
                var line = mLexer.GetLine(mToken.Y);
                var i = line.IndexOf('\t');
                while (i >= 0)
                {
                    RejectToken(new Token(" ", i, mToken.Y, eTokenBits.Meta),
                        "Illegal tab");
                    i = line.IndexOf('\t', i + 1);
                }
                // ; at end of line
                if (line.Length != 0 && line[line.Length - 1] == ';')
                {
                    RejectToken(new Token(" ", line.Length - 1, mToken.Y, eTokenBits.Meta),
                        "Illegal semi-colon at end of line");
                }
                // Space at end of line (don't record error if our cursor is ther)
                if (line.Length != 0 && char.IsWhiteSpace(line[line.Length - 1]) && mLexer.Cursor.Y != mToken.Y)
                {
                    RejectToken(new Token(" ", line.Length - 1, mToken.Y, eTokenBits.Meta),
                        "Illegal white space at end of line");
                }
            }
        }

        void ScanLow()
        {
            if (!mLexerEnum.MoveNext())
                return;
            mToken = mLexerEnum.Current;
            if (mToken.Name != "")
                mToken.Clear();
            mTokenName = mToken.Name;
        }

        void ParsePragma()
        {
            mToken.Type = eTokenType.Reserved;
            if (mSyntax.Pragmas.ContainsKey(mTokenName))
            {
                RejectToken(mToken, "Duplicate pragma");
                return;
            }
            if (!sPragmas.Contains(mTokenName))
                mToken.AddWarning("Unkown pragma");
            mSyntax.Pragmas[mTokenName] = new SyntaxPragma(mToken);

            if (mTokenName == "__fail")
                throw new Exception("Parse fail test");
            if (mTokenName == "ShowSemi")
                mShowSemi = true;
            if (mTokenName == "ShowMeta")
                mShowMeta = true;
            if (mTokenName == "NoParse")
            {
                while (Accept() != "")
                { }
                throw new NoCompilePragmaException();
            }

        }

        public class ParseError : TokenError
        {
            public ParseError(string message) : base(message) { }
        }

        // Reject the given token
        public void RejectToken(Token token, string errorMessage)
        {
            if (token.Error)
                return; // The first error is the most pertinent
            mParseErrors++;
            token.AddError(new ParseError(errorMessage));
            RecordMetaToken(token);
        }

        // Call this after adding info to a meta token
        public void RecordMetaToken(Token token)
        {
            // Make sure meta tokens with errors are recorded
            if (token.Meta)
            {
                if (!mMetaTokens.Contains(token))
                    mMetaTokens.Add(token);
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
            while (!sRejectAnyStop.Contains(mToken)
                    && !sReservedWords.Contains(mTokenName)
                    && !extraStops.Contains(mToken))
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
            var virtualToken = new Token(text, connectedToken.X, connectedToken.Y, eTokenBits.Meta);
            if (mShowMeta)
                mMetaTokens.Add(virtualToken);
            Connect(connectedToken, virtualToken);
            return virtualToken;
        }


    }

}
