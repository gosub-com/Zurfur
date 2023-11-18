using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;

using Zurfur.Lex;
using Zurfur.Jit;

namespace Zurfur.Compiler
{
    public class ParseError : TokenError
    {
        public ParseError(string message) : base(message) { }
    }

    /// <summary>
    /// Base class for the parser
    /// </summary>
    class ParseZurf
    {
        public const string VT_TYPE_ARG = "$"; // Differentiate from '<' (must be 1 char long)
        public const string TOKEN_STR_LITERAL = "\"";
        public const string TOKEN_STR_LITERAL_MULTI = "\"\"\"";
        public const string TOKEN_COMMENT = "//";

        // Probably will also allow 2, but must also require
        // the entire file to be one way or the other.
        const int SCOPE_INDENT = 4;

        // TBD: Allow pragmas to be set externally
        static WordSet sPragmas = new WordSet("ShowParse NoParse NoCompilerChecks "
            + "NoVerify AllowUnderscoreDefinitions");


        ParseZurfCheck mZurfParseCheck;

        int                 mParseErrors;	// Number of errors
        Lexer				mLexer;			// Lexer to be parsed
        Lexer.Enumerator    mEnum;          // Lexer enumerator
        string              mTokenName ="*"; // Skipped by first accept
        Token               mToken = new(";");
        Token               mPrevToken = new(";");
        StringBuilder       mComments = new();
        int                 mCommentLineIndex;
        bool                mInTernary;
        bool                mAllowUnderscoreDefinitions;
        List<Token>         mInsertedTokens = new();
        int                 mInsertedIndex;

        // Be kind to GC
        Queue<List<SyntaxExpr>> mExprCache = new();

        List<SyntaxScope> mScopeStack = new();
        SyntaxFile mSyntax;

        public int ParseErrors => mParseErrors;

        // Add semicolons to all lines, except for:
        static WordSet sContinuationEnd = new WordSet("[ ( ,");
        static WordSet sContinuationNoBegin = new WordSet("} namespace mod type use pragma pub fun afun " 
            + "get set if while for return ret break continue else");
        static WordSet sContinuationBegin = new WordSet("] ) , . + - * / % | & || && and or "
                            + "== != : ? ?? > << <= < => -> .. :: !== ===  is in as has "
                            + "= += -= *= /= %= |= &= ~= " + TOKEN_STR_LITERAL);

        static WordSet sReservedWords = new WordSet("as has break case catch const "
            + "continue do then else elif todo extern nil true false defer use "
            + "finally for goto go if ife in is mod app include "
            + "new out pub public private priv readonly ro ref aref mut imut "
            + "return ret sizeof struct switch throw try nop "
            + "typeof type unsafe static while dowhile scope loop "
            + "async await astart atask task get set var when nameof "
            + "box init move copy clone drop own super self "
            + "extends impl implements fun afun sfun def yield yld let "
            + "any Any dyn Dyn dynamic Dynamic select match from to of on cofun "
            + "throws rethrow @ # and or not xor with exit pragma require ensure "
            + "of sync except exception loc local global my My");

        public static WordSet ReservedWords => sReservedWords;

        static WordSet sScopeQualifiers = new WordSet("pub public private unsafe unsealed static protected");
        static WordSet sFieldQualifiers = new WordSet("ro mut static");
        static WordSet sPreTypeQualifiers = new WordSet("ro ref struct noclone unsafe enum union interface");
        static WordSet sPostFieldQualifiers = new WordSet("init mut ref");
        static WordSet sParamQualifiers = new WordSet("ro own mut");

        static WordSet sReservedUserFuncNames = new WordSet("new clone drop");
        static WordSet sReservedIdentifierVariables = new WordSet("nil true false new move my sizeof typeof require");
        static WordSet sReservedMemberNames = new WordSet("clone");
        static WordSet sTypeUnaryOps = new WordSet("? ! * ^ [ & ro");

        static WordSet sEmptyWordSet = new WordSet("");

        static WordSet sCompareOps = new WordSet("== != < <= > >= === !== in");
        static WordSet sRangeOps = new WordSet(".. ..+");
        static WordSet sAddOps = new WordSet("+ - |");
        static WordSet sXorOps = new WordSet("~");
        static WordSet sMultiplyOps = new WordSet("* / % &");
        static WordSet sAssignOps = new WordSet("= += -= *= /= %= |= &= ~= <<= >>=");
        static WordSet sUnaryOps = new WordSet("+ - & &* ~ use unsafe clone mut not astart");

        // C# uses "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"  to resolve type
        // argument ambiguities. The following symbols allow us to call functions,
        // create types, and access static members. For example `F<T1>()` to
        // call a function or constructor and `F<T1>.Name` to access a static member.
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( ) . , ;");

        Regex sFindUrl = new Regex(@"///|//|`|((http|https|file|Http|Https|File|HTTP|HTTPS|FILE)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?)");

