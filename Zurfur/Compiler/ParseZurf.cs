using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        public const string TOKEN_STR_LITERAL = "\"";
        public const string TOKEN_STR_LITERAL_MULTI = "``";
        public const string TOKEN_COMMENT = "//";

        // Probably will also allow 2, but must also require
        // the entire file to be one way or the other.
        const int SCOPE_INDENT = 4;

        // TBD: Allow pragmas to be set externally
        static WordSet sPragmas = new WordSet("ShowParse ShowMeta NoParse NoCompilerChecks "
            + "NoVerify AllowUnderscoreDefinitions RequireBraces");


        ParseZurfCheck mZurfParseCheck;

        int                 mParseErrors;	// Number of errors
        Lexer				mLexer;			// Lexer to be parsed
        Lexer.Enumerator    mEnum;          // Lexer enumerator
        string              mTokenName ="*"; // Skipped by first accept
        Token               mToken = new Token(";");
        Token               mPrevToken = new Token(";");
        StringBuilder       mComments = new StringBuilder();
        int                 mTernaryLevel;
        bool                mShowMeta;
        bool                mAllowUnderscoreDefinitions;
        bool                mRequireBraces;
        List<Token>         mEndLineSemicolons = new List<Token>();
        int                 mEndLineSemicolonsIndex;

        // Be kind to GC
        Queue<List<SyntaxExpr>>   mExprCache = new Queue<List<SyntaxExpr>>();

        string mModuleBaseStr = "";
        string[] mModuleBasePath = Array.Empty<string>();
        List<SyntaxScope> mScopeStack = new List<SyntaxScope>();
        SyntaxFile mSyntax;

        public int ParseErrors => mParseErrors;

        // Add semicolons to all lines, except for:
        static WordSet sContinuationEnd = new WordSet("[ ( ,");
        static WordSet sContinuationNoBegin = new WordSet("} namespace module type use pragma pub fun afun " 
            + "get set aget aset if while for return break continue else");
        static WordSet sContinuationBegin = new WordSet("] ) , . + - * / % | & || && and or "
                            + "== != : ? ?? > << <= < => -> .. :: !== ===  is in as has "
                            + "= += -= *= /= %= |= &= ~= " + TOKEN_STR_LITERAL + " " + TOKEN_STR_LITERAL_MULTI);

        static WordSet sReservedWords = new WordSet("abstract as has super break case catch class const "
            + "continue default delegate do then else elif enum explicit extern true false defer use "
            + "finally fixed for goto if in interface internal is lock namespace module include "
            + "new null out override pub public private protected readonly ro ref mut imut "
            + "return unsealed unseal sealed sizeof struct switch this This self Self throw try "
            + "typeof type unsafe using static noself virtual while dowhile asm managed unmanaged "
            + "async await astart func afunc get set aset aget global partial var where when nameof "
            + "box boxed init move copy clone bag drop dispose own owned "
            + "trait mixin extends impl union fun afun def yield let cast "
            + "any dyn dynamic loop select match event aevent from to of on cofun cofunc global local it "
            + "throws atask task scope assign @ # and or not xor with cap exit pragma require ensure "
            + "of sync task except exception raise loc local global");

        static WordSet sFieldQualifiers = new WordSet("pub public private unsafe unsealed static protected");
        // Non reserved names: passcopy, nocopy
        static WordSet sTypeQualifiers = new WordSet("ro ref box owned class copy nocopy");
        static WordSet sPostFieldQualifiers = new WordSet("init set get ref mut");

        static WordSet sReservedUserFuncNames = new WordSet("new clone drop cast default");
        static WordSet sReservedIdentifierVariables = new WordSet("null this self true false default self super new cast move");
        static WordSet sReservedMemberNames = new WordSet("clone");
        static WordSet sTypeUnaryOps = new WordSet("? * ^ [ ref mut own ro box");

        static WordSet sEmptyWordSet = new WordSet("");
        static WordSet sAllowUnderscore = new WordSet("_");

        static WordSet sCompareOps = new WordSet("== != < <= > >= === !== in");
        static WordSet sRangeOps = new WordSet(".. ..+");
        static WordSet sAddOps = new WordSet("+ - |");
        static WordSet sXorOps = new WordSet("~");
        static WordSet sMultiplyOps = new WordSet("* / % &");
        static WordSet sAssignOps = new WordSet("= += -= *= /= %= |= &= ~= <<= >>=");
        static WordSet sUnaryOps = new WordSet("+ - ! & ~ use unsafe ref clone mut");

        // For now, do not allow more than one level.  Maybe we want to allow it later,
        // but definitely do not allow them to include compounds with curly braces.
        static WordSet sNoSubCompoundStatement = new WordSet("type class catch " 
                                + "get set pub private namespace module static static");

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // The following symbols allow us to call functions, create types, access static members, and cast
        // For example `F<T1>()` to call a function or constructor, `F<T1>.Name` to access a static or member,
        // and #F<T1>(expression) to cast.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) .");

        Regex sFindUrl = new Regex(@"///|//|`|((http|https|file|Http|Https|File|HTTP|HTTPS|FILE)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?)");

        static WordSet sStatementEndings = new WordSet("; => }");
        static WordSet sRejectAnyStop = new WordSet("=> ; { }", true);
        static WordSet sRejectForCondition = new WordSet("in");
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
            mEnum = lexer.GetEnumerator();
            mSyntax = new SyntaxFile();
            mSyntax.Lexer = lexer;
            mZurfParseCheck = new ParseZurfCheck(this);
        }

        Token EmptyToken => mLexer.EndToken;
        SyntaxToken EmptyExpr => new SyntaxToken(EmptyToken);
        SyntaxError SyntaxError => new SyntaxError(EmptyToken);

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
                PreProcess();
                Accept();
                ParseTopScope();
                PostProcess();
            }
            catch (NoCompilePragmaException)
            {
            }
        }

        void PreProcess()
        {
            Token prevToken = null;
            Token prevNonContinuationLineToken = null;
            Token token;

            var e = mLexer.GetEnumerator();
            while (e.MoveNext(out token))
            {
                while (token == TOKEN_COMMENT)
                {
                    ParseComment(e.CurrentLineTokens, e.CurrentLineTokenIndex-1);
                    e.SkipToEndOfLine();
                    e.MoveNext(out token);
                }

                if (token.Boln)
                    ScanBeginningOfLine();

                if (token == TOKEN_STR_LITERAL)
                    ScanQuote();
                else if (token == TOKEN_STR_LITERAL_MULTI)
                    ScanDoubleQuote();
                else
                    prevToken = token;
            }
            return;

            // Detect continuation lines and add meta semicolons
            void ScanBeginningOfLine()
            {
                token.Continuation = false;
                if (token.VerticalLine)
                    token.RemoveInfo<TokenVerticalLine>();
                if (prevToken == null)
                    return;

                bool isContinueEnd = sContinuationEnd.Contains(prevToken);
                bool isContinueBegin = sContinuationBegin.Contains(token.Name);
                bool isContinue = (isContinueEnd || isContinueBegin)
                                        && !sContinuationNoBegin.Contains(token.Name);

                // In the case where the line is continued from the previous
                // line (e.g. `(`, `[`, `,`, etc.) and the next line is not
                // indented, cancel the continuation.  This makes for better
                // error messages while typing.  If continued at the start of
                // line (e.g. `+`, `-`, etc.), it's obvious, so don't bother.
                // NOTE: This gets us the syntax error early.
                //       It doesn't change the fact that there would be
                //       a syntax error later if this didn't exist.
                if (isContinueEnd && !isContinueBegin
                        && prevNonContinuationLineToken != null
                        && prevNonContinuationLineToken.X >= token.X)
                {
                    isContinue = false;
                }

                if (isContinue)
                {
                    token.Continuation = true;
                }
                else
                {
                    prevNonContinuationLineToken = token;
                    mEndLineSemicolons.Add(AddMetaToken(new Token(";",
                            prevToken.X + prevToken.Name.Length, prevToken.Y)));
                }
            }

            void ScanQuote()
            {
                // Single line quote (always ends at end of line)
                token.Type = eTokenType.Quote;
                bool quoteEoln = token.Eoln;
                if (!token.Eoln)
                    while (e.MoveNext(out token) && token != TOKEN_STR_LITERAL && !token.Eoln)
                        token.Type = eTokenType.Quote;
                token.Type = eTokenType.Quote;
                if (quoteEoln || token.Eoln && token != TOKEN_STR_LITERAL)
                {
                    // ERROR: Expecting end quote.  Mark it and add meta semicolon.
                    // NOTE: Ideally we would add one meta semicolon and reject it,
                    //       but `Accept` clears the error.
                    prevToken = null;
                    RejectToken(AddMetaToken(new Token(" ", token.X + token.Name.Length, token.Y)),
                        "Expecting end quote before end of line");
                    mEndLineSemicolons.Add(AddMetaToken(new Token(";",
                            token.X + token.Name.Length, token.Y)));
                }
                else
                {
                    prevToken = token;
                }
            }

            void ScanDoubleQuote()
            {
                // Multi line quote
                token.Type = eTokenType.Quote;
                while (e.MoveNext(out token) && token != TOKEN_STR_LITERAL_MULTI && token != "")
                    token.Type = eTokenType.Quote;
                if (token == "")
                    RejectToken(token, "Expecting double back ticks '``' to end the multi-line string literal");
                else
                    prevToken = token;
            }

            // Call with tokenIndex pointing to "//"
            bool ParseComment(Token []tokens, int tokenIndex)
            {
                if (tokenIndex >= tokens.Length || tokens[tokenIndex] != TOKEN_COMMENT)
                    return false;

                // Show code comments (inside backticks)
                tokens[tokenIndex].Type = eTokenType.Comment;
                bool isCodeComment = false;
                for (int i = tokenIndex+1; i < tokens.Length; i++)
                {
                    var t = tokens[i];
                    t.Type = eTokenType.Comment;
                    if (t.Name == "`")
                    {
                        isCodeComment = !isCodeComment;
                        t.Subtype = eTokenSubtype.Normal;
                    }
                    else
                    {
                        // TBD: Subtype doesn't work here, so underline instead
                        t.Subtype = isCodeComment ? eTokenSubtype.CodeInComment : eTokenSubtype.Normal;
                        t.Underline = isCodeComment;
                    }
                }

                // Add link to comments that look like URL's
                // TBD: Editor highlights each token individually.
                //      Either use one meta token link, or change editor to group these somehow.
                var commentToken = tokens[tokenIndex];
                var x = commentToken.X + commentToken.Name.Length;
                var comment = mLexer.GetLine(commentToken.Y).Substring(x);
                var m = sFindUrl.Matches(comment);
                for (int i = 0; i < m.Count; i++)
                {
                    var url = m[i].Value.ToLower();
                    if (!(url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://")))
                        continue;
                    var tokenUrl = new TokenUrl(m[i].Value);
                    foreach (var t in tokens)
                        if (t.Y == commentToken.Y && t.X - x >= m[i].Index && t.X - x < m[i].Index + m[i].Length)
                        {
                            t.AddInfo(tokenUrl);
                            t.Underline = true;
                        }
                }
                return true;
            }
        }

        /// <summary>
        /// Check continuation line indentation
        /// and illegal tabs and semicolons.
        /// </summary>
        void PostProcess()
        {
            string line;
            Token[] tokens;
            int lineIndex;
            Token prev = null, token;

            for (lineIndex = 0;  lineIndex < mLexer.LineCount;  lineIndex++)
            {
                line = mLexer.GetLine(lineIndex);
                tokens = mLexer.GetLineTokens(lineIndex);
                CheckLine();

                // Skip blank and comment lines
                if (tokens.Length == 0)
                    continue;
                token = tokens[0];
                if (token.Type == eTokenType.Comment)
                    continue;

                CheckAlignment();
                prev = token;
            }
            return;

            void CheckAlignment()
            {
                if (prev == null)
                    return;
                if (token.Continuation && !prev.Continuation && token.X < prev.X + SCOPE_INDENT)
                {
                    RejectToken(NewMetaToken(token, " "),
                        "Continuation line must be indented at least one scope level");
                }
                if (!token.Continuation && prev.Continuation && token.X + SCOPE_INDENT > prev.X)
                {
                    // TBD: Prevent duplicating this error with the one above
                    RejectToken(NewMetaToken(prev, " "),
                        "Continuation line must be indented at least one scope level past line below it");
                }
            }

            // TBD: Allow tabs in multi-line quotes
            void CheckLine()
            {
                // Illegal tabs
                var i = line.IndexOf('\t');
                while (i >= 0)
                {
                    RejectToken(AddMetaToken(new Token(" ", i, lineIndex)), "Illegal tab");
                    i = line.IndexOf('\t', i + 1);
                }

                i = tokens.Length - 1;
                while (i >= 0 && tokens[i].Type == eTokenType.Comment)
                    i--;
                if (i >= 0 && tokens[i].Name == ";")
                    RejectToken(AddMetaToken(new Token(" ", tokens[i].X, tokens[i].Y)),
                        "Illegal semi-colon at end of line");
            }

        }

        void ParseTopScope()
        {
            var qualifiers = new List<Token>();
            Token alignment = null;
            while (mTokenName != "")
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
                        RejectToken(mToken, $"Incorrect indentation at this scope level, expecting {alignment.X} spaces");
                }
                ParseScopeStatement(qualifiers, true);
            }
        }

        void ParseScopeStatements(Token keyword)
        {
            var qualifiers = new List<Token>();
            ParseScopeLevel(keyword, sEmptyWordSet, useBraces => 
            {
                ParseScopeStatement(qualifiers, useBraces);
            });
        }

        private void ParseScopeStatement(List<Token> qualifiers, bool requireSemicolon)
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
            ParseQualifiers(sFieldQualifiers, qualifiers);

            var keyword = mToken;
            switch (mTokenName)
            {
                case ";":
                    if (qualifiers.Count != 0)
                        RejectToken(keyword, "Expecting a type/fun, property or field definition");
                    break;

                case "{":
                case "=>":
                    Accept();
                    RejectToken(keyword, "Unexpected '" + keyword + "'.  Expecting a keyword, such as 'type', 'fun', etc. before the start of a new scope.");
                    break;

                case "pragma":
                    ParsePragma();
                    break;

                case "use":
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    mSyntax.Using.Add(ParseUsingStatement());
                    if (mModuleBasePath.Length == 0)
                        RejectToken(keyword, "'use' statement must come after the first 'module' statement");
                    else if (mSyntax.Types.Count != 0 || mSyntax.Methods.Count != 0 || mSyntax.Fields.Count != 0)
                        RejectToken(keyword, "'use' statement must come before any types, fields, or methods are defined");
                    break;

                case "module":
                    mToken.Type = eTokenType.ReservedControl;
                    if (mToken.X != 0)
                        RejectToken(keyword, "'module' statement must be in the first column");
                    Accept();
                    if (mScopeStack.Count != 0 && mScopeStack.Last().Keyword != "module")
                        RejectToken(keyword, "'module' statement must not be inside a type, interface, impl, etc.");
                    ParseModuleStatement(keyword);
                    break;

                case "interface":
                case "enum":
                case "type":
                case "impl":
                    mToken.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    ParseTypeScope(keyword, qualifiers);
                    if (mScopeStack.Count != 0 && mScopeStack[mScopeStack.Count-1].Keyword != "module")
                        RejectToken(keyword, "Types must be declared at the module level");
                    break;

                case "get":
                case "aget":
                case "set":
                case "aset":
                case "fun":
                case "afun":
                    mToken.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    AddMethod(ParseMethod(keyword, qualifiers));

                    // TBD: Defining them inside the class could be a shortcut
                    //      or it could be illegal.  Either way, it is the same.
                    //if (mScopeStack.Count != 0 && mScopeStack[mScopeStack.Count-1].Keyword != "module")
                    //    RejectToken(keyword, "Functions must be declared in modules, not in types");
                    break;

                case "@":
                    ParseFieldFull(Accept(), qualifiers);
                    break;

                case "const":
                    mToken.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    AddField(ParseFieldSimple(qualifiers));
                    break;

                default:
                    if (mScopeStack.Count != 0 && mScopeStack.Last().Keyword == "enum" && keyword.Type == eTokenType.Identifier)
                    {
                        // For enum, assume the first identifier is a field
                        Accept();
                        AddField(ParseEnumField(qualifiers, keyword));
                    }
                    else
                    {
                        Accept();
                        RejectToken(keyword, "Expecting a field, type, function, property ('@', 'type', 'fun', 'get', 'const', etc.) or qualifier ('pub', etc.)");
                    }
                    break;
            }
            if (requireSemicolon)
                AcceptSemicolonOrReject();
        }

        private void ParseQualifiers(WordSet allowedQualifiers, List<Token> qualifiers)
        {
            while (allowedQualifiers.Contains(mTokenName))
            {
                mToken.Type = eTokenType.Reserved;
                qualifiers.Add(Accept());
            }
        }

        /// <summary>
        /// Perform parseLine(useBraces) action on each line at this scope level.
        /// </summary>
        private void ParseScopeLevel(Token keyword, WordSet notAllowed, Action<bool> parseLine)
        {
            bool useBraces = IsMatchPastMetaSemicolon("{");
            if (!useBraces && (mTokenName != ";" || !mToken.Meta))
            {
                Reject("Expecting end of line or '{'");
                useBraces = IsMatchPastMetaSemicolon("{");
            }
            if (useBraces)
            {
                ParseScopeLevelWithBraces(keyword, parseLine);
                return;
            }
            if (mRequireBraces)
                RejectToken(mToken, "Expecting '{' because pragma 'RequireBraces' is set");

            // Expecting end of line, skip extra tokens
            if (mTokenName != ";" || !mToken.Meta)
            {
                RejectToken(mToken, "Expecting end of line");
                Accept();
                while (mTokenName != ";" || !mToken.Meta)
                {
                    Accept().Grayed = true;
                    if (mTokenName == "")
                        return;
                }
            }

            // Next line must be indented
            var keywordColumnToken = mLexer.GetLineTokens(keyword.Y)[0];
            if (NextLineToken().X <= keywordColumnToken.X)
            {
                // Immediate end of scope
                RejectToken(mToken, "Expecting '{' or next line to be indented four spaces");
                return;
            }

            Accept(); // End of line semi-colon
            var scopeColumn = keywordColumnToken.X + SCOPE_INDENT;
            while (true)
            {
                if (mPrevToken.Meta && mToken.X != scopeColumn)
                {
                    RejectToken(mToken, $"Incorrect indentation at this scope level, expecting {scopeColumn} spaces");
                }
                if (notAllowed.Contains(mTokenName))
                    Reject($"Braceless statement '{keyword.Name}' may not embed '{mTokenName}' statement");

                parseLine(false);

                if (mTokenName != ";")
                    Reject($"Expecting ';' or end of line");

                if (mTokenName == "}" || mTokenName == "")
                    break;

                if (mToken.Meta && NextLineToken().X <= keywordColumnToken.X)
                    break;

                AcceptMatch(";");
            }

            // Draw scope lines
            // TBD: Make if...elif...else consistent, all or none.
            int scopeLines = mPrevToken.Y - keywordColumnToken.Y;
            if (scopeLines >= 2)
                Token.AddScopeLines(mLexer, keywordColumnToken.Y, scopeLines, false);
        }

        /// <summary>
        /// Parse '{ statements }'
        /// </summary>
        void ParseScopeLevelWithBraces(Token keyword, Action<bool> parseLine)
        {
            // Require '{'
            if (mToken.Meta && mTokenName == ";")
                Accept();
            if (!AcceptMatchPastMetaSemicolon("{"))
                return;
            var openToken = mPrevToken;
            mPrevToken.Type = eTokenType.ReservedControl;

            var keywordColumnToken = mLexer.GetLineTokens(keyword.Y)[0];
            if (openToken.Boln && openToken.X != keywordColumnToken.X)
            {
                // Only braces at beginning of line get checked
                RejectToken(openToken, "Expecting open brace to line up under the previous line");
            }

            while (AcceptMatch(";"))
                ;

            var scopeColumn = keywordColumnToken.X + SCOPE_INDENT;
            if (mTokenName != "}" && mTokenName != "" && openToken.Eoln && mToken.X != scopeColumn)
            {
                // Check first line spacing only when it's a new line
                RejectToken(mToken, $"Incorrect indentation at this scope level, expecting {scopeColumn} spaces");
            }
            var y = mToken.Y;
            while (mTokenName != "}" && mTokenName != "")
            {
                if (mToken.X != scopeColumn && mToken.Y != y)
                    RejectToken(mToken, $"Incorrect indentation at this scope level, expecting {scopeColumn} spaces");
                y = mToken.Y;
                parseLine(true);
            }

            bool error = false;
            if (AcceptMatchOrReject("}", $"Expecting '}}' while parsing {keyword}"))
            {
                var closeToken = mPrevToken;
                closeToken.Type = eTokenType.ReservedControl;
                Connect(openToken, closeToken);
                if (closeToken.Boln && closeToken.Eoln && closeToken.X != keywordColumnToken.X)
                {
                    // Only braces on thier own line get checked
                    RejectToken(closeToken, "Expecting close brace to line up under the open brace keyword column", true);
                }
            }
            else
            {
                error = true;
                RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace"
                                                        : "This scope has an error on its closing brace");
            }
            Token.AddScopeLines(mLexer, openToken.Y, mPrevToken.Y - openToken.Y - 1, error);
        }


        void AddType(SyntaxType type)
        {
            if (type == null)
                return; // Error already marked while parsing definition
            if (mModuleBasePath.Length == 0)
            {
                RejectToken(type.Name, "The module name must be defined before any type");
                return;
            }
            mSyntax.Types.Add(type);
        }
        void AddField(SyntaxField field)
        {
            if (field == null)
                return; // Error already marked while parsing definition
            if (mModuleBasePath.Length == 0)
            {
                RejectToken(field.Name, "The module name must be defined before any field");
                return;
            }
            mSyntax.Fields.Add(field);
        }

        void AddMethod(SyntaxFunc method)
        {
            if (method == null)
                return; // Error already marked while parsing definition
            if (mModuleBasePath.Length == 0)
            {
                RejectToken(method.Name, "The module name must be defined before any method");
                return;
            }
            mSyntax.Methods.Add(method);
        }

        SyntaxUsing ParseUsingStatement()
        {
            var synUsing = new SyntaxUsing();
            var tokens = new List<Token>();
            ParseQualifiedIdentifier(tokens, "Expecting a module name identifier");
            synUsing.ModuleName = tokens.ToArray();

            if (AcceptMatch("["))
            {
                var openToken = mPrevToken;
                tokens.Clear();
                do
                {
                    if (mTokenName == "]")
                        break;
                    if (!AcceptIdentifier("Expecting a symbol from the module"))
                        break;
                    if (tokens.FindIndex(m => m.Name == mPrevToken.Name) < 0)
                        tokens.Add(mPrevToken);
                    else
                        RejectToken(mPrevToken, "Duplicate symbol");
                } while (AcceptMatch(","));
                if (AcceptMatchOrReject("]"))
                    Connect(mPrevToken, openToken);
                synUsing.Symbols = tokens.ToArray();
            }

            return synUsing;
        }

        void ParseModuleStatement(Token keyword)
        {
            var namePath = new List<Token>();
            do
            {
                if (!AcceptIdentifier("Expecting a module name identifier"))
                    break;
                namePath.Add(mPrevToken);
                RejectUnderscoreDefinition(mPrevToken);
            } while (AcceptMatch("."));

            if (namePath.Count == 0)
                return; // Rejected above

            bool scopeAdded = false;
            for (int i = 0;  i <  namePath.Count;  i++)
            {
                // Match base path
                if (i < mModuleBasePath.Length)
                {
                    if (namePath[i] != mModuleBasePath[i])
                    {
                        RejectToken(namePath[i], $"Expecting module name to start with '{mModuleBaseStr}'");
                        return;
                    }
                    continue;
                }
                scopeAdded = true;
                if (i < mScopeStack.Count)
                    mScopeStack.RemoveRange(i, mScopeStack.Count-i);
                mScopeStack.Add(new SyntaxModule(keyword, namePath[i], i == 0 ? null : mScopeStack[i-1]));
            }
            if (!scopeAdded)
            {
                RejectToken(namePath[namePath.Count - 1], $"Expecting module name to have another identifier after '{mModuleBaseStr}.'");
                return;
            }

            // Collect base module name
            var namePathStrArray = namePath.ConvertAll(token => token.Name).ToArray();
            var namePathStr = string.Join(".", namePathStrArray);
            if (mModuleBasePath.Length == 0)
            {
                mModuleBasePath = namePathStrArray;
                mModuleBaseStr = namePathStr;
            }
            var module = (SyntaxModule)mScopeStack[mScopeStack.Count - 1];
            mSyntax.Modules[namePathStr] = module;

            // Accumulate comments and keyword tokens for this module
            module.Comments += " " + mComments;
            mComments.Clear();
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
            if (mTokenName == "RequireBraces")
                mRequireBraces = true;
            if (mTokenName == "NoParse")
            {
                while (Accept() != "")
                { }
                throw new NoCompilePragmaException();
            }
            Accept();
        }

        void ParseTypeScope(Token keyword, List<Token> qualifiers)
        {
            var synClass = new SyntaxType(keyword);
            synClass.Qualifiers = qualifiers.ToArray();
            synClass.Parent = mScopeStack.Count == 0 ? null : mScopeStack.Last();
            synClass.Comments = mComments.ToString();
            mComments.Clear();

            // Experimental: See how syntax would look if we put "pub" after "type"
            if (mTokenName == "pub")
                qualifiers.Add(Accept());

            if (keyword.Name == "type")
            {
                foreach (var q in qualifiers)
                    if (sTypeQualifiers.Contains(q.Name))
                        RejectToken(q, "Place qualifier after 'type' keyword");
                ParseQualifiers(sTypeQualifiers, qualifiers);
            }

            if (keyword == "impl")
            {
                if (mTokenName == "<")
                     synClass.TypeArgs = ParseTypeParameters();

                if (!CheckIdentifier("Expecting a type name"))
                    return;
                synClass.ImplType = ParseType();
                if (!AcceptMatch("::"))
                {
                    Reject("Expecting '::' or 'for'");
                    return;
                }
                if (!CheckIdentifier("Expecting a type name"))
                    return;

                synClass.Name = mToken;
                synClass.ImplInterface = ParseType();
            }
            else
            {
                // Parse class name and type parameters
                if (!CheckIdentifier("Expecting a type name"))
                    return;
                synClass.Name = Accept();
                synClass.Name.Type = eTokenType.TypeName;
                if (mTokenName == "<" && (keyword == "type" || keyword == "interface"))
                    synClass.TypeArgs = ParseTypeParameters();
            }


            AddType(synClass);
            RejectUnderscoreDefinition(synClass.Name);

            // Push new path
            var oldScopeStack = mScopeStack;
            mScopeStack = new List<SyntaxScope>(oldScopeStack);
            mScopeStack.Add(synClass);

            // Simple class or struct
            if (AcceptMatch("("))
            {
                synClass.Simple = true;
                var open = mPrevToken;
                do
                {
                    var simpleField = ParseFieldSimple(new List<Token>());
                    if (simpleField != null)
                    {
                        simpleField.Simple = true;
                        AddField(simpleField);
                    }
                } while (AcceptMatch(","));
                if (AcceptMatchOrReject(")"))
                    Connect(mPrevToken, open);
                mScopeStack = oldScopeStack;
                return;
            }

            if (AcceptMatch("=") || AcceptMatch("is"))
            {
                var prev = mPrevToken.Name;
                synClass.Simple = true;
                synClass.Alias = ParseType();
                if (prev == "=")
                {
                    mScopeStack = oldScopeStack;
                    return;
                }
            }

            synClass.Constraints = ParseConstraints();

            ParseScopeStatements(synClass.Keyword);


            // Restore old path
            mScopeStack = oldScopeStack;
        }

        SyntaxExpr ParseTypeParameters()
        {
            if (!AcceptMatch("<"))
                return null;
            var openToken = mPrevToken;
            var typeParams = NewExprList();
            typeParams.Add(new SyntaxToken(ParseIdentifier("Expecting a type name", sRejectTypeName)));
            if (mPrevToken.Type == eTokenType.Identifier)
                mPrevToken.Type = eTokenType.TypeName;
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                typeParams.Add(new SyntaxToken(ParseIdentifier("Expecting a type name", sRejectTypeName)));
                if (mPrevToken.Type == eTokenType.Identifier)
                    mPrevToken.Type = eTokenType.TypeName;
            }
            if (AcceptMatchOrReject(">", "Expecting '>' to end the type argument list"))
            {
                Connect(openToken, mPrevToken);
            }
            return new SyntaxMulti(openToken, FreeExprList(typeParams));
        }


        private SyntaxConstraint[] ParseConstraints()
        {
            if (!AcceptMatchPastMetaSemicolon("where"))
                return null;
            List<SyntaxConstraint> constraints = new List<SyntaxConstraint>();
            do
            {
                constraints.Add(ParseConstraint());
            } while (AcceptMatchPastMetaSemicolon("where"));

            return constraints.ToArray();
        }

        SyntaxConstraint ParseConstraint()
        {
            var constraint = new SyntaxConstraint();
            if (!AcceptMatch("This") && !AcceptIdentifier("Expecting a type name"))
                return null;
            constraint.TypeName = mPrevToken;
            mPrevToken.Type = eTokenType.TypeName;

            if (!AcceptMatchOrReject("is", "Expecting 'is' while parsing constraint"))
                return null;

            var constraintTypeNames = NewExprList();
            do
            {
                constraintTypeNames.Add(ParseType());
            } while (AcceptMatch("+"));
            constraint.TypeConstraints = FreeExprList(constraintTypeNames);
            return constraint;                       
        }

        /// <summary>
        /// Current token must already be checked for validity
        /// </summary>
        SyntaxField ParseFieldSimple(List<Token> qualifiers)
        {
            if (!AcceptIdentifier("Expecting field name"))
                return null;
            var newVarName = mPrevToken;
            newVarName.Type = eTokenType.DefineField;
            RejectUnderscoreDefinition(newVarName);

            var field = new SyntaxField(newVarName);
            field.Parent = mScopeStack.Count == 0 ? null : mScopeStack.Last();
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = mComments.ToString();
            mComments.Clear();

            if (mTokenName != "=")
                field.TypeName = ParseType();

            if (mTokenName == "=")
                field.Initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

            return field;
        }

        private void ParseFieldFull(Token keyword, List<Token> qualifiers)
        {
            // Variable name
            if (!AcceptIdentifier("Expecting field name"))
                return;
            var newVarName = mPrevToken;
            newVarName.Type = eTokenType.DefineField;
            RejectUnderscoreDefinition(newVarName);

            if (AcceptMatch("static"))
                qualifiers.Add(mPrevToken);

            // Type name
            var errors = mParseErrors;
            var typeName = ParseType();
            if (mParseErrors != errors)
                return;

            // Post field qualifiers
            if (keyword != "const" && (mTokenName == "pub"))
            {
                // TBD: Distinguish "ro @a int" from "@a int pub ro", same for "pub"
                qualifiers.Insert(0, Accept());
                while (sPostFieldQualifiers.Contains(mTokenName))
                    qualifiers.Add(Accept());
            }

            // Initializer
            SyntaxExpr initializer = null;
            if (mTokenName == "=")
                initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

            var field = new SyntaxField(newVarName);
            field.Parent = mScopeStack.Count == 0 ? null : mScopeStack.Last();
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
        SyntaxField ParseEnumField(List<Token> qualifiers, Token name)
        {
            var field = new SyntaxField(name);
            field.Parent = mScopeStack.Count == 0 ? null : mScopeStack.Last();
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

        /// <summary>
        /// Function or property
        /// </summary>
        SyntaxFunc ParseMethod(Token keyword, List<Token> qualifiers)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc(keyword);
            synFunc.Parent = mScopeStack.Count == 0 ? null : mScopeStack.Last();
            synFunc.Comments = mComments.ToString();
            mComments.Clear();

            // Type parameters, TBD: fun <T> List<T>::name()
            // NOTE: Since parameters after function name are a little
            //       confusing with extension methods (see below), we could
            //       do it Kotlin style.   OTOH, most methods will not be
            //       extensions, and people can get used to it.
            //if (mTokenName == "<")
            //    synFunc.TypeArgs = ParseTypeParameters();

            // Experimental: See how syntax would look if we put "pub" after "fun"
            //      TBD: Move to before type parameters?
            while (mTokenName == "pub" || mTokenName == "protected" || mTokenName == "static")
                qualifiers.Add(Accept());

            // TBD: Move `this` to first parameter?
            while (mTokenName == "mut")
                qualifiers.Add(Accept());

            var validMethodName = ParseExtensionTypeAndMethodName(out synFunc.ExtensionType, out synFunc.Name);
            if (mTokenName == "<" && synFunc.TypeArgs == null)
                synFunc.TypeArgs = ParseTypeParameters();

            // Don't process function while user is typing (this is for a better error message)
            if (!IsMatchPastMetaSemicolon("("))
            {
                validMethodName = false;
            }

            synFunc.MethodSignature = ParseMethodSignature(keyword, out var hasReturnType);
            synFunc.Constraints = ParseConstraints();

            while (AcceptMatchPastMetaSemicolon("require"))
            {
                // TBD: Store in parse tree
                ParseExpr();
            }

            // Body
            if (AcceptMatchPastMetaSemicolon("extern") || AcceptMatchPastMetaSemicolon("impl"))
            {
                qualifiers.Add(mPrevToken);
            }
            else
            {
                synFunc.Statements = ParseStatements(keyword);
            }

            synFunc.Qualifiers = qualifiers.ToArray();

            if (!validMethodName)
                return null;
            return synFunc;
        }

        /// <summary>
        /// Returns true if we are a valid method name
        /// </summary>
        bool ParseExtensionTypeAndMethodName(out SyntaxExpr extensionType, out Token funcName)
        {
            extensionType = null;
            if (sReservedUserFuncNames.Contains(mTokenName))
            {
                // Reserved function
                funcName = Accept();
                funcName.Type = eTokenType.Reserved;
                return true;
            }
            // Extension method
            if (AcceptMatch("("))
            {
                // TBD: Move extension method to parameter
                var extParam = ParseMethodParam(false);
                if (!AcceptMatchOrReject(")") || extParam.Count == 0)
                {
                    funcName = mToken;
                    return false;
                }
                extensionType = extParam[0];
            }

            funcName = mToken;
            if (!CheckIdentifier("Expecting a function or property name", sRejectTypeName))
                return false;

            Accept();
            funcName.Type = eTokenType.DefineMethod;
            RejectUnderscoreDefinition(funcName);
            return true;
        }


        /// <summary>
        /// returns SyntaxExpr:
        ///     [0] - Parameters (name, type)
        ///     [1] - Returns (name, type) possibly blank for each
        ///     [2] - error/exit token
        /// </summary>
        private SyntaxExpr ParseMethodSignature(Token keyword, out bool hasReturnType)
        {
            hasReturnType = false;
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
                // Single return parameter
                var returns = NewExprList();
                if (BeginsType())
                {
                    hasReturnType = true;
                    returns.Add(new SyntaxBinary(EmptyToken, ParseType(), EmptyExpr));
                }
                returnParams = new SyntaxMulti(EmptyToken, FreeExprList(returns));
            }

            SyntaxToken qualifier = EmptyExpr;
            if (AcceptMatchPastMetaSemicolon("throws"))
                qualifier = new SyntaxToken(mPrevToken);

            return new SyntaxMulti(keyword, funcParams, returnParams, qualifier);
        }


        SyntaxExpr ParseMethodParams()
        {
            // Read open token, '('
            if (!AcceptMatchPastMetaSemicolon("(")  && !AcceptMatchOrReject("("))
                return SyntaxError;

            // Parse parameters
            var openToken = mPrevToken;
            var parameters = NewExprList();
            if (mTokenName != ")")
                parameters.Add(ParseMethodParam());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseMethodParam());
            }

            // Ellipse to signify repeated parameters
            //if (AcceptMatch("..."))
            //    mPrevToken.AddWarning("Repeated parameters not supported yet");

            if (AcceptMatchOrReject(")", "Expecting ')' or ','"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(EmptyToken, FreeExprList(parameters));
        }

        // Syntax Tree: (name, type, initializer)
        SyntaxExpr ParseMethodParam(bool requireName = true)
        {
            if (!BeginsType())
            {
                Reject("Expecting a variable or type name", sRejectFuncParam);
                return SyntaxError;
            }

            var name = mToken;
            var nameExpr = ParseType();
            SyntaxExpr type;
            if (BeginsType())
            {
                type = ParseType();
                if (nameExpr.Count != 0)
                    RejectToken(nameExpr.Token, "Expecting a variable name without type parameters");
                else if (sReservedWords.ContainsKey(name))
                    RejectToken(name, "Expecting an identifier, must not be reserved");
            }
            else
            {
                // No variable name given
                if (requireName)
                {
                    Reject("Expecting a type name", sRejectFuncParam);
                    type = new SyntaxToken(EmptyToken);
                }
                else
                {
                    type = nameExpr;
                    name = EmptyToken;
                }
            }

            if (name != "")
            {
                name.Type = eTokenType.DefineParam;
                RejectUnderscoreDefinition(name);
            }

            var initializer = AcceptMatch("=") ? ParseExpr() : EmptyExpr;
            return new SyntaxBinary(name, type, initializer);
        }
        
        SyntaxExpr ParseStatements(Token keyword)
        {
            var semicolon = mToken;
            var statement = NewExprList();
            ParseScopeLevel(keyword, sNoSubCompoundStatement, useBraces => ParseStatement(statement, useBraces));
            return new SyntaxMulti(semicolon, FreeExprList(statement));
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
                    statements.Add(ParseStatements(keyword));
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
                                    ParseStatements(keyword)));
                    break;

                case "scope":
                    // SCOPE (body)
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxUnary(Accept(), ParseStatements(keyword)));
                    break;

                case "do":
                    // DO (condition) (body)
                    mToken.Type = eTokenType.ReservedControl;
                    var doKeyword = Accept();
                    var doStatements = ParseStatements(keyword);
                    var doExpr = (SyntaxExpr)SyntaxError;
                    if (mToken == ";")
                        Accept();
                    if (AcceptMatchOrReject("dowhile"))
                    {
                        mPrevToken.Type = eTokenType.ReservedControl;
                        doExpr = ParseExpr();
                    }
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxBinary(doKeyword, doExpr, doStatements));
                    break;                        

                case "if":
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    statements.Add(new SyntaxBinary(keyword, ParseExpr(),
                                    ParseStatements(keyword)));
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
                                        ParseStatements(keyword)));
                    }
                    else
                    {
                        // `else`
                        statements.Add(new SyntaxUnary(keyword,
                                        ParseStatements(keyword)));
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
                                    ParseStatements(keyword)));
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

                case "catch":
                    Accept();
                    var caseExpressions = NewExprList();
                    if (mTokenName != ":")
                        caseExpressions.Add(ParseConditionalOr());
                    while (AcceptMatch(","))
                        caseExpressions.Add(ParseConditionalOr());
                    statements.Add(new SyntaxMulti(keyword, FreeExprList(caseExpressions)));
                    AcceptMatchOrReject(":", "Expecting ':' or ','");
                    requireSemicolon = false;
                    break;

                case "fun":
                case "afun":
                case "func":
                case "afunc":
                    // TBD: Process local functions
                    //      Need to pass scope into ParseMethod
                    //      Require `local` keyword
                    var qualifiers = new List<Token>() { Accept() };
                    keyword.Type = eTokenType.ReservedControl;  // Fix keyword to make it control
                    keyword.AddWarning("Local function not processed yet");
                    var synFunc = ParseMethod(keyword, qualifiers);
                    //if (synFunc != null)
                    //    statements.Add(synFunc);
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
            var peek = mEnum.PeekNoSpace();
            if (peek != "=" && peek != ">")
                return;
            var token = mToken;
            Accept();
            var metaToken = token.Name + peek;
            if (metaToken == ">>")
            {
                peek = mEnum.PeekNoSpace();
                if (peek == "=")
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
                return SyntaxError;

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
                typeName = EmptyExpr;

            SyntaxExpr initializer;
            if (mTokenName == "=")
                initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());
            else
                initializer = EmptyExpr;

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
                    result = new SyntaxBinary(lambdaToken, result, ParseStatements(lambdaToken));
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
            while (mTokenName == "or")
                result = new SyntaxBinary(Accept(), result, ParseConditionalAnd());
            return result;
        }

        SyntaxExpr ParseConditionalAnd()
        {
            var result = ParseComparison();
            while (mTokenName == "and")
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
            var result = sRangeOps.Contains(mTokenName) ? EmptyExpr : ParseAdd();
            if (sRangeOps.Contains(mTokenName))
            {
                result = new SyntaxBinary(Accept(), result, 
                    mTokenName == ")" || mTokenName == "]" ? EmptyExpr : ParseAdd());
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
            else
                while (mTokenName == "??")
                {
                    mToken.Type = eTokenType.BoldSymbol;
                    result = new SyntaxBinary(Accept(), result, ParseNewVarCapture());
                }

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
                return new SyntaxBinary(Accept(), ParseTypeFunc(), ParseUnary());
            }

            if (mTokenName == "sizeof" || mTokenName == "typeof")
            {
                return new SyntaxUnary(Accept(), ParseTypeFunc());
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
        private SyntaxExpr ParseTypeFunc()
        {
            if (!AcceptMatchOrReject("("))
                return new SyntaxError(mToken);
            var funcOpenToken = mPrevToken;
            var funType = ParseType();
            if (AcceptMatchOrReject(")"))
                Connect(mPrevToken, funcOpenToken);
            return funType;
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

                if (AcceptMatchOrReject(")", "Expecting ')' or ','"))
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

            return new SyntaxMulti(EmptyToken, FreeExprList(newVarList));
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
                    if (mEnum.PeekNoSpace() == "(")
                    {
                        // Type assertion
                        result = new SyntaxBinary(NewMetaToken(Accept(), ".("), ParseTypeFunc(), result);
                    }
                    else
                    {
                        // Dot operator
                        result = new SyntaxBinary(Accept(), result,
                            new SyntaxToken(ParseIdentifier("Expecting identifier", null, sReservedMemberNames)));
                    }
                    accepted = true;
                }
                else if (mTokenName == ".*")
                {
                    // Dereference
                    accepted = true;
                    result = new SyntaxUnary(Accept(), result);
                }
                else if (mTokenName == "<")
                {
                    // Possibly a type argument list.  Let's try it and find out.
                    var typeArgIdentifier = mPrevToken;
                    var p = SaveParsePoint();
                    var openTypeToken = mToken;

                    mParseErrors = 0;
                    var typeArgs = ParseTypeArguments(result);
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
            if (mTokenName == TOKEN_STR_LITERAL || mTokenName == TOKEN_STR_LITERAL_MULTI)
            {
                return ParseStringLiteral(null);
            }
            if (mToken.Type == eTokenType.Identifier)
            {
                var identifier = Accept();
                if (mTokenName == TOKEN_STR_LITERAL || mTokenName == TOKEN_STR_LITERAL_MULTI)
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
        /// Parse interpolated string: "string ${expr} continue ${\rn}"
        /// Prefix may be null, or 'tr'.  Next token must be quote symbol.
        /// TBD: Store "tr" in the parse tree.
        /// </summary>
        SyntaxExpr ParseStringLiteral(Token syntax)
        {
            const string STR_PARAM = "{?}";
            const string STR_TEMP_REPLACE = "\uF127"; // Anything unlikely to ever be seen in source code

            var quote = mToken;
            var literalTokens = new List<Token>();
            var literalSb = new StringBuilder();
            var literalExpr = NewExprList();
            var scoopStartY = -1;
            var scoopStartX = -1;

            if (mToken == TOKEN_STR_LITERAL)
                ParseSingleQuote();
            else
                ParseDoubleQuote();

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

            void ParseSingleQuote()
            {
                while (mToken == TOKEN_STR_LITERAL)
                {
                    // Read until end quote or end of line
                    BeginScoop(mToken);
                    literalTokens.Add(Accept());
                    while (mToken != TOKEN_STR_LITERAL && !(mToken.Meta && mToken == ";"))
                    {
                        if (mToken == "{")
                            ParseInterpolatedExpression();
                        else
                            literalTokens.Add(Accept());
                    }
                    EndScoop(mToken);

                    if (mToken == TOKEN_STR_LITERAL)
                    {
                        var prev = mToken;
                        literalTokens.Add(Accept());
                        if (mToken == TOKEN_STR_LITERAL && prev.Y == mToken.Y)
                            RejectToken(mToken, "Double quote not allowed in string");
                    }
                }
            }

            void ParseDoubleQuote()
            {
                // TBD: Scoop needs to collect "\n"
                while (mToken == TOKEN_STR_LITERAL_MULTI)
                {
                    // Read until end quote or end of line
                    BeginScoop(mToken);
                    literalTokens.Add(Accept());
                    while (mToken != TOKEN_STR_LITERAL_MULTI && mToken != "")
                    {
                        if (mToken == "$" && mEnum.PeekNoSpace() == "{")
                        {
                            Accept().Type = eTokenType.Reserved;
                            ParseInterpolatedExpression();
                        }
                        else
                            literalTokens.Add(Accept());
                    }
                    EndScoop(mToken);

                    if (mToken == TOKEN_STR_LITERAL_MULTI)
                    {
                        var prev = mToken;
                        literalTokens.Add(Accept());
                        if (mToken == TOKEN_STR_LITERAL_MULTI && prev.Y == mToken.Y)
                            RejectToken(mToken, "Double '``' not allowed in string");
                    }
                }
            }


            // Begin scooping after this token (called at beginning quote)
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
                if (len > 0)
                    literalSb.Append(mLexer.GetLine(scoopStartY).Substring(scoopStartX, len));
                scoopStartX = -1;
            }

            void ParseInterpolatedExpression()
            {
                EndScoop(mToken);
                Accept(); // "{"
                mPrevToken.Type = eTokenType.ReservedControl;

                if (mTokenName == "\\")
                    ParseEscapes();
                else if (mTokenName != TOKEN_STR_LITERAL) // String not allowed in string (user is probably typing)
                {
                    literalExpr.Add(ParseExpr());
                    literalSb.Append(STR_TEMP_REPLACE);
                }
                if (AcceptMatchOrReject("}", "Expecting '}' to end string interpolation"))
                    mPrevToken.Type = eTokenType.ReservedControl;

                BeginScoop(mPrevToken);
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
                return SyntaxError;

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
            if (AcceptMatchOrReject(expectedToken, $"Expecting '{expectedToken}' or ','"))
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

        // Excluding "type"
        bool BeginsType()
        {
            return mToken.Type == eTokenType.Identifier
                || sTypeUnaryOps.Contains(mTokenName)
                || mToken == "fun" || mToken == "afun" || mToken == "This";
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

            if (mToken == "This")
            {
                mToken.Type = eTokenType.TypeName;
                return new SyntaxToken(Accept());
            }

            if (mToken == "fun" || mToken == "afun")
            {
                // Type arguments
                var funKeyword = Accept();
                if (mTokenName == "<")
                    RejectToken(mToken, "Generic type args on lambda not supported YET!");
                return ParseMethodSignature(funKeyword, out var hasReturnType);
            }

            if (AcceptMatch("type"))
            {
                return ParseAnonymousClass(mPrevToken);
            }

            if (mToken.Type != eTokenType.Identifier)
            {
                AcceptIdentifier("Expecting a type name", sRejectTypeName);
                return SyntaxError;
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
                        return SyntaxError;
                    mToken.Type = eTokenType.TypeName;
                    result = new SyntaxBinary(dot, result, new SyntaxToken(Accept()));
                }
                else if (mTokenName == "<")
                {
                    accepted = true;
                    result = ParseTypeArguments(result);
                    if (mTokenName == "<")
                        RejectToken(mToken, "Illegal type argument list after type argument list");
                }
            } while (accepted);

            return result;
        }

        /// <summary>
        /// Parse type argument list: <Arg...>
        /// </summary>
        SyntaxExpr ParseTypeArguments(SyntaxExpr left)
        {
            Debug.Assert(mTokenName == "<");
            var openToken = Accept();
            var typeArgs = NewExprList();
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
        Token ParseIdentifier(string errorMessage, WordSet extraStops = null, WordSet allowExtraReservedWords = null)
        {
            AcceptIdentifier(errorMessage, extraStops, allowExtraReservedWords);
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
            if (AcceptMatch(match))
                return true;
            if (IsMatchPastMetaSemicolon(match))
            {
                Accept();
                Accept();
                return true;
            }
            return false;
        }

        bool IsMatchPastMetaSemicolon(string match)
        {
            if (mTokenName == match)
                return true;
            if (!mToken.Meta || mTokenName != ";")
                return false;
            return NextLineToken() == match;
        }

        // Return the token on the next line ignoring empty lines and comments
        Token NextLineToken()
        {
            int y = mToken.Y + 1;
            while (y < mLexer.LineCount)
            {
                var lt = mLexer.GetLineTokens(y);
                y++;
                if (lt.Length == 0 || lt[0] == TOKEN_COMMENT)
                    continue;
                return lt[0];
            }
            return EmptyToken;
        }

        // Accept match, otherwise reject until match token, then try one more time
        bool AcceptMatchOrReject(string matchToken, string message = null, bool tryToRecover = true)
        {
            if (AcceptMatch(matchToken))
                return true;
            Reject(message != null ? message : ("Expecting '" + matchToken + "'"), 
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
            public Lexer.Enumerator Enum;
            public Token PrevToken;
            public Token Token;
            public eTokenType TokenType;
            public int ParseErrors;
            public int MetaTokenCount;
            public int EndLineSemicolonsIndex;
        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.Enum = mEnum;
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            p.TokenType = mToken.Type;
            p.ParseErrors = mParseErrors;
            p.MetaTokenCount = mLexer.MetaTokens.Count;
            p.EndLineSemicolonsIndex = mEndLineSemicolonsIndex;
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mEnum = p.Enum;
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mToken.Type = p.TokenType;
            mTokenName = mToken.Name;
            mParseErrors = p.ParseErrors;
            while (mLexer.MetaTokens.Count > p.MetaTokenCount)
                mLexer.MetaTokens.RemoveAt(mLexer.MetaTokens.Count-1);
            mEndLineSemicolonsIndex = p.EndLineSemicolonsIndex;
        }

        Token Accept()
        {
            mPrevToken = mToken;

            GetNextToken();
            
            // Clear all errors and formatting, but preserve meta and continuation bits
            bool meta = mToken.Meta;
            bool continuation = mToken.Continuation;
            mToken.Clear();
            mToken.Meta = meta;
            mToken.Continuation = continuation;

            // Set token type
            if (mTokenName.Length == 0)
                mToken.Type = eTokenType.Normal;
            else if (mTokenName[0] == TOKEN_STR_LITERAL[0])
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
        }

        // Read past comments
        void GetNextToken()
        {
            // Read past comments
            while (true)
            {
                // Insert meta semicolon at end of line
                if (mEnum.Current != null && mEnum.Current.Eoln
                        && mEndLineSemicolonsIndex < mEndLineSemicolons.Count
                        && mEndLineSemicolons[mEndLineSemicolonsIndex].Y <= mEnum.Current.Y)
                {
                    mToken = mEndLineSemicolons[mEndLineSemicolonsIndex++];
                    mTokenName = mToken.Name;
                    return;
                }
                var line = mEnum.CurrentLineIndex;
                if (!mEnum.MoveNext(out mToken))
                {
                    mToken = EmptyToken;
                    mTokenName = mToken.Name;
                    return;
                }

                // Blank line clears the comment buffer
                if (mToken.Y > line+1)
                    mComments.Clear();

                if (mToken.Type == eTokenType.Comment)
                {
                    // Retrieve comment.
                    var x = mToken.X + mToken.Name.Length;
                    var comment = mLexer.GetLine(mToken.Y).Substring(x);
                    var commentTr = comment.Trim();

                    // Simple markdown
                    if (commentTr == "")
                        mComments.Append("\n\n"); // Blank is a paragraph
                    else if (comment.StartsWith("  ") || comment.StartsWith("\t"))
                    {
                        mComments.Append("\n");   // Indented is a line
                        mComments.Append(commentTr);
                    }
                    else
                        mComments.Append(commentTr);

                    mComments.Append(" ");
                    mEnum.SkipToEndOfLine();
                    continue;
                }
                break;
            }
            mTokenName = mToken.Name;
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
            if (name.Length >= 2 && name[0] == '_' && name[1] == '_')
                RejectToken(token, "Definition may not begin with '__'");
        }


        // Reject the given token
        public void RejectToken(Token token, string errorMessage, bool logDuplicates = false)
        {
            if (token.Error && !logDuplicates)
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
