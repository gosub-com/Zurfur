using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.Remoting.Messaging;
using System.Text.RegularExpressions;
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
        static WordSet sPragmas = new WordSet("ShowParse ShowMeta NoParse NoCompilerChecks NoVerify AllowUnderscoreDefinitions");


        ParseZurfCheck mZurfParseCheck;

        int                 mParseErrors;	// Number of errors
        Lexer				mLexer;			// Lexer to be parsed
        int                 mAcceptY = 0;
        int                 mAcceptX = 0;
        List<Token>         mStatements = new List<Token>();
        string              mTokenName ="*"; // Skipped by first accept
        Token               mToken = new Token(";");
        Token               mPrevToken = new Token(";");
        Token               mNextStatementToken;  // Null unless at the end of a statement
        StringBuilder       mComments = new StringBuilder();
        int                 mTernaryLevel;
        bool                mShowMeta;
        bool                mAllowUnderscoreDefinitions;

        // Be kind to GC
        Queue<List<SyntaxExpr>>   mExprCache = new Queue<List<SyntaxExpr>>();

        string mNamespaceBaseStr = "";
        string[] mNamespaceBasePath = Array.Empty<string>();
        Token[] mNamePath = Array.Empty<Token>();
        SyntaxFile mSyntax;

        public int ParseErrors => mParseErrors;

        // Add semicolons to all lines, except for:
        static WordSet sContinuationEnd = new WordSet("[ ( ,");
        static WordSet sContinuationNoBegin = new WordSet("} namespace module pub fun afun extern implement youdo static noself");
        static WordSet sContinuationBegin = new WordSet("\" ] ) , . + - * / % | & || && == != "
                            + ": ? > << <= < => -> .. :: !== ===  is in as "
                            + "= += -= *= /= %= |= &= ~=");
        static WordSet sContinuationNoCheckAlign = new WordSet("] ) {");
        WordSet sStringLiteralEscapes = new WordSet("{ \" }");

        static WordSet sReservedWords = new WordSet("abstract as base break case catch class const "
            + "continue default delegate do then else elif enum explicit extern true false defer use "
            + "finally fixed for goto if implicit in interface internal is lock namespace module include "
            + "new null operator out override pub public private protected readonly ro ref dref mut imut "
            + "return unsealed unseal sealed sizeof stackalloc heapalloc struct switch this self throw try "
            + "typeof type unsafe using static noself virtual while dowhile asm managed unmanaged "
            + "async await astart func afunc get set aset aget global partial var where when nameof "
            + "box boxed init move copy clone drop error dispose own owned "
            + "trait mixin extends youdo implements implement impl union fun afun def yield let cast "
            + "any dyn loop select match event aevent from to of on cofun cofunc global local val it "
            + "throws atask task scope assign @ # and or not xor with cap exit pragma require ensure "
            + "of sync task except exception raise loc local global");

        static WordSet sClassFieldQualifiers = new WordSet("pub public protected private internal unsafe "
            + "noself unsealed abstract virtual override ro");
        static WordSet sPostFieldQualifiers = new WordSet("init ref set get mut");

        static WordSet sReservedUserFuncNames = new WordSet("new drop cast default implicit");
        static WordSet sReservedIdentifierVariables = new WordSet("null this self true false default base new cast dref");
        static WordSet sTypeUnaryOps = new WordSet("? * ^ [ ref mut");

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sAllowConstraintKeywords = new WordSet("unmanaged");
        static WordSet sAllowUnderscore = new WordSet("_");

        public static WordSet sOverloadableOps = new WordSet("+ - * / % [ in");
        static WordSet sCompareOps = new WordSet("== != < <= > >= === !== in");
        static WordSet sRangeOps = new WordSet(".. ::");
        static WordSet sAddOps = new WordSet("+ - |");
        static WordSet sXorOps = new WordSet("~");
        static WordSet sMultiplyOps = new WordSet("* / % &");
        static WordSet sAssignOps = new WordSet("= += -= *= /= %= |= &= ~= <<= >>=");
        static WordSet sUnaryOps = new WordSet("+ - ! & ~ use unsafe ref");

        // For now, do not allow more than one level.  Maybe we want to allow it later,
        // but definitely do not allow them to include compounds with curly braces.
        static WordSet sNoSubCompoundStatement = new WordSet("type class catch error " 
                                + "get set pub private protected namespace module static noself fun afun prop ");

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // The following symbols allow us to call functions, create types, access static members, and cast
        // For example `F<T1>()` to call a function or constructor, `F<T1>.Name` to access a static or member,
        // and #F<T1>(expression) to cast.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) .");

        Regex sFindUrl = new Regex(@"///|//|`|((http|https|file|Http|Https|File|HTTP|HTTPS|FILE)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?)");

        static WordSet sStatementEndings = new WordSet("; => }");
        static WordSet sStatementsDone = new WordSet("} namespace module type interface enum noself", true);
        static WordSet sRejectAnyStop = new WordSet("=> ; { }", true);
        static WordSet sRejectForCondition = new WordSet("in");
        static WordSet sRejectFuncName = new WordSet("(");
        static WordSet sRejectFuncParam = new WordSet(", )");
        static WordSet sRejectTypeName = new WordSet("( )");

        static WordMap<string> sStringLiterals = new WordMap<string>()
            { { "n", "\n" }, { "r", "\r"}, {"rn", "\r\n"}, {"t", "\t"}, {"b", "\b" } };

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public ParseZurf(Lexer lexer)
        {
            mLexer = lexer;
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
            mLexer.MetaTokens.Clear();
            mLexer.MetaTokens.Add(mLexer.EndToken);

            // TBD: Pre-process all continuations before parsing.
            //      Then insert ";" using that info.
            //      Also, make this into a function.
            for (int lineIndex = 0;  lineIndex < mLexer.LineCount;  lineIndex++)
            {
                var line = mLexer.GetLineTokens(lineIndex);
                if (line.Length != 0)
                {
                    var token = line[0];
                    token.Continuation = false;
                    if (token.VerticalLine)
                        token.RemoveInfo<TokenVerticalLine>();
                }
            }

            if (Debugger.IsAttached)
            {
                // Failure causes error in dubugger
                ParseCompilationUnit();
                mZurfParseCheck.Check(mSyntax);
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
            }
            if (mTokenName != "")
                RejectToken(mToken, "Parse error: Expecting end of file");
            while (mTokenName != "")
                Accept();

            mLexer.ShowMetaTokens = mShowMeta;

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
            Token alignment = null;
            SyntaxFunc prevProperty = null;
            while (mTokenName != "" && (mTokenName != "}" || topScope))
            {
                if (alignment == null)
                {
                    if (mTokenName != ";" && mTokenName != "{")
                        alignment = mToken;
                }
                else
                {
                    var visibleSemicolon = mPrevToken.Name == ";" && !mPrevToken.Meta;
                    if (mToken.X != alignment.X && !visibleSemicolon)
                        RejectToken(mToken, "Indentation of this statement must match endentation of statement above it");
                }

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

                // 'set' must come immediately after 'get' without
                // intervening fields, functions, or types, etc.
                if (mTokenName != "set" && mTokenName != "aset")
                    prevProperty = null;

                var keyword = mToken;
                switch (mTokenName)
                {
                    case ";":
                        if (qualifiers.Count != 0)
                            RejectToken(keyword, "Expecting a type/fun/prop, etc. or field definition");
                        break;

                    case "{":
                    case "=>":
                        Accept();
                        RejectQualifiers(qualifiers, "Unexpected qualifiers");
                        RejectToken(keyword, "Unexpected '" + keyword + "'.  Expecting a keyword, such as 'type', 'fun', etc. before the start of a new scope.");
                        break;

                    case "pragma":
                        ParsePragma();
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
                            RejectToken(keyword, "Namespace statements must not be inside a type/enum/interface body");
                        ParseNamespaceStatement(keyword);
                        break;

                    case "interface":
                    case "enum": // TBD: Change to 'type enum'
                    case "type":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        while (AcceptMatch("ref") || AcceptMatch("ro") || AcceptMatch("boxed") || AcceptMatch("class"))
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

                    case "get":
                    case "aget":
                    case "set":
                    case "aset":
                        mToken.Type = eTokenType.ReservedControl;
                        Accept();
                        var prop = ParseProperty(prevProperty, keyword, parentScope, qualifiers);
                        AddMethod(prop);
                        prevProperty = null;
                        if (keyword == "get" || keyword == "aget")
                            prevProperty = prop;
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
                            RejectToken(keyword, "Expecting a field, type, function, property ('@', 'type', 'fun', 'prop', 'const', etc.) or qualifier ('pub', etc.)");
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
                if (!AcceptIdentifier("Expecting a namespace identifier"))
                    break;
                namePath.Add(mPrevToken);
                RejectUnderscoreDefinition(mPrevToken);
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
            mNamePath = namePath.ToArray();
            var namePathStrArray = namePath.ConvertAll(token => token.Name).ToArray();
            var namePathStr = string.Join(".", namePathStrArray);
            if (mNamespaceBasePath.Length == 0)
            {
                mNamespaceBasePath = namePathStrArray;
                mNamespaceBaseStr = namePathStr;
            }
            if (!mSyntax.Namespaces.TryGetValue(namePathStr, out var ns))
            {
                ns = new SyntaxNamespace();
                mSyntax.Namespaces[namePathStr] = ns;
            }
            // Accumulate comments and keyword tokens for this namespace
            ns.Comments += " " + mComments;
            mComments.Clear();
            ns.Path = namePath.ToArray();
        }

        void ParsePragma()
        {
            mToken.Type = eTokenType.ReservedControl;
            Accept();
            mToken.Type = eTokenType.Reserved;
            if (mSyntax.Pragmas.ContainsKey(mTokenName))
                RejectToken(mToken, "Duplicate pragma");
            if (!sPragmas.Contains(mTokenName))
                mToken.AddError("Unkown pragma");
            mSyntax.Pragmas[mTokenName] = new SyntaxPragma(mToken);

            if (mTokenName == "__fail")
                throw new Exception("Parse fail test");
            if (mTokenName == "ShowMeta")
                mShowMeta = true;
            if (mTokenName == "AllowUnderscoreDefinitions")
                mAllowUnderscoreDefinitions = true;
            if (mTokenName == "NoParse")
            {
                while (Accept() != "")
                { }
                throw new NoCompilePragmaException();
            }
            Accept();
        }

        // Parse class, struct, interface, or enum
        void ParseClass(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            var synClass = new SyntaxType(keyword);
            synClass.Qualifiers = qualifiers.ToArray();
            synClass.ParentScope = parentScope;
            synClass.NamePath = mNamePath;
            synClass.Comments = mComments.ToString();
            mComments.Clear();

            if (!CheckIdentifier("Expecting an identifier possibly followed by type parameters (e.g. 'Name<T1>', etc)"))
                return; // TBD: Could try to recover (user probably editing class name)

            // Parse class name and type parameters
            (synClass.Name, synClass.TypeArgs) = GetSimpleNameWithTypeArgs(ParseType());
            if (synClass.Name.Type != eTokenType.TypeName)
                return; // TBD: Could try to recover (user probably editing class name)

            AddType(synClass);
            RejectUnderscoreDefinition(synClass.Name);

            // Push new path
            var oldPath = mNamePath;
            var namePath = new List<Token>(mNamePath);
            namePath.Add(synClass.Name);
            mNamePath = namePath.ToArray();

            // Simple class or struct
            bool isClass = qualifiers.FindIndex(a => a.Name == "class") >= 0;
            if (AcceptMatch("("))
            {
                if (isClass)
                    RejectToken(mPrevToken, "A 'type class' may not declare simple fields");

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
            else if (AcceptMatch("="))
            {
                if (isClass)
                    RejectToken(mPrevToken, "A 'type class' may not be an alias");
                synClass.Simple = true;
                synClass.AliasOrExtends = ParseType();
            }
            else if (AcceptMatchPastMetaSemicolon("extends"))
            {
                if (!isClass)
                    RejectToken(mPrevToken, "Use 'type class' to allow extending a type");
                synClass.AliasOrExtends = ParseType();
            }

            // Parse implemented classes
            if (AcceptMatchPastMetaSemicolon("implements"))
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

            if (AcceptMatchPastMetaSemicolon("{"))
            {
                var openToken = mPrevToken;
                ParseScopeStatements(synClass);

                bool error = false;
                if (AcceptMatchOrReject("}", "while parsing " + synClass.Keyword.Name + " body of '" + synClass.Name + "'"))
                {
                    Connect(openToken, mPrevToken);
                }
                else
                {
                    error = true;
                    RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace"
                                                            : "This scope has an error on its closing brace");
                }
                openToken.AddInfo(new TokenVerticalLine
                {
                    X = openToken.X,
                    Y = openToken.Y + 1,
                    Lines = mPrevToken.Y - openToken.Y,
                    Error = error
                });
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
            while (AcceptMatchPastMetaSemicolon("where"))
                constraints.Add(ParseConstraint(mPrevToken));
            return constraints.ToArray();
        }

        SyntaxConstraint ParseConstraint(Token keyword)
        {
            var constraint = new SyntaxConstraint();
            constraint.Keyword = keyword;
            if (!AcceptIdentifier("Expecting a type name"))
                return constraint;
            constraint.GenericTypeName = mPrevToken;
            mPrevToken.Type = eTokenType.TypeName;

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
            if (!AcceptIdentifier("Expecting field name"))
                return null;
            var newVarName = mPrevToken;
            newVarName.Type = eTokenType.DefineField;
            RejectUnderscoreDefinition(newVarName);

            var field = new SyntaxField(newVarName);
            field.ParentScope = parentScope;
            field.NamePath = mNamePath;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            mComments.Clear();

            if (mTokenName != "=")
                field.TypeName = ParseType();

            if (mTokenName == "=")
                field.Initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

            return field;
        }

        private void ParseFieldFull(Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            // Variable name
            if (!AcceptIdentifier("Expecting field name"))
                return;
            var newVarName = mPrevToken;
            newVarName.Type = eTokenType.DefineField;
            RejectUnderscoreDefinition(newVarName);


            foreach (var token in qualifiers)
            {
                if (token.Name != "ro" && token.Name != "noself")
                    token.AddError("This qualifier is not applicable to fields.");
            }

            // Type name
            var errors = mParseErrors;
            var typeName = ParseType();
            if (mParseErrors != errors)
                return;

            // Post field qualifiers
            if (keyword != "const" && (mTokenName == "pub" || mTokenName == "protected"))
            {
                qualifiers.Insert(0, Accept());
                while (sPostFieldQualifiers.Contains(mTokenName))
                    //qualifiers.Add(Accept());
                    Accept();
            }

            // Initializer
            SyntaxExpr initializer = null;
            if (mTokenName == "=")
                initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

            var field = new SyntaxField(newVarName);
            field.ParentScope = parentScope;
            field.NamePath = mNamePath;
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            mComments.Clear();
            field.TypeName = typeName;
            field.Initializer = initializer;
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
            mComments.Clear();
            name.Type = eTokenType.DefineField;
            RejectUnderscoreDefinition(name);


            // Optionally initialize
            if (AcceptMatch("="))
            {
                // Initialize via assignment
                field.Initializer = ParseExpr();
            }
            return field;
        }

        private SyntaxFunc ParseProperty(SyntaxFunc prevProperty, Token keyword, SyntaxScope parentScope, List<Token> qualifiers)
        {
            Token name = null;
            SyntaxExpr signature = null;
            if (keyword == "get" || keyword == "aget")
            {
                // Parse name and type
                if (!AcceptIdentifier("Expecting a property name"))
                    return null;
                name = mPrevToken;
                name.Type = eTokenType.DefineField;
                RejectUnderscoreDefinition(name);
                signature = ParseType();
                if (mToken.Error)
                    return null;
            }
            else
            {
                if (prevProperty == null)
                {
                    RejectToken(keyword, "Expecting this setter to be directly under the getter");
                    return null; ;
                }
                name = prevProperty.Name;
                signature = prevProperty.MethodSignature;
            }

            var synFunc = new SyntaxFunc(keyword);
            synFunc.IsProperty = true;
            synFunc.ParentScope = parentScope;
            synFunc.NamePath = mNamePath;
            synFunc.Comments = mComments.ToString();
            mComments.Clear();
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Name = name;
            synFunc.MethodSignature = signature;

            // Body
            if (AcceptMatchPastMetaSemicolon("extern") || AcceptMatchPastMetaSemicolon("youdo"))
            {
                qualifiers.Add(mPrevToken);
                if (mPrevToken.Name == "youdo")
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
            else
            {
                synFunc.Statements = ParseCompoundStatement(keyword);
            }
            return synFunc;
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
            mComments.Clear();

            //if (mTokenName == "^")
            //    qualifiers.Add(Accept());
            if (mTokenName == "mut")
                qualifiers.Add(Accept());
            if (mTokenName == "ref")
                qualifiers.Add(Accept());


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
                    synFunc.Name = NewMetaToken(operatorKeyword, operatorKeyword + operatorName);
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
            synFunc.MethodSignature = ParseMethodSignature(keyword);
            synFunc.Constraints = ParseConstraints();

            while (AcceptMatchPastMetaSemicolon("require"))
            {
                // TBD: Store in parse tree
                ParseExpr();
            }

            // Body
            if (AcceptMatchPastMetaSemicolon("extern") || AcceptMatchPastMetaSemicolon("youdo"))
            {
                qualifiers.Add(mPrevToken);
            }
            else
            {
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
        private SyntaxExpr ParseMethodSignature(Token keyword)
        {
            var funcParams = ParseMethodParams();

            if (mTokenName == "type")
                RejectToken(mToken, "Use '->' instead of anonymous type");

            SyntaxExpr returnParams;
            if (AcceptMatchPastMetaSemicolon("->"))
            {
                returnParams = ParseMethodParams();
            }
            else
            {
                var returns = NewExprList();
                returns.Add(ParseMethodParam(false));
                returnParams = new SyntaxMulti(mLexer.EndToken, FreeExprList(returns));
            }

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

            // TBD: Convert this to use "." instead of requiring "::" to
            //      separate extension method type from function name.
            //      Probably a simple chnage to GetSimpleNameWithTypeArgs.
            var funName = ParseType();
            if (AcceptMatch("::") && CheckIdentifier("Expecting a function or type name", sRejectTypeName))
            {
                className = funName;
                funName = ParseType();
            }

            (funcName, typeArgs) = GetSimpleNameWithTypeArgs(funName);
            RejectUnderscoreDefinition(funcName);
            var validMethodName = funcName.Type == eTokenType.TypeName;
            if (validMethodName)
                funcName.Type = eTokenType.DefineMethod;
            return validMethodName;
        }

        SyntaxExpr ParseMethodParams()
        {
            // Read open token, '('
            if (!AcceptMatchPastMetaSemicolon("(")  && !AcceptMatchOrReject("("))
                return new SyntaxError();

            // Parse parameters
            var openToken = mPrevToken;
            var parameters = NewExprList();
            if (mTokenName != ")")
                parameters.Add(ParseMethodParam(true));
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseMethodParam(true));
            }

            // Ellipse to signify repeated parameters
            //if (AcceptMatch("..."))
            //    mPrevToken.AddWarning("Repeated parameters not supported yet");

            if (AcceptMatchOrReject(")", " or ','"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(mLexer.EndToken, FreeExprList(parameters));
        }

        // Syntax Tree: (name, type, initializer)
        SyntaxExpr ParseMethodParam(bool requireName)
        {
            var name = Token.Empty;
            if (requireName)
            {
                if (!AcceptIdentifier("Expecting a variable name", sRejectFuncParam))
                    return new SyntaxError();
                name = mPrevToken;
                name.Type = eTokenType.DefineParam;
                RejectUnderscoreDefinition(name);
            }

            var type = ParseType();
            var initializer = AcceptMatch("=") ? ParseExpr() : SyntaxExpr.Empty;

            return new SyntaxBinary(name, type, initializer);
        }
        
        SyntaxExpr ParseCompoundStatement(Token keyword)
        {
            if (IsMatchPastMetaSemicolon("{"))
                return ParseStatements("'" + keyword.Name + "' statement");

            // TBD: We could do better error recovery for all the cases below.
            //      The user is probably editing the top part of the compound statement,
            //      so we could parse anything that looks correct, but gray it out as we go.

            // Ignore compound statement on error
            var keywordColumnToken = mLexer.GetLineTokens(keyword.Y)[0];
            var keywordColumn = keywordColumnToken.X;
            if (mToken.Error)
                return new SyntaxError(mToken);

            // Expecting invisible meta semi-colon
            // Expecting next line to be indented
            if (mToken.Meta && mTokenName == ";" && mNextStatementToken != null
                && mNextStatementToken.X < keywordColumn + 2)
            {
                RejectToken(mToken, $"Braceless compound statement '{keyword.Name}' is expecting '{{' or next line must be indented at least two spaces");
                return new SyntaxError();
            }

            if (!AcceptMatch(";"))
            {
                Reject($"Braceless compound statement '{keyword.Name}' is expecting '{{' or end of line");
                if (!mToken.Meta || mTokenName != ";")
                    return new SyntaxError();
                Accept(); // Continue parsing for better error recovery
            }
            else if (!mPrevToken.Meta)
            {
                RejectToken(mPrevToken, $"Braceless compound statement '{keyword.Name}' is expecting '{{' or end of line");
                return new SyntaxError();
            }

            if (mTokenName == "}" || mTokenName == ";")
            {
                RejectToken(mPrevToken, $"Braceless compound statement '{keyword.Name}' is expecting '{{' or non-empty statement");
                return new SyntaxError();
            }

            var compoundColumn = mToken.X;
            Token semicolon = mPrevToken;
            var statement = NewExprList();
            while (true)
            {
                if (sNoSubCompoundStatement.Contains(mTokenName))
                    RejectToken(mToken, $"Braceless compound statement '{keyword.Name}' may not embed '{mTokenName}' statement");

                ParseStatement(statement, false);
                if (mTokenName != ";")
                    Reject($"Braceless compound statement '{keyword.Name}' is expecting end of line");
                if (mTokenName == "}" || mTokenName == "")
                    break;

                var newColumn = mNextStatementToken == null ? -1 : mNextStatementToken.X;
                if (mToken.Meta && newColumn <= keywordColumn)
                    break;

                AcceptMatch(";");
                if (mPrevToken.Meta && newColumn != compoundColumn)
                    RejectToken(mToken, $"Indentation in braceless compound statement '{keyword.Name}' must match the statement above it");
            }

            // Draw scope lines
            // TBD: Should use a bit more intelligence to hide lines for short
            //      paragraphs.  Also, need to make sure if...elif...else
            //      shows consistent scope lines (all or none).
            //      Also, turn this into a function and combine with
            //      other TokenVerticalLines.
            int scopeLines = mPrevToken.Y - keywordColumnToken.Y;
            if (scopeLines >= 2)
            {
                keywordColumnToken.AddInfo(new TokenVerticalLine
                {
                    X = keywordColumn,
                    Y = keywordColumnToken.Y + 1,
                    Lines = scopeLines
                });
            }

            return new SyntaxMulti(semicolon, FreeExprList(statement));
        }

        /// <summary>
        /// Parse '{ statements }'
        /// </summary>
        SyntaxExpr ParseStatements(string errorMessage)
        {
            // Require '{'
            if (mToken.Meta && mTokenName == ";")
                Accept();
            if (!AcceptMatchOrReject("{"))
                return new SyntaxError();
            var openToken = mPrevToken;

            while (AcceptMatch(";"))
                ;

            var statements = NewExprList();
            var (x, y) = (mToken.X, mToken.Y);
            while (!sStatementsDone.Contains(mTokenName))
            {
                if (mToken.X != x && mToken.Y != y && mToken != "error" && mToken != "catch")
                    RejectToken(mToken, "Indentation of statement must match statement above");
                y = mToken.Y;
                ParseStatement(statements, true);
            }

            bool error = false;
            if (AcceptMatchOrReject("}", "while parsing " + errorMessage))
            {
                Connect(openToken, mPrevToken);
            }
            else
            {
                error = true;
                RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace"
                                                        : "This scope has an error on its closing brace");
            }
            openToken.SetInfo(new TokenVerticalLine
            {
                X = openToken.X,
                Y = openToken.Y + 1,
                Lines = mPrevToken.Y - openToken.Y - 1,
                Error = error
            });

            return new SyntaxMulti(openToken, FreeExprList(statements));
        }
        private void ParseStatement(List<SyntaxExpr> statements, bool requireSemicolon)
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
                    mToken.Type = eTokenType.ReservedControl;
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
                    statements.Add(new SyntaxBinary(keyword, ParseExpr(),
                                    ParseCompoundStatement(keyword)));
                    break;

                case "elif":
                case "else":
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    if (mPrevToken == "elif" || AcceptMatch("if"))
                    {
                        // `elif` or `else if`
                        if (mPrevToken.Name == "if")
                            RejectToken(mPrevToken, "Shorten to 'elif'");
                        mPrevToken.Type = eTokenType.ReservedControl;
                        statements.Add(new SyntaxBinary(keyword, ParseExpr(),
                                        ParseCompoundStatement(keyword)));
                    }
                    else
                    {
                        // `else`
                        statements.Add(new SyntaxUnary(keyword,
                                        ParseCompoundStatement(keyword)));
                    }
                    break;

                case "for":
                    // FOR (variable) (condition) (statements)
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    if (!AcceptMatch("@"))
                        RejectToken(mToken, "Expecting '@'");
                    var forVariable = new SyntaxToken(ParseIdentifier("Expecting a loop variable", sRejectForCondition));
                    forVariable.Token.Type = eTokenType.DefineLocal;
                    RejectUnderscoreDefinition(forVariable.Token);
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

                //case "switch":
                //    // SWITCH (expr) (statements)
                //    mToken.Type = eTokenType.ReservedControl;
                //    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements("'switch' statement")));
                //    break;

                case "finally":
                    Accept();
                    AcceptMatchOrReject(":");
                    statements.Add(new SyntaxToken(keyword));
                    requireSemicolon = false;
                    break;

                case "error":
                case "catch":
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
            var peek = PeekOnLine();
            if (peek.X != mToken.X + mTokenName.Length)
                return;
            if (peek != "=" && peek != ">")
                return;
            var token = mToken;
            Accept();
            var metaToken = token.Name + peek;
            if (metaToken == ">>")
            {
                peek = PeekOnLine();
                if (peek.X == token.X + metaToken.Length && peek == "=")
                {
                    Accept();
                    metaToken = ">>=";
                }
            }
            // Replace with a virtual token
            mToken = NewMetaToken(token, metaToken);
            mTokenName = mToken.Name;
        }

        SyntaxExpr ParseNewVarStatment(bool allowUnderscore)
        {
            if (!AcceptIdentifier("Expecting variable name", null, allowUnderscore ? sAllowUnderscore : null))
                return new SyntaxError();

            var newVarName = mPrevToken;
            if (newVarName.Name != "_")
            {
                newVarName.Type = eTokenType.DefineLocal;
                RejectUnderscoreDefinition(newVarName);
            }

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

        SyntaxExpr ParsePair()
        {
            var result = ParseExpr();
            if (mTokenName == ":")
            {
                result = new SyntaxBinary(Accept(), result, ParseExpr());
                if (mTokenName == ":")
                    Reject("Pair operator ':' is not associative, must use parentheses");
            }
            return result;
        }


        ///  Parse expression (doesn't include ',' '=', or ':' statements)
        SyntaxExpr ParseExpr()
        {
            return ParseLambda();
        }

        SyntaxExpr ParseLambda()
        {
            var result = ParseTernary();

            if (mTokenName == "=>")
            {
                if (result.Token.Name != "@")
                    RejectToken(mToken, "Left side must be new variable expression, e.g. '@a' or '@(a,b)'");

                var lambdaToken = Accept();
                if (IsMatchPastMetaSemicolon("{"))
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

            if (mTokenName == "type")
            {
                return ParseAnonymousClass(Accept());
            }

            return ParsePrimary();
        }

        /// <summary>
        /// Parse a function taking a type name (cast, sizeof, typeof, etc)
        /// </summary>
        private SyntaxExpr ParseTypeFunc(bool parseUnaryAfter)
        {
            var funcName = Accept();
            if (!AcceptMatchOrReject("("))
                return new SyntaxError(mToken);
            var funcOpenToken = mPrevToken;
            var funType = ParseType();
            if (AcceptMatchOrReject(")"))
                Connect(mPrevToken, funcOpenToken);
            if (parseUnaryAfter)
                return new SyntaxBinary(funcName, funType, ParseUnary());
            else
                return new SyntaxUnary(funcName, funType);
        }

        private SyntaxExpr ParseNewVarExpr()
        {
            var newVarList = NewExprList();
            if (AcceptMatch("("))
            {
                var open = mPrevToken;
                do
                {
                    if (AcceptIdentifier(""))
                    {
                        mPrevToken.Type = eTokenType.DefineLocal;
                        newVarList.Add(new SyntaxToken(mPrevToken));
                        RejectUnderscoreDefinition(mPrevToken);
                    }
                } while (AcceptMatch(","));

                if (AcceptMatchOrReject(")", "or ','"))
                    Connect(open, mPrevToken);
            }
            else
            {
                // Allow parameterless lambda capture "@ => 0", but not parameterless capture "f()@ + 2"
                if (mTokenName != "=>" && AcceptIdentifier("Expecting variable name or '=>'"))
                {
                    mPrevToken.Type = eTokenType.DefineLocal;
                    newVarList.Add(new SyntaxToken(mPrevToken));
                    RejectUnderscoreDefinition(mPrevToken);
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
                    result = ParseParen(mTokenName, result);
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

            // Parse parentheses: (expression) - not a function call.
            // Example: @a = [1,2,3]
            if (mTokenName == "(" || mTokenName == "[")
            {
                // Use ")" or "]" to differentiate between function call and ordering
                var close = mTokenName == "(" ? ")" : "]";
                var result = ParseParen(mTokenName, null);

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
        /// TBD: Store "tr" in the parse tree.
        /// TBD: The '{' and '} in string literals can destroy the scope,
        ///      causing havoc while the user is typing.
        ///      Error recovery could be improved by recognizing
        ///      string literals at a lower level (e.g. Accept instead
        ///      of ParseAtom), then intellegent error rcovery could
        ///      work a lot better.  See note in `ParseCompoundStatement`
        /// </summary>
        SyntaxExpr ParseStringLiteral(Token prefix)
        {
            const string STR_PARAM = "{?}";
            const string STR_TEMP_REPLACE = "\uF127"; // Anything unlikely to ever be seen in source code

            var quote = mToken;
            var literalTokens = new List<Token>();
            var literalSb = new StringBuilder();
            var literalExpr = NewExprList();

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

                    // Special case because user is typing.
                    // Continue quoted string on next line.
                    if (AcceptMatchPastMetaSemicolon("\""))
                    {
                        BeginScoop(mPrevToken);
                        continue;
                    }
                    // Special case because user is typing.
                    // Begin interpolation on next line.
                    if (!IsMatchPastMetaSemicolon("{"))
                        break; // End quoted string
                }

                if (!sStringLiteralEscapes.Contains(mToken))
                {
                    literalTokens.Add(Accept());
                    continue;
                }

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
                    // Special case because user is typing
                    if (AcceptMatchPastMetaSemicolon("{"))
                    {
                        RejectToken(mPrevToken, "Begin interpolation '{' should be enclosed within quotes");
                    }
                    else
                    {
                        break; // End quoted string
                    }
                }
                if (mPrevToken.Name == "{")
                {
                    mPrevToken.Type = eTokenType.ReservedControl;
                    if (mToken.Meta)
                        AcceptMatch(";");
                    
                    if (mTokenName == "\\")
                        ParseEscapes();
                    else if (mTokenName != "\"") // String not allowed in string (user is probably typing)
                        ParseInterpolatedExpression();

                    if (mTokenName == ";" && mToken.Meta &&  AcceptMatchPastMetaSemicolon("}"))
                        RejectToken(mPrevToken, "End interpolation '}' should be on previous line, or this should be a continuation line");
                    else if (AcceptMatchPastMetaSemicolon("}"))
                        mPrevToken.Type = eTokenType.ReservedControl;
                    else
                        RejectToken(mToken, "Expecting '}' to end string interpolation");

                    BeginScoop(mPrevToken);
                    continue; // Continue quoted string
                }
                if (mPrevToken.Name == "}")
                {
                    mPrevToken.Type = eTokenType.ReservedControl;
                    RejectToken(mPrevToken, "Expecting another '}' in string literal");
                    BeginScoop(mPrevToken);
                    continue;  // Continue parsing quoted string
                }
            }
            EndScoop(mToken);


            // Show final string
            var str = literalSb.ToString();
            var containsReplacement = str.Contains(STR_PARAM);
            str = str.Replace(STR_TEMP_REPLACE, STR_PARAM);
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
                if (containsReplacement && STR_PARAM.Contains(token.Name))
                    RejectToken(token, $"Interpolated string literal may not contain {STR_PARAM}");
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
                    (literalName == "}" ? mToken : mPrevToken).Grayed = true;
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
                literalExpr.Add(ParseExpr());
                literalSb.Append(STR_TEMP_REPLACE);
            }

        }


        /// <summary>
        /// Read the open '(' or '[' and then parse the parameters into parameters.
        /// When `left` has an expression, it is a function call or array index
        /// and the open '(' or '[' is used as the token.  When primary is null,
        /// it is a tuple or an array and the closing ')' or ']' is used.
        /// </summary>
        SyntaxExpr ParseParen(string expecting, SyntaxExpr left)
        {
            var parameters = NewExprList();
            if (left != null)
                parameters.Add(left);

            // Read open token, '(' or '['
            if (!AcceptMatchOrReject(expecting))
                return new SyntaxError();

            // Parse parameters
            var openToken = mPrevToken;
            var expectedToken = openToken == "(" ? ")" : "]";
            if (mTokenName != expectedToken)
            {
                parameters.Add(ParsePair());
                while (AcceptMatch(","))
                {
                    Connect(openToken, mPrevToken);
                    parameters.Add(ParsePair());
                }
            }

            var keyword = openToken;
            if (AcceptMatchOrReject(expectedToken, "or ','"))
            {
                Connect(openToken, mPrevToken);
                if (left != null)
                    keyword = mPrevToken;

                // Make key value pairs bold
                if (expecting == "[")
                {
                    var isPairs = true;
                    foreach (var e in parameters)
                    {
                        if (e.Token.Name != ":")
                        {
                            isPairs = false;
                            break;
                        }
                    }
                    if (isPairs)
                    {
                        openToken.Type = eTokenType.BoldSymbol;
                        mPrevToken.Type = eTokenType.BoldSymbol;
                        foreach (var e in parameters)
                            e.Token.Type = eTokenType.BoldSymbol;
                    }
                }
            }

            return new SyntaxMulti(keyword, FreeExprList(parameters));
        }


        SyntaxExpr ParseType()
        {
            if (sTypeUnaryOps.Contains(mTokenName))
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
                var funKeyword = Accept();
                if (mTokenName == "<")
                    RejectToken(mToken, "Generic type args on lambda not supported YET!");
                return ParseMethodSignature(funKeyword);
            }

            if (AcceptMatch("type"))
            {
                return ParseAnonymousClass(mPrevToken);
            }

            if (mToken.Type != eTokenType.Identifier)
            {
                AcceptIdentifier("Expecting a type name", sRejectTypeName);
                return new SyntaxError(mLexer.EndToken);
            }

            mToken.Type = eTokenType.TypeName;
            var result = (SyntaxExpr)new SyntaxToken(Accept());
            bool accepted;
            do
            {
                accepted = false;
                if (mTokenName == ".")
                {
                    accepted = true;
                    var dot = Accept();
                    if (!CheckIdentifier("Expecting type name"))
                        return new SyntaxError(mLexer.EndToken);
                    mToken.Type = eTokenType.TypeName;
                    result = new SyntaxBinary(dot, result, new SyntaxToken(Accept()));
                }
                else if (mTokenName == "<")
                {
                    accepted = true;
                    result = ParseTypeArgumentList(result);
                    if (mTokenName == "<")
                        RejectToken(mToken, "Illegal type argument list after type argument list");
                }
            } while (accepted);

            return result;
        }


        /// <summary>
        /// Parse type argument list: <Arg...>
        /// </summary>
        SyntaxExpr ParseTypeArgumentList(SyntaxExpr left)
        {
            Debug.Assert(mTokenName == "<");
            var openToken = Accept();

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
            return new SyntaxMulti(NewMetaToken(openToken, VT_TYPE_ARG), FreeExprList(typeArgs));
        }

        /// <summary>
        /// After calling `ParseType`, call this to get the simple name
        /// with type arguments and mark anything else with an error.
        /// No sub-embedded types, just this: TypeName<Arg1, Arg2, ...>
        /// RETURNS: (name, type args)
        /// </summary>
        (Token, SyntaxExpr) GetSimpleNameWithTypeArgs(SyntaxExpr type)
        {
            // Type name, no type args
            if (type.Count == 0 || type.Token != VT_TYPE_ARG)
            {
                if (type.Token.Type != eTokenType.TypeName)
                    RejectToken(type.Token, "Expecting an identifier possibly followed by type parameters (e.g. 'Name<T1>', etc)");
                if (type.Count != 0)
                    RejectToken(type[0].Token, "Expecting an identifier possibly followed by type parameters (e.g. 'Name<T1>', etc)");
                return (type.Token, new SyntaxToken(mLexer.EndToken));
            }
            // Type args, verify no other stuff in there
            var typeArgList = NewExprList();
            bool first = true;
            foreach (var typeArg in type)
            {
                if (typeArg.Count != 0)
                    RejectToken(typeArg.Token, "Expecting an identifier possibly followed by type parameters (e.g. 'Name<T1>', etc)");
                else if (typeArg.Token.Type != eTokenType.TypeName)
                    RejectToken(typeArg.Token, "Expecting an identifier possibly followed by type parameters (e.g. 'Name<T1>', etc)");
                else if (!first)
                    typeArgList.Add(typeArg);
                first = false;
            }
            return (type[0].Token, new SyntaxMulti(type.Token, FreeExprList(typeArgList)));
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
            if (!AcceptIdentifier(errorMessage))
                return;
            tokens.Add(mPrevToken);

            // Parse the rest
            while (AcceptMatch(".")
                    &&  AcceptIdentifier(errorMessage))
            { 
                tokens.Add(mPrevToken);
            }
        }

        /// <summary>
        /// Parse an identifier.  Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        Token ParseIdentifier(string errorMessage, WordSet errorStop = null)
        {
            AcceptIdentifier(errorMessage, errorStop);
            return mPrevToken;
        }

        /// <summary>
        /// Parse an identifier.  Error returns false and causes
        /// reject until end of statement or extraStops hit
        /// </summary>
        bool AcceptIdentifier(string errorMessage, WordSet extraStops = null, WordSet allowExtraReservedWords = null)
        {
            if (allowExtraReservedWords != null && allowExtraReservedWords.Contains(mTokenName))
            {
                Accept();
                return true;
            }
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

        bool AcceptMatchPastMetaSemicolon(string match)
        {
            // Eat the meta ";" if the token after it matches
            if (mToken.Meta && mTokenName == ";" && mNextStatementToken != null && mNextStatementToken.Name == match)
            {
                Accept();
                if (mToken.Name != "{" && mToken.Name != "}")
                    mToken.Continuation = true;
            }
            return AcceptMatch(match);
        }

        bool IsMatchPastMetaSemicolon(string match)
        {
            return mTokenName == match || mToken.Meta && mTokenName == ";" && mNextStatementToken != null && mNextStatementToken.Name == match;
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
            public Token PrevToken;
            public Token Token;
            public eTokenType TokenType;
            public Token NextStatementToken;
            public int ParseErrors;
            public int MetaTokenCount;

            public int AcceptY;
            public int AcceptX;
            public Token []Statements;

        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            p.TokenType = mToken.Type;
            p.NextStatementToken = mNextStatementToken;
            p.ParseErrors = mParseErrors;
            p.MetaTokenCount = mLexer.MetaTokens.Count;

            p.AcceptX = mAcceptX;
            p.AcceptY = mAcceptY;
            p.Statements = mStatements.ToArray();
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mToken.Type = p.TokenType;
            mTokenName = mToken.Name;
            mNextStatementToken = p.NextStatementToken;
            mParseErrors = p.ParseErrors;
            while (mLexer.MetaTokens.Count > p.MetaTokenCount)
                mLexer.MetaTokens.RemoveAt(mLexer.MetaTokens.Count-1);

            mAcceptX = p.AcceptX;
            mAcceptY = p.AcceptY;
            mStatements.Clear();
            mStatements.AddRange(p.Statements);
        }

        // Returns the next token on this line (or "" if at end or comment)
        Token PeekOnLine()
        {
            if (mAcceptX >= mStatements.Count)
                return Token.Empty;
            return mStatements[mAcceptX];
        }

        // Accept the current token and advance to the next, skipping all comments.
        // The new token is saved in mToken and the token name is saved in mTokenName.
        // Returns the token that was accepted.  Token type is set.
        Token Accept()
        {
            if (mAcceptX >= mStatements.Count)
            {
                GetNextStatements();
                mAcceptX = 0;
                if (mStatements.Count == 0)
                {
                    mToken = mLexer.EndToken;
                    mTokenName = "";
                    return mToken;
                }
            }

            mPrevToken = mToken;
            mToken = mStatements[mAcceptX++];
            mTokenName = mToken.Name;

            // Clear all errors and formatting, but preserve meta and continuation bits
            bool meta = mToken.Meta;
            bool continuation = mToken.Continuation;
            mToken.Clear();
            mToken.Meta = meta;
            mToken.Continuation = continuation;

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
            else
                mToken.Type = eTokenType.Normal;

            if (mToken.OnlyTokenOnLine && (mTokenName == "{" || mTokenName == "}"))
                mToken.Shrink = true;

            return mPrevToken;

            /// <summary>
            /// Get the next statement(s) into `mStatements`.  Can have multiple
            /// statements when separated by visible semi-colons.  Skip comments. 
            /// Appends a meta semi-colon except when empty.
            /// </summary>
            void GetNextStatements()
            {
                mStatements.Clear();
                var inQuote = false;
                var inInterpolatedQuote = false;
                mNextStatementToken = null;
                var continuationColumn = -1;
                while (mAcceptY < mLexer.LineCount)
                {
                    GetNextLine(ref inQuote, ref inInterpolatedQuote);
                    CheckLine();
                    mAcceptY++;

                    if (mStatements.Count == 0 || mAcceptY >= mLexer.LineCount)
                        continue;

                    var nextLine = mLexer.GetLineTokens(mAcceptY);
                    if (nextLine.Length == 0 || nextLine[0].Name == "//" || nextLine[0].Name == "///")
                        continue;

                    if (inQuote && !inInterpolatedQuote)
                    {
                        // Missing end quote, error given by ParseStringLiteral
                        inQuote = false;
                    }

                    var nextToken = nextLine[0];
                    var prevToken = mStatements[mStatements.Count - 1];
                    bool isContinueLine = sContinuationEnd.Contains(prevToken.Name)
                                            || sContinuationBegin.Contains(nextToken.Name)
                                            || inInterpolatedQuote && prevToken.Name == "{"
                                            || inInterpolatedQuote && nextToken == "{";

                    if (sContinuationNoBegin.Contains(nextToken.Name) || !isContinueLine)
                    {
                        // Next line is not a continuation, check alignment
                        mNextStatementToken = nextToken;
                        if (continuationColumn >= 0 && Math.Abs(continuationColumn - nextToken.X) < 2
                                && nextToken.Name != "}")
                        {
                            // TBD: Maybe move this error to previous line?
                            RejectToken(NewMetaToken(nextToken, " "),
                                "Previous line was a continuation, and this is not allowed to line up under it.  "
                                +"Indent/outdent this line or the previous line.");
                        }
                        break;
                    }

                    // Continuation line, check alignment
                    nextToken.Continuation = true;
                    if (!sContinuationNoCheckAlign.Contains(nextToken.Name))
                    {
                        continuationColumn = nextToken.X;
                        if (nextToken.X != 0 && nextToken.X < mStatements[0].X + 2)
                        {
                            continuationColumn = -1;
                             RejectToken(NewMetaToken(nextToken, " "),
                                "Continuation line must be indented two spaces or be all the way to the left");
                        }
                    }
                }

                // Add meta semi-colon (non-empty lines)
                if (mStatements.Count != 0)
                {
                    var prevToken = mStatements[mStatements.Count - 1];
                    if (prevToken.Name != ";")
                    {
                        var metaToken = AddMetaToken(new Token(";",
                            prevToken.X + prevToken.Name.Length, prevToken.Y));
                        mStatements.Add(metaToken);
                    }
                }
            }

            void GetNextLine(ref bool inQuote, ref bool inInterpolatedQuote)
            {
                var tokens = mLexer.GetLineTokens(mAcceptY);
                for (int i = 0; i < tokens.Length; i++)
                {
                    // Skip comments, but not when part of a quoted string
                    var token = tokens[i];
                    var tokenName = token.Name;
                    if ((tokenName == "//" || tokenName == "///") && (!inQuote || inInterpolatedQuote))
                    {
                        ParseComments(tokens, i);
                        break;
                    }
                    // Get strings
                    if (tokenName == "\"")
                        inQuote = !inQuote;

                    if (inQuote)
                    {
                        if (tokenName == "{" && (i+1 >= tokens.Length || tokens[i+1].Name != "{"))
                            inInterpolatedQuote = true;
                        if (inInterpolatedQuote && tokenName == "}" && (i+1 >= tokens.Length || tokens[i+1].Name != "}"))
                            inInterpolatedQuote = false;
                    }
                    mStatements.Add(token);
                }
            }

            void CheckLine()
            {
                // Ignore empty lines
                var tokens = mLexer.GetLineTokens(mAcceptY);
                var line = mLexer.GetLine(mAcceptY);
                if (tokens.Length == 0 || line.Length == 0)
                    return;
                var lastToken = tokens[tokens.Length - 1];
                if (lastToken.Type == eTokenType.Comment || lastToken.Type == eTokenType.PublicComment)
                    return;

                // NOTE: This is below the level of `Accept`, so need to add
                //       a virtual token rather than mark the token itself.
                // Illegal ';' at end of line
                if (lastToken.Name == ";")
                {
                    RejectToken(AddMetaToken(new Token(" ", lastToken.X, lastToken.Y)),
                        "Illegal semi-colon at end of line");
                }
                // Illegal tabs
                var i = line.IndexOf('\t');
                while (i >= 0)
                {
                    RejectToken(AddMetaToken(new Token(" ", i, mAcceptY)), "Illegal tab");
                    i = line.IndexOf('\t', i + 1);
                }
            }

            // Call with index pointing to "//" or "///"
            void ParseComments(Token[] tokens, int index)
            {

                var commentToken = tokens[index];
                var tokenType = commentToken == "///" ? eTokenType.PublicComment : eTokenType.Comment;

                // Retrieve comment
                var x = commentToken.X + commentToken.Name.Length;
                var comment = mLexer.GetLine(commentToken.Y).Substring(x);
                if (tokenType == eTokenType.PublicComment)
                {
                    mComments.Append(comment);
                    mComments.Append(" ");
                }

                // Set comment color (use backtick to color code variables)
                bool isCodeComment = false;
                for (int i = index; i < tokens.Length; i++)
                {
                    var t = tokens[i];
                    t.Type = tokenType;
                    if (t.Name == "`")
                    {
                        isCodeComment = !isCodeComment;
                        t.Subtype = eTokenSubtype.Normal;
                    }
                    else
                    {
                        // TBD: Subtype doesn't work here, so underline instead
                        t.Subtype = isCodeComment ? eTokenSubtype.CodeInComment : eTokenSubtype.Normal;
                        if (isCodeComment)
                            t.Type = eTokenType.Reserved;  // TBD: Remove when Subtype is fixed
                        t.Underline = isCodeComment;
                    }
                }

                // Add link to comments that look like URL's
                // TBD: Editor highlights each token individually.
                //      Either use one meta token link, or change editor to group these somehow.
                var m = sFindUrl.Matches(comment);
                for (int i = 0;  i < m.Count;  i++)
                {
                    var url = m[i].Value.ToLower();
                    if (!(url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://")))
                        continue;
                    var tokenUrl = new TokenUrl(m[i].Value);
                    foreach (var t in tokens)
                        if (t.Y == commentToken.Y && t.X - x >= m[i].Index && t.X - x < m[i].Index + m[i].Length)
                            t.AddInfo(tokenUrl);
                }
            }
        }

        public class ParseError : TokenError
        {
            public ParseError(string message) : base(message) { }
        }

        // Reject definitions beginning or ending with '_'
        void RejectUnderscoreDefinition(Token token)
        {
            if (mAllowUnderscoreDefinitions)
                return;
            var name = token.Name;
            if (name.Length != 0 && (name[0] == '_' || name[token.Name.Length - 1] == '_'))
                RejectToken(token, "Definition may not begin or end with '_'");
        }


        // Reject the given token
        public void RejectToken(Token token, string errorMessage)
        {
            if (token.Error)
                return; // The first error is the most pertinent
            mParseErrors++;
            token.AddError(new ParseError(errorMessage));
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

        /// <summary>
        /// Create a meta token connected to connectedToken
        /// </summary>
        Token NewMetaToken(Token connectedToken, string text)
        {
            var metaToken = AddMetaToken(new Token(text, connectedToken.X, connectedToken.Y));
            Connect(connectedToken, metaToken);
            return metaToken;
        }

        /// <summary>
        /// Set meta bit and add to lexer
        /// </summary>
        Token AddMetaToken(Token token)
        {
            token.Meta = true;
            mLexer.MetaTokens.Add(token);
            return token;
        }


    }

}