        static WordSet sStatementEndings = new WordSet("; }");
        static WordSet sRejectAnyStop = new WordSet("; { }", true);
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
            mLexer.MetaTokensClear();

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
                var tokens = mLexer.GetLineTokens(0);
                if (tokens.Length >= 2 && tokens[0] == "pragma" && tokens[1] == "NoParse")
                    return;
                ScanScopeStructure();
                ScanCheck();
                Accept();
                ParseTopScope();
            }
            catch (NoCompilePragmaException)
            {
            }
        }

        /// <summary>
        /// Scan for continuation lines, comments, quotes, and add braces and semicolons.
        /// </summary>
        void ScanScopeStructure()
        {
            int scope = 0;
            var braceStack = new List<(Token token, int y)>();
            Token? prevNonCommentToken = null;
            Token? prevNonContinuationLineToken = null;
            Token? token;

            var e = mLexer.GetEnumerator();
            while (e.MoveNext(out token))
            {
                while (token == TOKEN_COMMENT)
                {
                    ScanComment(e.CurrentLineTokens, e.CurrentLineTokenIndex-1);
                    e.SkipToEndOfLine();
                    e.MoveNext(out token);
                }

                if (token.Boln)
                    AddBracesAndSemicolons();

                if (token == TOKEN_STR_LITERAL)
                    ScanQuoteSingleLine();
                else if (token == TOKEN_STR_LITERAL_MULTI)
                    ScanQuoteMultiLine();
                else
                    prevNonCommentToken = token;
            }
            return;

            // Detect continuation lines, add braces and semicolons.
            void AddBracesAndSemicolons()
            {
                token.Continuation = false;
                if (token.VerticalLine)
                    token.RemoveInfo<TokenVerticalLine>();
                if (prevNonCommentToken == null)
                    return;

                bool isContinueEnd = sContinuationEnd.Contains(prevNonCommentToken);
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
                    return;
                }

                // Add scope/expression separators '{', '}', or ';'
                var xIndex = mLexer.GetLine(prevNonCommentToken.Y).Length;
                if (token.X > scope + SCOPE_INDENT - 1)
                {
                    // Add open braces '{'
                    do {
                        scope += SCOPE_INDENT;
                        var openBrace = AddMetaToken(new Token("{", xIndex++, prevNonCommentToken.Y));
                        braceStack.Add((openBrace, prevNonContinuationLineToken?.Y??0));
                        mInsertedTokens.Add(openBrace);
                    } while (token.X > scope + SCOPE_INDENT - 1);
                }
                else if (token.X < scope - (SCOPE_INDENT - 1))
                {
                    // Add close braces with semi-colons ';};'
                    do {
                        // End statement before brace
                        mInsertedTokens.Add(AddMetaToken(new Token(";", xIndex++, prevNonCommentToken.Y)));
                        scope -= SCOPE_INDENT;
                        var openBrace = braceStack.Pop();
                        var closeBrace = AddMetaToken(new Token("}", xIndex++, prevNonCommentToken.Y));
                        mInsertedTokens.Add(closeBrace);
                        mInsertedTokens.Add(AddMetaToken(new Token(";", xIndex++, prevNonCommentToken.Y)));

                        Token.AddScopeLines(mLexer, openBrace.y, closeBrace.Y - openBrace.y, false);

                        // TBD: This doesn't work because the tokens are cleared
                        //      when accepting them (see that TBD to see why)
                        //Connect(openBrace.token, closeBrace);

                    } while (token.X < scope - (SCOPE_INDENT - 1));
                }
                else
                {
                    mInsertedTokens.Add(AddMetaToken(new Token(";",
                            xIndex++, prevNonCommentToken.Y)));
                }

                prevNonContinuationLineToken = token;
            }

            void ScanQuoteSingleLine()
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
                    prevNonCommentToken = null;
                    RejectToken(AddMetaToken(new Token(" ", token.X + token.Name.Length, token.Y)),
                        "Expecting end quote before end of line");
                    mInsertedTokens.Add(AddMetaToken(new Token(";",
                            token.X + token.Name.Length, token.Y)));
                }
                else
                {
                    prevNonCommentToken = token;
                }
            }

            void ScanQuoteMultiLine()
            {
                // Multi line quote
                token.Type = eTokenType.Quote;
                while (e.MoveNext(out token) && token != TOKEN_STR_LITERAL_MULTI && token != "")
                    token.Type = eTokenType.Quote;
                if (token == "")
                    RejectToken(token, "Expecting \"\"\" to end the multi-line string literal");
                else
                    prevNonCommentToken = token;
            }

            // Call with tokenIndex pointing to "//"
            bool ScanComment(Token []tokens, int tokenIndex)
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
        void ScanCheck()
        {
            for (int i = 0; i < mInsertedTokens.Count - 1; i++)
                if (mInsertedTokens[i].Location > mInsertedTokens[i + 1].Location)
                    throw new Exception("Additional meta-tokens must be sorted");

            Token? prevToken = null;
            var prevTokenHasError = false;
            for (var lineIndex = 0;  lineIndex < mLexer.LineCount;  lineIndex++)
            {
                var line = mLexer.GetLine(lineIndex);
                var tokens = mLexer.GetLineTokens(lineIndex);
                CheckTabsAndSemicolons(tokens, lineIndex, line);

                // Skip blank and comment lines
                if (tokens.Length == 0)
                    continue;
                
                var token = tokens[0];
                if (token.Type == eTokenType.Comment)
                    continue;

                CheckAlignment(token);
            }
            return;

            void CheckAlignment(Token token)
            {
                var hasError = false;
                if (prevToken != null && token.Continuation && !prevToken.Continuation
                    && token.X < prevToken.X + SCOPE_INDENT)
                {
                    RejectToken(AddMetaToken(new Token(" ", token.X - 1, token.Y)),
                        "Continuation line must be indented one scope level past the line above it");
                    hasError = true;
                }
                else if (token.X % SCOPE_INDENT != 0)
                {
                    RejectToken(AddMetaToken(new Token(new string(' ', token.X % SCOPE_INDENT), 
                        token.X/SCOPE_INDENT*SCOPE_INDENT, token.Y)),
                        "First token be aligned on a scope level");
                    hasError = true;
                }
                if (prevToken != null && !prevTokenHasError 
                    && !token.Continuation && prevToken.Continuation 
                    && token.X + SCOPE_INDENT > prevToken.X)
                {
                    RejectToken(AddMetaToken(new Token(" ", prevToken.X-1, prevToken.Y)),
                        "Continuation line must be indented one scope level past the line below it");
                }
                prevTokenHasError = hasError;
                prevToken = token;
            }

            // TBD: Allow tabs in multi-line quotes
            void CheckTabsAndSemicolons(Token []tokens, int lineIndex, string line)
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

            // Search for 'mod' keyword
            while (mTokenName != "")
            {
                if (mTokenName == "pragma")
                    ParsePragma();
                else if (mTokenName == "mod")
                {
                    if (ParseModuleStatement(Accept()))
                        break;
                }
                else if (!Reject("Expecting 'mod' or 'pragma' keyword"))
                    Accept();
                if (mTokenName == ";")
                    Accept();
            }
            if (mToken == ";" && mToken.Meta)
                Accept();
            if (mTokenName == "")
                return;

            while (mTokenName != "")
            {
                if (mToken.X != 0 && mToken != ";")
                    RejectToken(mToken, $"Incorrect indentation, module level statements must be in first column");
                ParseModuleScopeStatement(qualifiers);
            }
        }

        private void ParseModuleScopeStatement(List<Token> qualifiers)
        {
            // Read attributes and qualifiers
            ParseAttributes(qualifiers);

            var keyword = mToken;
            switch (mTokenName)
            {
                case ";":
                    Accept();
                    break;

                case "pragma":
                    ParsePragma();
                    qualifiers.Clear();
                    break;

                case "use":
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    mSyntax.Using.Add(ParseUsingStatement());
                    if (mSyntax.Types.Count != 0 || mSyntax.Functions.Count != 0 || mSyntax.Fields.Count != 0)
                        RejectToken(keyword, "'use' statement must come before any types, fields, or functions are defined");
                    qualifiers.Clear();
                    break;

                case "type":
                    mToken.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    ParseTypeScope(keyword, qualifiers);
                    qualifiers.Clear();
                    break;

                case "fun":
                case "afun":
                    mToken.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    ParseFunction(keyword, qualifiers, true);
                    qualifiers.Clear();
                    break;

                case "@":
                    Accept();
                    ParseFieldFull(qualifiers);
                    qualifiers.Clear();
                    break;

                case "const":
                    keyword.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    AddField(ParseFieldSimple(qualifiers));
                    qualifiers.Clear();
                    break;

                default:
                    Accept();
                    RejectToken(keyword, "Expecting  '@' (field), 'type', 'fun', 'const', etc. or qualifier ('pub', etc.)");
                    qualifiers.Clear();
                    break;
            }
        }

        private void ParseAttributes(List<Token> qualifiers)
        {
            var attributes = NewExprList();
            while (AcceptMatch("["))
            {
                var open = mPrevToken;
                if (sScopeQualifiers.Contains(mToken))
                {
                    while (sScopeQualifiers.Contains(mToken))
                        qualifiers.Add(Accept());
                }
                else
                    attributes.Add(ParseExpr());

                if (AcceptMatchOrReject("]"))
                    Connect(mPrevToken, open);
            }

            FreeExprList(attributes); // TBD: Store in expression tree
        }

        private void ParseQualifiers(WordSet allowedQualifiers, List<Token> qualifiers)
        {
            while (allowedQualifiers.Contains(mTokenName))
            {
                mToken.Type = eTokenType.Reserved;
                qualifiers.Add(Accept());
            }
        }

        void AddField(SyntaxField? field)
        {
            if (field == null)
                return; // Error already marked while parsing definition
            mSyntax.Fields.Add(field);
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

        bool ParseModuleStatement(Token keyword)
        {
            if (keyword.X != 0)
                RejectToken(mToken, "'mod' statement must be in the first column");
            keyword.Type = eTokenType.ReservedControl;

            var namePath = new List<Token>();
            do
            {
                if (!AcceptIdentifier("Expecting a module name identifier"))
                    break;
                namePath.Add(mPrevToken);
                RejectUnderscoreDefinition(mPrevToken);
            } while (AcceptMatch("."));

            if (namePath.Count == 0)
                return false; // Rejected above

            // Each module goes on the scope stack
            for (int i = 0;  i <  namePath.Count;  i++)
                mScopeStack.Add(new SyntaxModule(keyword, namePath[i], i == 0 ? null : mScopeStack[i-1]));

            // Collect base module name
            var namePathStrArray = namePath.ConvertAll(token => token.Name).ToArray();
            var namePathStr = string.Join(".", namePathStrArray);
            var module = (SyntaxModule)mScopeStack[mScopeStack.Count - 1];
            mSyntax.Modules[namePathStr] = module;

            // Accumulate comments and keyword tokens for this module
            module.Comments += " " + mComments;
            mComments.Clear();
            return true;
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

        void ParseTypeScope(Token keyword, List<Token> qualifiers)
        {
            var synType = new SyntaxType(keyword);
            synType.Parent = mScopeStack.Count == 0 ? null : mScopeStack.Last();
            synType.Comments = mComments.ToString();
            mComments.Clear();

            ParseQualifiers(sPreTypeQualifiers, qualifiers);
            synType.Qualifiers = qualifiers.ToArray();

            // Parse type name
            if (!CheckIdentifier("Expecting a type name"))
                return;
            synType.Name = Accept();
            synType.Name.Type = eTokenType.TypeName;
            (synType.TypeArgs, synType.Constraints) = ParseTypeParameters();

            mSyntax.Types.Add(synType);
            RejectUnderscoreDefinition(synType.Name);

            // Push new path
            var oldScopeStack = mScopeStack;
            mScopeStack = new List<SyntaxScope>(oldScopeStack);
            mScopeStack.Add(synType);

            // Simple struct
            if (AcceptMatch("("))
            {
                synType.Simple = true;
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

            // Alias or 'is' type
            if (AcceptMatch("=") || AcceptMatch("is"))
            {
                var prev = mPrevToken.Name;
                synType.Simple = true;
                synType.Alias = ParseType();
                mScopeStack = oldScopeStack;
                return;
            }

            var qualifiers2 = new List<Token>();
            ParseTypeScopeStatements(synType, qualifiers2);

            // Restore old path
            mScopeStack = oldScopeStack;
        }

        private void ParseTypeScopeStatements(SyntaxType synType, List<Token> qualifiers2)
        {
            if (ExpectStartOfScope())
            {
                var openBrace = mPrevToken;
                while (mToken != "" && mToken != "}")
                {
                    ParseTypeScopeStatement(synType, qualifiers2);
                    ExpectEndOfStatement();
                }
                if (ExpectEndOfScope())
                    Connect(mPrevToken, openBrace);
            }
        }

        private void ParseTypeScopeStatement(SyntaxType parent, List<Token> qualifiers)
        {
            // Read attributes and qualifiers
            ParseAttributes(qualifiers);
            var isInterface = Array.Find(parent.Qualifiers, a => a == "interface") != null;
            var isEnum = Array.Find(parent.Qualifiers, a => a == "enum") != null;

            switch (mTokenName)
            {
                case ";":
                    break;

                case "{":
                    RejectToken(mToken, "Unnecessary scope is not allowed");
                    ParseTypeScopeStatements(parent, qualifiers);
                    break;


                case "where":
                    RejectToken(Accept(), "'where' is reserved in this context");
                    break;

                case "const":
                    if (isInterface || isEnum)
                        RejectToken(mToken, $"Interfaces and enumerations may not contain 'const'");
                    mToken.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    AddField(ParseFieldSimple(qualifiers));
                    qualifiers.Clear();
                    break;

                case "fun":
                case "afun":
                    if (!isInterface)
                        RejectToken(mToken, $"Only interfaces may not contain '{mTokenName}'");
                    mToken.Type = eTokenType.ReservedControl;
                    qualifiers.Add(Accept());
                    ParseFunction(mToken, qualifiers, !isInterface);
                    qualifiers.Clear();
                    break;

                default:
                    if (isEnum)
                        AddField(ParseEnumField(qualifiers));
                    else if (isInterface)
                        RejectToken(mToken, "Interface is expecting 'fun'");
                    else
                        ParseFieldFull(qualifiers);

                    qualifiers.Clear();
                    break;
            }
        }


        (SyntaxExpr? typeParams, SyntaxConstraint[]? constraints) ParseTypeParameters()
        {
            if (!AcceptMatch("["))
                return (null, null);
            var openToken = mPrevToken;
            var typeParams = NewExprList();
            var constraints = Array.Empty<SyntaxConstraint>();
            while (mToken != "]"
                && AcceptIdentifier("Expecting ']' or a type parameter", sRejectTypeName))
            {
                typeParams.Add(new SyntaxToken(mPrevToken));
                if (mPrevToken.Type == eTokenType.Identifier)
                    mPrevToken.Type = eTokenType.TypeName;

                if (mToken.Type == eTokenType.Identifier)
                {
                    var constraint = ParseConstraint(mPrevToken);
                    if (constraint != null)
                        constraints = constraints.Append(constraint).ToArray();
                }

                if (AcceptMatch(","))
                    Connect(openToken, mPrevToken);
            }
            if (AcceptMatch("]"))
                Connect(openToken, mPrevToken);

            return (new SyntaxMulti(openToken, FreeExprList(typeParams)), constraints);
        }

        SyntaxConstraint? ParseConstraint(Token typeToken)
        {
            if (mToken.Type != eTokenType.Identifier)
                return null;

            var constraint = new SyntaxConstraint();
            constraint.TypeName = typeToken;
            var constraintTypeNames = NewExprList();
            while (mToken.Type == eTokenType.Identifier)
            {
                constraintTypeNames.Add(ParseType());
                AcceptMatch("+");
            }
            constraint.TypeConstraints = FreeExprList(constraintTypeNames);
            return constraint;
        }

        SyntaxField? ParseEnumField(List<Token> qualifiers)
        {
            // Variable name
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

            // Optionally initialize
            if (AcceptMatch("="))
            {
                // Initialize via assignment
                field.Initializer = ParseExpr();
            }
            return field;
        }

        SyntaxField? ParseFieldSimple(List<Token> qualifiers)
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

        private void ParseFieldFull(List<Token> qualifiers)
        {
            // Variable name
            if (!AcceptIdentifier("Expecting field name"))
                return;
            var newVarName = mPrevToken;
            newVarName.Type = eTokenType.DefineField;
            RejectUnderscoreDefinition(newVarName);

            while (sFieldQualifiers.Contains(mToken))
                qualifiers.Add(Accept()); ;

            // Type name
            var errors = mParseErrors;
            var typeName = ParseType();
            if (mParseErrors != errors)
                return;

            // Post field qualifiers
            if (mTokenName == "pub")
            {
                // TBD: Distinguish "ro @a int" from "@a int pub ro", same for "pub"
                qualifiers.Insert(0, Accept());
                while (sPostFieldQualifiers.Contains(mTokenName))
                    qualifiers.Add(Accept());
            }

            // Initializer
            SyntaxExpr? initializer = null;
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
        /// Function
        /// </summary>
        void ParseFunction(Token keyword, List<Token> qualifiers, bool body)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc(keyword);
            synFunc.Parent = mScopeStack.Count == 0 ? null : mScopeStack.Last();
            synFunc.Comments = mComments.ToString();
            mComments.Clear();

            if (mTokenName == "get" || mTokenName == "set")
            {
                mToken.Type = eTokenType.ReservedControl;
                qualifiers.Add(Accept());
            }

            var validFunctionName = ParseExtensionTypeAndMethodName(out synFunc.ExtensionType, out synFunc.Name);
            (synFunc.TypeArgs, synFunc.Constraints) = ParseTypeParameters();
            synFunc.FunctionSignature = ParseFunctionSignature(keyword);

            // Body
            if (AcceptMatch("extern") || AcceptMatch("todo"))
                qualifiers.Add(mPrevToken);
            else if (body)
                synFunc.Statements = ParseStatements(synFunc);

            synFunc.Qualifiers = qualifiers.ToArray();

            if (validFunctionName)
                mSyntax.Functions.Add(synFunc);
        }


        /// <summary>
        /// Returns true if we are a valid method name
        /// </summary>
        bool ParseExtensionTypeAndMethodName(
            out SyntaxExpr? extensionType,
            out Token funcName)
        {
            extensionType = null;
            funcName = mToken;

            var mutToken = mToken == "mut" ? Accept() : null;

            // TBD: Verifier to ensure this function not defined anywhere other than Zurfur module                
            if (mToken == "require" || mToken == "new" ||  mToken == "drop")
                mToken.Type = eTokenType.Identifier;

            if (!CheckIdentifier("Expecting a function or property name", sRejectTypeName))
                return false;

            var nameExpr = ParseType();

            // Old style generic type args (always an error now)
            if (nameExpr.Count >= 2 && nameExpr.Token == VT_TYPE_ARG)
            {
                foreach (var t in nameExpr.Skip(1))
                    RejectToken(t.Token, "Old type parameter syntax not accepted");
                nameExpr = nameExpr[0];
            }

            // Extension type
            if (nameExpr.Token == "." && nameExpr.Count >= 2)
            {
                extensionType = nameExpr[0];
                nameExpr = nameExpr[1];
            }

            // TBD: Record 'mut' token for static functions inside interfaces
            if (mutToken != null && extensionType != null)
                extensionType = new SyntaxUnary(mutToken, extensionType);
            else if (mutToken != null)
                mutToken.AddWarning("Not stored in parse tree yet");

            if (nameExpr.Count != 0)
                RejectToken(nameExpr.Token, "Expecting a function name");

            funcName = nameExpr.Token;
            funcName.Type = eTokenType.DefineMethod;

            RejectUnderscoreDefinition(funcName);
            return nameExpr.Count == 0;
        }


        /// <summary>
        /// returns SyntaxExpr:
        ///     [0] - Parameters (name, type)
        ///     [1] - Returns (name, type) possibly blank for each
        ///     [2] - error/exit token
        /// </summary>
        private SyntaxExpr ParseFunctionSignature(Token keyword)
        {
            // Parameters
            var funcParams = ParseFunctionParams();

            // Returns
            SyntaxExpr returnParams;
            if (mToken == ("("))
            {
                returnParams = ParseFunctionParams();
            }
            else
            {
                // Single return parameter
                var returns = NewExprList();
                if (BeginsType())
                {
                    // TBD: Param qualifiers probably need to be part of type
                    var qualifiers = NewExprList();
                    while (sParamQualifiers.Contains(mToken))
                        qualifiers.Add(new SyntaxToken(Accept()));

                    returns.Add(new SyntaxMulti(EmptyToken, ParseType(), EmptyExpr,
                        new SyntaxMulti(EmptyToken, FreeExprList(qualifiers))));
                }
                returnParams = new SyntaxMulti(EmptyToken, FreeExprList(returns));
            }

            return new SyntaxMulti(keyword, funcParams, returnParams, EmptyExpr);
        }


        SyntaxExpr ParseFunctionParams()
        {
            // Read open token, '('
            if (!AcceptMatchOrReject("("))
                return SyntaxError;

            // Parse parameters
            var openToken = mPrevToken;
            var parameters = NewExprList();
            if (mTokenName != ")")
                parameters.Add(ParseFunctionParam());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseFunctionParam());
            }

            if (AcceptMatchOrReject(")", "Expecting ')' or ','"))
                Connect(openToken, mPrevToken);

            return new SyntaxMulti(EmptyToken, FreeExprList(parameters));
        }

        // Syntax Tree: (name, type, initializer, qualifiers)
        SyntaxExpr ParseFunctionParam()
        {
            var qualifiers = NewExprList();

            if (!AcceptIdentifier("Expecting a variable name", sRejectFuncParam))
                return SyntaxError;
            var name = mPrevToken;
            name.Type = eTokenType.DefineParam;
            RejectUnderscoreDefinition(name);

            if (AcceptMatch("my"))
                qualifiers.Add(new SyntaxToken(mPrevToken));

            // TBD: Param qualifiers probably need to be part of type
            while (sParamQualifiers.Contains(mToken))
                qualifiers.Add(new SyntaxToken(Accept()));

            var type = ParseType();
            var initializer = (SyntaxExpr)EmptyExpr;
            if (AcceptMatch("="))
                initializer = ParseExpr();
            return new SyntaxMulti(name, type, initializer, 
                new SyntaxMulti(EmptyToken, FreeExprList(qualifiers)));
        }

        SyntaxExpr ParseStatements(SyntaxFunc ?topLevelFunction = null)
        {
            if (!ExpectStartOfScope())
                return new SyntaxError(EmptyToken);

            var openBrace = mPrevToken;
            var statement = NewExprList();
            while (mToken != "" && mToken != "}")
            {
                ParseStatement(statement, topLevelFunction);
                ExpectEndOfStatement();
            }
            if (ExpectEndOfScope())
                Connect(mPrevToken, openBrace);

            return new SyntaxMulti(openBrace, FreeExprList(statement));
        }

        // Parse a statement.  `topLevelFunction` is null unless this is being
        // parsed at the top most level functipn
        private void ParseStatement(List<SyntaxExpr> statements, SyntaxFunc ?topLevelFunction)
        {
            var keyword = mToken;
            switch (mToken)
            {
                case "}":
                    break;

                case ";":
                    break;

                case "extern":
                    if (topLevelFunction == null)
                        RejectToken(mToken, "The 'extern' qualifier can only be used at the top level function scope");
                    else if (statements.Count != 0)
                        // TBD: Error for statements following this one
                        RejectToken(mToken, "The 'extern' qualifier must be the only statement in the function");
                    else
                        topLevelFunction.Qualifiers = topLevelFunction.Qualifiers.Append(mToken).ToArray();
                    Accept();
                    break;

                case "where":
                    RejectToken(Accept(), "'where' is reserved in this context");
                    break;

                case "=>":
                    RejectToken(mToken, "Unexpected '=>'");
                    Accept();
                    break;


                case "{":
                    RejectToken(mToken, "Unnecessary scope is not allowed");
                    statements.Add(ParseStatements());
                    break;
                
                case "defer":
                case "unsafe":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    break;

                case "while":
                    // WHILE (condition) (body)
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements()));
                    break;

                case "scope":
                    // SCOPE (body)
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxUnary(Accept(), ParseStatements()));
                    break;

                case "do":
                    // DO (body)
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxUnary(Accept(), ParseStatements()));
                    break;

                case "dowhile":
                    mToken.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    break;                        

                case "if":
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    statements.Add(new SyntaxBinary(keyword, ParseExpr(), ParseStatements()));
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
                        statements.Add(new SyntaxBinary(keyword, ParseExpr(), ParseStatements()));
                    }
                    else
                    {
                        // `else`
                        statements.Add(new SyntaxUnary(keyword, ParseStatements()));
                    }
                    break;

                case "for":
                    // FOR (variable) (condition) (statements)
                    mToken.Type = eTokenType.ReservedControl;
                    Accept();
                    if (AcceptMatch("@"))
                        mPrevToken.Type = eTokenType.NewVarSymbol;
                    else
                        RejectToken(mToken, "Expecting '@'");

                    var forVariable = new SyntaxToken(ParseIdentifier("Expecting a loop variable", sRejectForCondition));
                    forVariable.Token.Type = eTokenType.DefineLocal;
                    RejectUnderscoreDefinition(forVariable.Token);
                    AcceptMatchOrReject("in");
                    var forCondition = ParseExpr();
                    statements.Add(new SyntaxMulti(keyword, forVariable, forCondition, ParseStatements()));
                    break;

                case "astart":
                    statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                    break;

                case "todo":
                case "nop":
                    statements.Add(new SyntaxToken(Accept()));
                    break;

                case "throw":
                case "return":
                case "ret":
                case "yld":
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
                        expressions.Add(ParseExpr());
                    } while (AcceptMatch(","));
                    statements.Add(new SyntaxMulti(keyword, FreeExprList(expressions)));
                    break;

                case "continue":
                case "break":
                    keyword.Type = eTokenType.ReservedControl;
                    statements.Add(new SyntaxToken(Accept()));
                    break;

                case "fun":
                case "afun":
                    // TBD: Process local functions
                    //      Need to pass scope into ParseFunction
                    //      Require `local` keyword
                    var qualifiers = new List<Token>() { Accept() };
                    keyword.Type = eTokenType.ReservedControl;  // Fix keyword to make it control
                    keyword.AddWarning("Local function not working yet");
                    ParseFunction(keyword, qualifiers, true);
                    break;

                default:
                    if ((sReservedWords.Contains(mTokenName) || mTokenName == "")
                        && !sReservedIdentifierVariables.Contains(mTokenName)
                        && mTokenName != "@")
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
                if (mToken == "{")
                    result = new SyntaxBinary(lambdaToken, result, ParseStatements());
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
            if (mTokenName == "??")
            {
                if (mInTernary)
                    RejectToken(mToken, "Ternary expressions may not be nested");
                mInTernary = true;
                mToken.Type = eTokenType.BoldSymbol;
                var operatorToken = Accept();
                var firstConditional = ParseRange();
                if (mTokenName != ":")
                {
                    mInTernary = false;
                    Reject("Expecting a ':' to separate expression for the ternary '?' operator");
                    return result;
                }
                mToken.Type = eTokenType.BoldSymbol;
                Connect(mToken, operatorToken);
                Accept();
                result = new SyntaxMulti(operatorToken, result, firstConditional, ParseRange());
                mInTernary = false;

                if (mTokenName == "??")
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
            var result = ParseIsAsCapture();
            InterceptAndReplaceGT();
            while (mTokenName == "<<" || mTokenName == ">>")
            {
                result = new SyntaxBinary(Accept(), result, ParseIsAsCapture());
                InterceptAndReplaceGT();
                if (mTokenName == "<<" || mTokenName == ">>")
                    RejectToken(mToken, "Shift operators are not associative, must use parentheses");
            }
            return result;
        }

        SyntaxExpr ParseIsAsCapture()
        {
            var result = ParseUnary();
            if (mTokenName == "is" || mTokenName == "as")
                result = new SyntaxBinary(Accept(), result, ParseType());
            else if (mTokenName == "@")
                result = new SyntaxBinary(Accept(), result, ParseNewVars());
            else if (mTokenName == "?")
                result = new SyntaxBinary(Accept(), result, ParseUnary());

            return result;
        }

        SyntaxExpr ParseUnary()
        {
            if (mTokenName == "@")
                return new SyntaxUnary(Accept(), ParseNewVars());

            if (sUnaryOps.Contains(mTokenName))
            {
                if (mTokenName == "+")
                    RejectToken(mToken, "Unary '+' operator is not allowed");
                return new SyntaxUnary(Accept(), ParseUnary());
            }

            var result = ParsePrimary();

            if (mTokenName == "!" || mTokenName == "!!")
                result = new SyntaxUnary(Accept(), result);
            return result;
        }

        /// <summary>
        /// Parse a function taking a type name (sizeof, typeof, etc)
        /// </summary>
        private SyntaxExpr ParseFunTakingType()
        {
            if (!AcceptMatchOrReject("("))
                return new SyntaxError(mToken);
            var funcOpenToken = mPrevToken;
            var funType = ParseType();
            if (AcceptMatchOrReject(")"))
                Connect(mPrevToken, funcOpenToken);
            return funType;
        }

        private SyntaxExpr ParseNewVars()
        {
            var newVarList = NewExprList();
            if (AcceptMatch("("))
            {
                var open = mPrevToken;
                do
                {
                    ParseNewVar(newVarList);
                } while (AcceptMatch(","));
                if (AcceptMatchOrReject(")", "Expecting ')' or ','"))
                    Connect(open, mPrevToken);
            }
            else
            {
                ParseNewVar(newVarList);
            }

            if (mTokenName == "@")
                Reject("New variable operator '@' is not associative");

            return new SyntaxMulti(EmptyToken, FreeExprList(newVarList));
        }

        void ParseNewVar(List<SyntaxExpr> newVars)
        {
            if (!AcceptIdentifier("Expecting variable name"))
                return;
            var name = mPrevToken;
            name.Type = eTokenType.DefineLocal;
            RejectUnderscoreDefinition(name);
            var typeExpr = BeginsType() ? ParseType() : EmptyExpr;
            newVars.Add(new SyntaxUnary(name, typeExpr));
        }

        SyntaxExpr ParsePrimary()
        {
            var result = ParseAtom();

            if (result.Token == "sizeof" || result.Token == "typeof")
            {
                result = new SyntaxUnary(result.Token, ParseFunTakingType());
            }

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
                        result = new SyntaxBinary(NewMetaToken(Accept(), ".("), ParseFunTakingType(), result);
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
                    var p = SaveParsePoint();
                    mParseErrors = 0;
                    var typeArgs = ParseTypeArguments(result);
                    if (mParseErrors == 0 && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Yes, it is a type argument list.  Keep it
                        mParseErrors = p.ParseErrors;
                        accepted = true;
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
                var numberToken = Accept();

                // Optionally accept an identifier after the number (e.g. `0f32`, etc.)
                if (mToken.Type == eTokenType.Identifier && !mToken.Name.StartsWith("_"))
                    return new SyntaxUnary(numberToken, new SyntaxToken(Accept()));
                return new SyntaxToken(numberToken);
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
        /// Parse interpolated string: "string {expr} continue {\rn}"
        /// Or multi line string: """string ${expr} continye ${\r\n}"
        /// Prefix may be null, or 'tr'.  Next token must be quote symbol.
        /// TBD: Store "tr" in the parse tree.
        /// </summary>
        SyntaxExpr ParseStringLiteral(Token? syntax)
        {
            const string STR_PARAM = "{?}";
            const string STR_TEMP_REPLACE = "\uF127"; // Anything unlikely to ever be seen in source code

            var quote = mToken;
            var literalTokens = new List<Token>();
            var literalSb = new StringBuilder();
            var literalExpr = NewExprList();
            var scoopStartY = -1;
            var scoopStartX = -1;

            ParseQuote(quote);

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
                token.AddInfo(new ParseInfo(strPrint));
                token.Type = eTokenType.Quote;
            }

            return new SyntaxUnary(quote, new SyntaxMulti(new Token(str), FreeExprList(literalExpr)));

            void ParseQuote(string terminator)
            {
                bool multiLine = terminator == TOKEN_STR_LITERAL_MULTI;
                while (mToken == terminator)
                {
                    // Read until end quote or end of line
                    BeginScoop(mToken);
                    literalTokens.Add(Accept());
                    while (mToken != terminator && mToken != "" && !(mToken.Meta && mToken == ";"))
                    {
                        if (!multiLine && mToken == "{"
                            || multiLine && mToken == "$" && mEnum.PeekNoSpace() == "{")
                        {
                            EndScoop(mToken);
                            if (multiLine)
                                Accept().Type = eTokenType.Reserved;
                            ParseInterpolatedExpression();
                        }
                        else
                            literalTokens.Add(Accept());
                    }
                    EndScoop(mToken);

                    if (mToken == terminator)
                    {
                        var prev = mToken;
                        literalTokens.Add(Accept());
                        if (mToken == terminator && prev.Y == mToken.Y)
                            RejectToken(mToken, $"Double '{terminator}' not allowed in string");
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
        SyntaxExpr ParseParen(string expecting, SyntaxExpr? left)
        {
            var parameters = NewExprList();
            if (left != null)
                parameters.Add(left);

            // Read open token, '(' or '['
            if (!AcceptMatchOrReject(expecting))
                return new SyntaxError(mToken);

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
            else
            {
                // Put syntax error into expression so code generator
                // doesn't mark more errors
                parameters.Add(new SyntaxError(mToken));
            }

            return new SyntaxMulti(keyword, FreeExprList(parameters));
        }

        bool BeginsType()
        {
            return mToken.Type == eTokenType.Identifier
                || sTypeUnaryOps.Contains(mTokenName)
                || sParamQualifiers.Contains(mTokenName)
                || mToken == "fun" || mToken == "afun"
                || mToken == "(";
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

                var tArg = NewMetaToken(token, VT_TYPE_ARG);
                var tName = new SyntaxToken(token);
                if (token == "!" && !BeginsType())
                    return new SyntaxBinary(tArg, tName, new SyntaxToken(NewMetaToken(token, "()")));
                return new SyntaxBinary(tArg, tName, ParseType());
            }

            // Tuple
            if (mToken == "(")
                return ParseTypeTuple();

            if (mToken == "fun" || mToken == "afun")
                return ParseFunctionSignature(Accept());

            if (mToken.Type != eTokenType.Identifier)
            {
                AcceptIdentifier("Expecting a type name", sRejectTypeName);
                return SyntaxError;
            }

            // Identifier
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

                    if (!sReservedUserFuncNames.ContainsKey(mTokenName) && !CheckIdentifier("Expecting type name"))
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

        SyntaxExpr ParseTypeTuple()
        {
            Debug.Assert(mTokenName == "(");
            var openToken = Accept();
            var tupleArgs = NewExprList();
            if (mTokenName != ")")
                tupleArgs.Add(ParseTypeTupleElement());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                tupleArgs.Add(ParseTypeTupleElement());
            }

            if (AcceptMatchOrReject(")", "Expecting ')' to end tuple type argument list"))
                Connect(openToken, mPrevToken);
            return new SyntaxMulti(openToken, FreeExprList(tupleArgs));
        }

        SyntaxExpr ParseTypeTupleElement()
        {
            // Parse type or variable name
            var typeOrVariable = ParseType();
            if (!BeginsType())
                return new SyntaxUnary(EmptyToken, typeOrVariable);  // Just type

            // Variable name and type
            typeOrVariable.Token.Type = eTokenType.Identifier;
            if (typeOrVariable.Count != 0)
                RejectToken(typeOrVariable.Token, "Illegal variable name");
            return new SyntaxUnary(typeOrVariable.Token, ParseType());
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
        Token ParseIdentifier(string errorMessage, WordSet? extraStops = null, WordSet? allowExtraReservedWords = null)
        {
            AcceptIdentifier(errorMessage, extraStops, allowExtraReservedWords);
            return mPrevToken;
        }

        /// <summary>
        /// Parse an identifier.  Error returns false and causes
        /// reject until end of statement or extraStops hit
        /// </summary>
        bool AcceptIdentifier(string errorMessage, WordSet? extraStops = null, WordSet? allowExtraReservedWords = null)
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

        bool CheckIdentifier(string errorMessage, WordSet? extraStops = null)
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

        bool ExpectStartOfScope()
        {
            return AcceptMatchOrReject("{", "Expecting statements, either '{' or next line must be indented");
        }

        bool ExpectEndOfScope()
        {
            return AcceptMatchOrReject("}", "Expecting end of statements, either '}' or next line must be outdented");
        }

        // Expect either ';' or '}', anything else is an error.  Don't eat '}'
        void ExpectEndOfStatement()
        {
            if (mToken != "}")
                AcceptMatchOrReject(";", "Expecting end of line or ';' after statement");
        }

        // Accept match, otherwise reject until match token, then try one more time
        bool AcceptMatchOrReject(string matchToken, string? message = null, bool tryToRecover = true)
        {
            if (AcceptMatch(matchToken))
                return true;
            Reject(message != null ? message : ("Expecting '" + matchToken + "'"), 
                        tryToRecover ? new WordSet(matchToken) : null);
            if (tryToRecover)
                return AcceptMatch(matchToken);
            return false;
        }

        struct ParsePoint
        {
            public Lexer.Enumerator Enum;
            public Token PrevToken;
            public Token Token;
            public eTokenType TokenType;
            public int ParseErrors;
            public int MetaTokenCount;
            public int InsertedIndex;
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
            p.InsertedIndex = mInsertedIndex;
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
                mLexer.MetaTokensRemoveAt(mLexer.MetaTokens.Count-1);
            mInsertedIndex = p.InsertedIndex;
        }

        Token Accept()
        {
            mPrevToken = mToken;

            GetNextToken();
            SkipComments();

            // Clear all errors and formatting, but preserve meta and continuation bits.
            // TBD: This would be better done in ScanScopeStructure, but since we currently
            //      mark errors, then backtrack, we can't do this there.  Add flag to
            bool meta = mToken.Meta;
            bool continuation = mToken.Continuation;
            TokenVerticalLine []? verticalLine = mToken.VerticalLine ? mToken.GetInfos<TokenVerticalLine>() : null;
            mToken.Clear();
            mToken.Meta = meta;
            mToken.Continuation = continuation;
            if (verticalLine != null)
                foreach (var info in verticalLine)
                    mToken.AddInfo(info);

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

        private void SkipComments()
        {
            // Skip comments and record then in mComments
            while (mToken.Type == eTokenType.Comment)
            {
                // Any non-comment line (including blank lines) clears the comment buffer
                if (mToken.Y > mCommentLineIndex + 1)
                    mComments.Clear();
                mCommentLineIndex = mToken.Y;

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
                GetNextToken();
            }
        }


        // Read past comments and insert meta tokens
        void GetNextToken()
        {
            // Read next token if previous one wasn't inserted
            if (!mToken.Meta || (mTokenName != ";" && mTokenName != "{" && mTokenName != "}"))
                mEnum.MoveNext();
            mToken = mEnum.Current ?? EmptyToken;
            mTokenName = mToken.Name;

            // Insert meta tokens if necessary
            if (mInsertedIndex < mInsertedTokens.Count
                    && mInsertedTokens[mInsertedIndex].Location <= mToken.Location)
            {
                mToken = mInsertedTokens[mInsertedIndex++];
                mTokenName = mToken.Name;
            }
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

        // Reject the current token, then advance until the
        // end of line token or extraStops.
        // Returns TRUE if any token was accepted
        bool Reject(string errorMessage, WordSet? extraStops = null)
        {
            RejectToken(mToken, errorMessage);
            if (extraStops == null)
                extraStops = sEmptyWordSet;

            bool accepted = false;
            while (!sRejectAnyStop.Contains(mToken)
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
            mLexer.MetaTokensAdd(token);
            return token;
        }


    }

}
