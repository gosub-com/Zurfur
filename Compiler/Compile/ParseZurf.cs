using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;

using Gosub.Lex;
using Zurfur.Vm;

namespace Zurfur.Compiler;

public class ParseError : TokenError
{
    public ParseError(string message) : base(message) { }
}

/// <summary>
/// Base class for the parser
/// </summary>
class ParseZurf
{
    // NOTE: >=, >>, and >>= are omitted and handled at parser level.
    public const string MULTI_CHAR_TOKENS = ".* &* << <= == != && || ?? !! += -= *= /= %= &= |= ~= <<= => -> " 
        +$"!== === :: .. ..+ ... ++ -- // {TOKEN_STR_LITERAL_MULTI_BEGIN} {TOKEN_STR_LITERAL_MULTI_END} ```";


    public const string VT_TYPE_ARG = "$"; // Differentiate from '<' (must be 1 char long)
    public const string TOKEN_STR_LITERAL = "\"";
    public const string TOKEN_STR_LITERAL_MULTI_BEGIN = "``";
    public const string TOKEN_STR_LITERAL_MULTI_END = "``";
    public const string TOKEN_STR_INTEPOLATE = "#";
    public const string TOKEN_COMMENT = "//";

    // Probably will also allow 2, but must also require the entire file to be one way or the other.
    const int SCOPE_INDENT = 4;

    // TBD: Allow pragmas to be set externally
    static WordSet s_pragmas = new("ShowParse NoParse NoCompilerChecks NoVerify AllowUnderscoreDefinitions");


    ParseZurfCheck _zurfParseCheck;

    int                 _parseErrors;	// Number of errors
    Lexer				_lexer;			// Lexer to be parsed
    Lexer.Enumerator    _enum;          // Lexer enumerator
    string              _tokenName ="*"; // Skipped by first accept
    Token               _token = new(";");
    Token               _prevToken = new(";");
    StringBuilder       _comments = new();
    int                 _commentLineIndex;
    bool                _inTernary;
    bool                _allowUnderscoreDefinitions;
    List<Token>         _insertedTokens = new();
    int                 _insertedIndex;

    // Be kind to GC
    Queue<List<SyntaxExpr>> _exprCache = new();

    List<SyntaxScope> _scopeStack = new();
    SyntaxFile _syntax;

    public int ParseErrors => _parseErrors;

    // Add semicolons to all lines, except for:
    static WordSet s_continuationEnd = new("[ ( ,");
    static WordSet s_continuationNoBegin = new("} namespace mod type use pragma pub fun afun " 
        + "get set if while for return ret break continue else");
    static WordSet s_continuationBegin = new("] ) , . + - * / % | & || && and or not "
                        + "== != : ? ?? > << <= < => -> .. :: !== ===  is in as has "
                        + "= += -= *= /= %= |= &= ~= " + TOKEN_STR_LITERAL);

    static WordSet s_reservedWords = new("as has break case catch const "
        + "continue do then else elif todo extern nil true false defer use "
        + "finally for goto go if ife in is mod app include "
        + "new out pub public private priv readonly ro ref aref mut imut "
        + "return sizeof struct switch throw try nop implicit "
        + "typeof type unsafe static while dowhile scope loop "
        + "async await astart atask task get set var when nameof "
        + "box init move copy clone drop own super "
        + "fun afun sfun def yield let "
        + "dyn dynamic match from to of on "
        + "throws rethrow @ # and or not xor with exit pragma "
        + "of sync except exception loc local global self Self ");

    public static WordSet ReservedWords => s_reservedWords;

    static WordSet s_scopeQualifiers = new("pub public private unsafe implicit static");
    static WordSet s_fieldQualifiers = new("ro mut");
    static WordSet s_preTypeQualifiers = new("ro ref struct noclone unsafe enum union interface");
    static WordSet s_postFieldQualifiers = new("init mut ref");
    static WordSet s_paramQualifiers = new("ro own mut");

    static WordSet s_allowReservedFunNames = new("new drop");
    static WordSet s_reservedIdentifierVariables = new("nil true false new move sizeof typeof");
    static WordSet s_reservedMemberNames = new("clone");
    static WordSet s_typeUnaryOps = new("? ! * ^ [ & ro");

    static WordSet s_emptyWordSet = new("");

    static WordSet s_compareOps = new("== != < <= > >= === !== in");
    static WordSet s_rangeOps = new(".. ..+");
    static WordSet s_addOps = new("+ - |");
    static WordSet s_xorOps = new("~");
    static WordSet s_multiplyOps = new("* / % &");
    static WordSet s_assignOps = new("= += -= *= /= %= |= &= ~= <<= >>=");
    static WordSet s_unaryOps = new("+ - & &* ~ use unsafe clone mut not astart");

    // C# uses "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"  to resolve type
    // argument ambiguities. The following symbols allow us to call functions,
    // create types, and access static members. For example `F<T1>()` to
    // call a function or constructor and `F<T1>.Name` to access a static member.
    static WordSet s_typeArgumentParameterSymbols = new("( ) . , ;");

    Regex s_findUrl = new(@"///|//|`|((http|https|file|Http|Https|File|HTTP|HTTPS|FILE)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?)");

    static WordSet s_statementEndings = new("; }");
    static WordSet s_rejectAnyStop = new("; { }", true);
    static WordSet s_rejectForCondition = new("in");
    static WordSet s_rejectFuncParam = new(", )");
    static WordSet s_rejectTypeName = new("( )");

    static WordMap<string> s_stringLiterals = new()
        { { "n", "\n" }, { "r", "\r"}, {"rn", "\r\n"}, {"t", "\t"}, {"b", "\b" } };

    /// <summary>
    /// Parse the given lexer
    /// </summary>
    public ParseZurf(Lexer lexer)
    {
        _lexer = lexer;
        _enum = lexer.GetEnumerator();
        _syntax = new SyntaxFile() { Lexer = lexer };
        _zurfParseCheck = new ParseZurfCheck(this);
    }

    Token EmptyToken => _lexer.EndToken;
    SyntaxToken EmptyExpr => new SyntaxToken(_lexer.EndToken);
    SyntaxError SyntaxError => new SyntaxError(_lexer.EndToken);

    // Be kind to GC
    List<SyntaxExpr> NewExprList()
    {
        if (_exprCache.Count == 0)
            return new List<SyntaxExpr>();
        return _exprCache.Dequeue();
    }

    SyntaxExpr []FreeExprList(List<SyntaxExpr> expr)
    {
        var array = expr.ToArray();
        expr.Clear();
        if (_exprCache.Count < 10)
            _exprCache.Enqueue(expr);
        return array;
    }

    public SyntaxFile Parse()
    {
        _lexer.EndToken.Clear();
        _lexer.MetaTokensClear();

        if (Debugger.IsAttached)
        {
            // Failure causes error in dubugger
            ParseCompilationUnit();
            _zurfParseCheck.Check(_syntax);
        }
        else
        {
            try
            {
                ParseCompilationUnit();
                _zurfParseCheck.Check(_syntax);
            }
            catch (Exception ex1)
            {
                var errorMessage = "Parse failure: " + ex1.Message + "\r\n\r\n" + ex1.StackTrace;
                RejectToken(_token, errorMessage);
                if (_zurfParseCheck.LastToken != null)
                    RejectToken(_zurfParseCheck.LastToken, errorMessage);
                while (_tokenName != "")
                    Accept();
                var lexEnum = new Lexer.Enumerator(_lexer);
                lexEnum.MoveNext();
                RejectToken(lexEnum.Current, errorMessage);
            }
        }
        if (_tokenName != "")
            RejectToken(_token, "Parse error: Expecting end of file");
        while (_tokenName != "")
            Accept();

        return _syntax;
    }
    class NoCompilePragmaException : Exception { }

    /// <summary>
    /// Parse the file
    /// </summary>
    void ParseCompilationUnit()
    {
        try
        {
            var tokens = _lexer.GetLineTokens(0);
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

        var e = _lexer.GetEnumerator();
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
            else if (token == TOKEN_STR_LITERAL_MULTI_BEGIN)
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

            bool isContinueEnd = s_continuationEnd.Contains(prevNonCommentToken);
            bool isContinueBegin = s_continuationBegin.Contains(token.Name);
            bool isContinue = (isContinueEnd || isContinueBegin)
                                    && !s_continuationNoBegin.Contains(token.Name);

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
            var xIndex = _lexer.GetLine(prevNonCommentToken.Y).Length;
            if (token.X > scope + SCOPE_INDENT - 1)
            {
                // Add open braces '{'
                do {
                    scope += SCOPE_INDENT;
                    var openBrace = AddMetaToken(new Token("{", xIndex++, prevNonCommentToken.Y));
                    braceStack.Add((openBrace, prevNonContinuationLineToken?.Y??0));
                    _insertedTokens.Add(openBrace);
                } while (token.X > scope + SCOPE_INDENT - 1);
            }
            else if (token.X < scope - (SCOPE_INDENT - 1))
            {
                // Add close braces with semi-colons ';};'
                do {
                    // End statement before brace
                    _insertedTokens.Add(AddMetaToken(new Token(";", xIndex++, prevNonCommentToken.Y)));
                    scope -= SCOPE_INDENT;
                    var openBrace = braceStack.Pop();
                    var closeBrace = AddMetaToken(new Token("}", xIndex++, prevNonCommentToken.Y));
                    _insertedTokens.Add(closeBrace);
                    _insertedTokens.Add(AddMetaToken(new Token(";", xIndex++, prevNonCommentToken.Y)));

                    Token.AddScopeLines(_lexer, openBrace.y, closeBrace.Y - openBrace.y, false);

                    // TBD: This doesn't work because the tokens are cleared
                    //      when accepting them (see that TBD to see why)
                    //Connect(openBrace.token, closeBrace);

                } while (token.X < scope - (SCOPE_INDENT - 1));
            }
            else
            {
                _insertedTokens.Add(AddMetaToken(new Token(";",
                        xIndex++, prevNonCommentToken.Y)));
            }

            prevNonContinuationLineToken = token;
        }

        void ScanQuoteSingleLine()
        {
            // Single line quote (always ends at end of line)
            bool quoteEoln = token.Eoln;
            if (!token.Eoln)
                while (e.MoveNext(out token) && token != TOKEN_STR_LITERAL && !token.Eoln)
                    ; // Skip until end of quote (or line)
            if (quoteEoln || token.Eoln && token != TOKEN_STR_LITERAL)
            {
                // ERROR: Expecting end quote.  Mark it and add meta semicolon.
                // NOTE: Ideally we would add one meta semicolon and reject it,
                //       but `Accept` clears the error.
                prevNonCommentToken = null;
                RejectToken(AddMetaToken(new Token(" ", token.X + token.Name.Length, token.Y)),
                    "Expecting end quote before end of line");
                _insertedTokens.Add(AddMetaToken(new Token(";",
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
            //e.MoveNext(out token); // Skip begin quote token
            while (e.MoveNext(out token) && token != TOKEN_STR_LITERAL_MULTI_END && token != "")
                ; // Skip until end of quote (or file)
            if (token == "")
                RejectToken(token, $"Expecting {TOKEN_STR_LITERAL_MULTI_END} to end the multi-line string literal");
            else
                prevNonCommentToken = token;
        }

        // Call with tokenIndex pointing to "//"
        bool ScanComment(Token []tokens, int tokenIndex)
        {
            if (tokenIndex >= tokens.Length || tokens[tokenIndex] != TOKEN_COMMENT)
                return false;

            // Show code comments (inside backticks)
            tokens[tokenIndex].Type = TokenType.Comment;
            bool isCodeComment = false;
            for (int i = tokenIndex+1; i < tokens.Length; i++)
            {
                var t = tokens[i];
                t.Type = TokenType.Comment;
                if (t.Name == "`")
                {
                    isCodeComment = !isCodeComment;
                    t.Subtype = TokenSubType.Normal;
                }
                else
                {
                    // TBD: Subtype doesn't work here, so underline instead
                    t.Subtype = isCodeComment ? TokenSubType.CodeInComment : TokenSubType.Normal;
                    t.Underline = isCodeComment;
                }
            }

            // Add link to comments that look like URL's
            // TBD: Editor highlights each token individually.
            //      Either use one meta token link, or change editor to group these somehow.
            var commentToken = tokens[tokenIndex];
            var x = commentToken.X + commentToken.Name.Length;
            var comment = _lexer.GetLine(commentToken.Y).Substring(x);
            var m = s_findUrl.Matches(comment);
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
        for (int i = 0; i < _insertedTokens.Count - 1; i++)
            if (_insertedTokens[i].Location > _insertedTokens[i + 1].Location)
                throw new Exception("Additional meta-tokens must be sorted");

        Token? prevToken = null;
        var prevTokenHasError = false;
        for (var lineIndex = 0;  lineIndex < _lexer.LineCount;  lineIndex++)
        {
            var line = _lexer.GetLine(lineIndex);
            var tokens = _lexer.GetLineTokens(lineIndex);
            CheckTabsAndSemicolons(tokens, lineIndex, line);

            // Skip blank and comment lines
            if (tokens.Length == 0)
                continue;
            
            var token = tokens[0];
            if (token.Type == TokenType.Comment)
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
            while (i >= 0 && tokens[i].Type == TokenType.Comment)
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
        while (_tokenName != "")
        {
            if (_tokenName == "pragma")
                ParsePragma();
            else if (_tokenName == "mod")
            {
                if (ParseModuleStatement(Accept()))
                    break;
            }
            else if (!Reject("Expecting 'mod' or 'pragma' keyword"))
                Accept();
            if (_tokenName == ";")
                Accept();
        }
        if (_token == ";" && _token.Meta)
            Accept();
        if (_tokenName == "")
            return;

        while (_tokenName != "")
        {
            if (_token.X != 0 && _token != ";")
                RejectToken(_token, $"Incorrect indentation, module level statements must be in first column");
            ParseModuleScopeStatement(qualifiers);
        }
    }

    private void ParseModuleScopeStatement(List<Token> qualifiers)
    {
        // Read attributes and qualifiers
        ParseAttributes(qualifiers);

        var keyword = _token;
        switch (_tokenName)
        {
            case ";":
                Accept();
                break;

            case "pragma":
                ParsePragma();
                qualifiers.Clear();
                break;

            case "use":
                _token.Type = TokenType.ReservedControl;
                Accept();
                _syntax.Using.Add(ParseUsingStatement());
                if (_syntax.Types.Count != 0 || _syntax.Functions.Count != 0 || _syntax.Fields.Count != 0)
                    RejectToken(keyword, "'use' statement must come before any types, fields, or functions are defined");
                qualifiers.Clear();
                break;

            case "type":
                _token.Type = TokenType.ReservedControl;
                qualifiers.Add(Accept());
                ParseTypeScope(keyword, qualifiers);
                qualifiers.Clear();
                break;

            case "fun":
            case "afun":
                _token.Type = TokenType.ReservedControl;
                qualifiers.Add(Accept());
                ParseFunction(keyword, qualifiers, true);
                qualifiers.Clear();
                break;

            case "@":
            case "let":
            case "var":
                Accept();
                ParseFieldFull(qualifiers);
                qualifiers.Clear();
                break;

            case "const":
                keyword.Type = TokenType.ReservedControl;
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
            var open = _prevToken;
            if (s_scopeQualifiers.Contains(_token))
            {
                do
                {
                    if (s_scopeQualifiers.Contains(_token))
                        qualifiers.Add(Accept());
                } while (AcceptMatch(","));
            }
            else
            {
                attributes.Add(ParseExpr());
            }
            if (AcceptMatchOrReject("]"))
                Connect(_prevToken, open);
        }

        FreeExprList(attributes); // TBD: Store in expression tree
    }

    private void ParseQualifiers(WordSet allowedQualifiers, List<Token> qualifiers)
    {
        while (allowedQualifiers.Contains(_tokenName))
        {
            _token.Type = TokenType.Reserved;
            qualifiers.Add(Accept());
        }
    }

    void AddField(SyntaxField? field)
    {
        if (field == null)
            return; // Error already marked while parsing definition
        _syntax.Fields.Add(field);
    }

    SyntaxUsing ParseUsingStatement()
    {
        var moduleName = ParseQualifiedIdentifier("Expecting a module name identifier");
        var symbols = new List<Token>();
        if (AcceptMatch("["))
        {
            var openToken = _prevToken;
            do
            {
                if (_tokenName == "]")
                    break;
                if (!AcceptIdentifier("Expecting a symbol from the module"))
                    break;
                if (symbols.FindIndex(m => m.Name == _prevToken.Name) < 0)
                    symbols.Add(_prevToken);
                else
                    RejectToken(_prevToken, "Duplicate symbol");
            } while (AcceptMatch(","));
            if (AcceptMatchOrReject("]"))
                Connect(_prevToken, openToken);
        }

        return new SyntaxUsing(moduleName.ToArray(), symbols.ToArray());
    }

    bool ParseModuleStatement(Token keyword)
    {
        if (keyword.X != 0)
            RejectToken(_token, "'mod' statement must be in the first column");
        keyword.Type = TokenType.ReservedControl;

        var namePath = new List<Token>();
        do
        {
            if (!AcceptIdentifier("Expecting a module name identifier"))
                break;
            namePath.Add(_prevToken);
            RejectUnderscoreDefinition(_prevToken);
        } while (AcceptMatch("."));

        if (namePath.Count == 0)
            return false; // Rejected above

        // Each module goes on the scope stack
        for (int i = 0;  i <  namePath.Count;  i++)
            _scopeStack.Add(new SyntaxModule(keyword, namePath[i], i == 0 ? null : _scopeStack[i-1]));

        // Collect base module name
        var namePathStrArray = namePath.ConvertAll(token => token.Name).ToArray();
        var namePathStr = string.Join(".", namePathStrArray);
        var module = (SyntaxModule)_scopeStack[_scopeStack.Count - 1];
        _syntax.Modules[namePathStr] = module;

        // Accumulate comments and keyword tokens for this module
        module.Comments += " " + _comments;
        _comments.Clear();
        return true;
    }

    void ParsePragma()
    {
        _token.Type = TokenType.ReservedControl;
        Accept();
        _token.Type = TokenType.Reserved;
        if (_syntax.Pragmas.ContainsKey(_tokenName))
            RejectToken(_token, "Duplicate pragma");
        if (!s_pragmas.Contains(_tokenName))
            _token.AddError("Unkown pragma");
        _syntax.Pragmas[_tokenName] = new SyntaxPragma(_token);

        if (_tokenName == "__fail")
            throw new Exception("Parse fail test");
        if (_tokenName == "AllowUnderscoreDefinitions")
            _allowUnderscoreDefinitions = true;
        if (_tokenName == "NoParse")
        {
            while (Accept() != "")
            { }
            throw new NoCompilePragmaException();
        }
        Accept();
    }

    void ParseTypeScope(Token keyword, List<Token> qualifiers)
    {
        var comments = _comments.ToString();
        _comments.Clear();

        ParseQualifiers(s_preTypeQualifiers, qualifiers);

        // Parse type name
        if (!CheckIdentifier("Expecting a type name"))
            return;

        var typeName = Accept();
        typeName.Type = TokenType.TypeName;
        var typeArgs = ParseTypeParameters();
        var constraints = ParseConstraints(keyword);

        var synType = new SyntaxType(keyword, typeName) 
        {
            Parent = _scopeStack.Count == 0 ? null : _scopeStack.Last(),
            Comments = comments,
            Qualifiers = qualifiers.ToArray(),
            TypeArgs = typeArgs,
            Constraints = constraints ?? []
        };

        _syntax.Types.Add(synType);
        RejectUnderscoreDefinition(typeName);

        // Push new path
        var oldScopeStack = _scopeStack;
        _scopeStack = [.. oldScopeStack, synType];

        // Alias or 'is' type
        if (AcceptMatch("=") || AcceptMatch("is"))
        {
            var prev = _prevToken.Name;
            synType.Alias = ParseType();
            _scopeStack = oldScopeStack;
            return;
        }

        var qualifiers2 = new List<Token>();
        ParseTypeScopeStatements(synType, qualifiers2);

        // Restore old path
        _scopeStack = oldScopeStack;
    }

    private void ParseTypeScopeStatements(SyntaxType synType, List<Token> qualifiers2)
    {
        if (ExpectStartOfScope())
        {
            var openBrace = _prevToken;
            while (_token != "" && _token != "}")
            {
                ParseTypeScopeStatement(synType, qualifiers2);
                ExpectEndOfStatement();
            }
            if (ExpectEndOfScope())
                Connect(_prevToken, openBrace);
        }
    }

    private void ParseTypeScopeStatement(SyntaxType parent, List<Token> qualifiers)
    {
        // Read attributes and qualifiers
        ParseAttributes(qualifiers);
        var isInterface = Array.Find(parent.Qualifiers, a => a == "interface") != null;
        var isEnum = Array.Find(parent.Qualifiers, a => a == "enum") != null;

        switch (_tokenName)
        {
            case ";":
                break;

            case "{":
                RejectToken(_token, "Unnecessary scope is not allowed");
                ParseTypeScopeStatements(parent, qualifiers);
                break;

            case "const":
                if (isInterface || isEnum)
                    RejectToken(_token, $"Interfaces and enumerations may not contain 'const'");
                _token.Type = TokenType.ReservedControl;
                qualifiers.Add(Accept());
                AddField(ParseFieldSimple(qualifiers));
                qualifiers.Clear();
                break;

            case "fun":
            case "afun":
                _token.Type = TokenType.ReservedControl;
                qualifiers.Add(Accept());
                ParseFunction(_token, qualifiers, !isInterface);
                qualifiers.Clear();
                break;

            default:
                if (isEnum)
                    AddField(ParseEnumField(qualifiers));
                else if (isInterface)
                    RejectToken(_token, "Interface is expecting 'fun'");
                else
                    ParseFieldFull(qualifiers);

                qualifiers.Clear();
                break;
        }
    }

    SyntaxExpr? ParseTypeParameters()
    {
        if (!AcceptMatch("<"))
            return null;
        var openToken = _prevToken;
        var typeParams = NewExprList();
        var constraints = Array.Empty<SyntaxConstraint>();
        while (_token != ">"
            && AcceptIdentifier("Expecting '>' or a type parameter", s_rejectTypeName))
        {
            typeParams.Add(new SyntaxToken(_prevToken));
            if (_prevToken.Type == TokenType.Identifier)
                _prevToken.Type = TokenType.DefineTypeParam;

            if (AcceptMatch(","))
                Connect(openToken, _prevToken);
        }
        if (AcceptMatch(">"))
            Connect(openToken, _prevToken);

        return new SyntaxMulti(openToken, FreeExprList(typeParams));
    }

    // Parse "where" statement
    SyntaxConstraint[] ParseConstraints(Token keyword)
    {
        if (!AcceptMatchPastMetaSemicolon("where", keyword))
            return [];
        
        var constraints = new List<SyntaxConstraint>();
        do
        {
            if (AcceptIdentifier("Expecting type parameter name", s_rejectTypeName))
            {
                var typeName = _prevToken;
                typeName.Type = TokenType.TypeName;
                var constraintTypeNames = NewExprList();
                do
                {
                    constraintTypeNames.Add(ParseType());
                } while (AcceptMatch("+"));
                if (constraintTypeNames.Count != 0)
                    constraints.Add(new SyntaxConstraint(typeName, FreeExprList(constraintTypeNames)));
            }
        } while (AcceptMatch(","));
        return constraints.ToArray();
    }


    SyntaxField? ParseEnumField(List<Token> qualifiers)
    {
        // Variable name
        if (!AcceptIdentifier("Expecting field name"))
            return null;
        var newVarName = _prevToken;
        newVarName.Type = TokenType.DefineField;
        RejectUnderscoreDefinition(newVarName);

        var comments = _comments.ToString();
        _comments.Clear();

        // Optionally initialize via assignment
        SyntaxExpr? initializer = null;
        if (AcceptMatch("="))
            initializer = ParseExpr();

        var field = new SyntaxField(newVarName)
        {
            Parent = _scopeStack.Count == 0 ? null : _scopeStack.Last(),
            Qualifiers = qualifiers.ToArray(),
            Comments = comments,
            Initializer = initializer
        };

        return field;
    }

    SyntaxField? ParseFieldSimple(List<Token> qualifiers)
    {
        if (!AcceptIdentifier("Expecting field name"))
            return null;
        var newVarName = _prevToken;
        newVarName.Type = TokenType.DefineField;
        RejectUnderscoreDefinition(newVarName);

        var comments = _comments.ToString();
        _comments.Clear();

        SyntaxExpr? typeName = null;
        if (_tokenName != "=")
            typeName = ParseType();

        SyntaxExpr? initializer = null;
        if (_tokenName == "=")
            initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

        var field = new SyntaxField(newVarName)
        {
            Parent = _scopeStack.Count == 0 ? null : _scopeStack.Last(),
            Qualifiers = qualifiers.ToArray(),
            Comments = comments,
            Simple = true,
            TypeName = typeName,
            Initializer = initializer
        };


        return field;
    }

    private void ParseFieldFull(List<Token> qualifiers)
    {
        // Variable name
        if (!AcceptIdentifier("Expecting field name"))
            return;
        var newVarName = _prevToken;
        newVarName.Type = TokenType.DefineField;
        RejectUnderscoreDefinition(newVarName);

        while (s_fieldQualifiers.Contains(_token))
            qualifiers.Add(Accept()); ;

        // Type name
        var errors = _parseErrors;
        var typeName = ParseType();
        if (_parseErrors != errors)
            return;

        // Post field qualifiers
        if (_tokenName == "pub")
        {
            // TBD: Distinguish "ro @a int" from "@a int pub ro", same for "pub"
            qualifiers.Insert(0, Accept());
            while (s_postFieldQualifiers.Contains(_tokenName))
                qualifiers.Add(Accept());
        }

        // Initializer
        SyntaxExpr? initializer = null;
        if (_tokenName == "=")
            initializer = new SyntaxUnary(Accept(), ParseRightSideOfAssignment());

        var field = new SyntaxField(newVarName)
        {
            Parent = _scopeStack.Count == 0 ? null : _scopeStack.Last(),
            Qualifiers = qualifiers.ToArray(),
            Comments = _comments.ToString(),
            TypeName = typeName,
            Initializer = initializer,
        };
        _comments.Clear();
        AddField(field);
    }

    /// <summary>
    /// Function
    /// </summary>
    void ParseFunction(Token keyword, List<Token> qualifiers, bool allowBody)
    {
        // Parse func keyword
        var comments = _comments.ToString();
        _comments.Clear();

        if (_tokenName == "get" || _tokenName == "set")
        {
            _token.Type = TokenType.ReservedControl;
            qualifiers.Add(Accept());
        }

        // Parse function receiver type and name: [receiver.]name
        Token? receiverTypeName = null;
        Token? functionName = null;
        functionName = ParseFunctionName();
        if (AcceptMatch("."))
        {
            receiverTypeName = functionName;
            if (receiverTypeName != null)
                receiverTypeName.Type = TokenType.TypeName;
            functionName = ParseFunctionName();
        }

        var typeParams = ParseTypeParameters();
        var functionSignature = ParseFunctionSignature(keyword, true, true);
        var constraints = ParseConstraints(keyword);
        
        var requires = NewExprList();
        while (AcceptMatchPastMetaSemicolon("require", keyword))
            requires.Add(ParseExpr());

        // Body
        SyntaxExpr? statements = null;
        if (AcceptMatchPastMetaSemicolon("extern", keyword) || AcceptMatchPastMetaSemicolon("todo", keyword))
            qualifiers.Add(_prevToken);
        else if (allowBody)
            statements = ParseStatements();

        // Don't compile unless there is a valid function name
        if (functionName != null)
        {
            _syntax.Functions.Add(new SyntaxFunc(keyword, functionName)
            {
                Parent = _scopeStack.Count == 0 ? null : _scopeStack.Last(),
                Comments = comments,
                TypeParams = typeParams,
                ReceiverTypeName = receiverTypeName,
                Constraints = constraints ?? [],
                FunctionSignature = functionSignature,
                Requires = FreeExprList(requires),
                Statements = statements,
                Qualifiers = qualifiers.ToArray()
            });
        }
    }


    /// <summary>
    /// Returns true if we are a valid method name
    /// </summary>
    Token? ParseFunctionName()
    {
        // Function name
        if (!AcceptIdentifier("Expecting a function or type name", s_rejectTypeName, s_allowReservedFunNames))
            return null;

        // Shortcut receiver style
        var funcName = _prevToken;
        funcName.Type = s_allowReservedFunNames.Contains(funcName.Name) ? TokenType.Reserved : TokenType.DefineMethod;


        RejectUnderscoreDefinition(funcName);
        return funcName;
    }


    /// <summary>
    /// returns SyntaxExpr:
    ///     [0] - Parameters (name, type)
    ///     [1] - Returns (name, type) possibly blank for each
    ///     [2] - error/exit token
    /// </summary>
    private SyntaxExpr ParseFunctionSignature(Token keyword, bool allowEmptyFirstType, bool allowInitializer)
    {
        // Parameters
        var funcParams = ParseFunctionParams(allowEmptyFirstType, true);

        // Returns
        SyntaxExpr returnParams;
        if (_token == ("("))
        {
            returnParams = ParseFunctionParams(false, false);
        }
        else
        {
            // Single return parameter
            var returns = NewExprList();
            if (BeginsType())
            {
                // TBD: Param qualifiers probably need to be part of type
                var qualifiers = NewExprList();
                while (s_paramQualifiers.Contains(_token))
                    qualifiers.Add(new SyntaxToken(Accept()));

                returns.Add(new SyntaxMulti(EmptyToken, ParseType(), EmptyExpr,
                    new SyntaxMulti(EmptyToken, FreeExprList(qualifiers))));
            }
            returnParams = new SyntaxMulti(EmptyToken, FreeExprList(returns));
        }

        return new SyntaxMulti(keyword, funcParams, returnParams, EmptyExpr);
    }


    SyntaxExpr ParseFunctionParams(bool allowEmptyFirstType, bool allowInitializer)
    {
        // Read open token, '('
        if (!AcceptMatchOrReject("("))
            return SyntaxError;

        // Parse parameters
        var openToken = _prevToken;
        var parameters = NewExprList();
        if (_tokenName != ")")
            parameters.Add(ParseFunctionParam(allowEmptyFirstType, allowInitializer));
        while (AcceptMatch(","))
        {
            Connect(openToken, _prevToken);
            parameters.Add(ParseFunctionParam(false, allowInitializer));
        }

        if (AcceptMatchOrReject(")", "Expecting ')' or ','"))
            Connect(openToken, _prevToken);

        return new SyntaxMulti(EmptyToken, FreeExprList(parameters));
    }

    // Syntax Tree: variable name[type, initializer, qualifiers]
    SyntaxExpr ParseFunctionParam(bool allowEmptyType, bool allowInitializer)
    {
        var qualifiers = NewExprList();

        if (!AcceptIdentifier("Expecting a variable name", s_rejectFuncParam))
            return SyntaxError;

        var name = _prevToken;
        name.Type = TokenType.DefineFunParam;
        RejectUnderscoreDefinition(name);

        // TBD: Param qualifiers probably need to be part of type
        while (s_paramQualifiers.Contains(_token))
            qualifiers.Add(new SyntaxToken(Accept()));

        var type = (SyntaxExpr)EmptyExpr;
        if (!allowEmptyType || BeginsType())
            type = ParseType();

        var initializer = (SyntaxExpr)EmptyExpr;
        if (AcceptMatch("="))
        {
            if (!allowInitializer)
                RejectToken(_prevToken, "Initializer not allowed");
            initializer = ParseExpr();
        }
        return new SyntaxMulti(name, type, initializer, 
            new SyntaxMulti(EmptyToken, FreeExprList(qualifiers)));
    }

    SyntaxExpr ParseStatements()
    {
        if (!ExpectStartOfScope())
            return new SyntaxError(EmptyToken);

        var openBrace = _prevToken;
        var statement = NewExprList();
        while (_token != "" && _token != "}")
        {
            ParseStatement(statement);
            ExpectEndOfStatement();
        }
        if (ExpectEndOfScope())
            Connect(_prevToken, openBrace);

        return new SyntaxMulti(openBrace, FreeExprList(statement));
    }

    // Parse a statement.  `topLevelFunction` is null unless this is being
    // parsed at the top most level functipn
    private void ParseStatement(List<SyntaxExpr> statements)
    {
        var keyword = _token;
        switch (_token)
        {
            case "}":
                break;

            case ";":
                break;

            case "{":
                RejectToken(_token, "Unnecessary scope is not allowed");
                statements.Add(ParseStatements());
                break;
            
            case "defer":
            case "unsafe":
                statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                break;

            case "while":
                // WHILE (condition) (body)
                _token.Type = TokenType.ReservedControl;
                statements.Add(new SyntaxBinary(Accept(), ParseExpr(), ParseStatements()));
                break;

            case "scope":
                // SCOPE (body)
                _token.Type = TokenType.ReservedControl;
                statements.Add(new SyntaxUnary(Accept(), ParseStatements()));
                break;

            case "do":
                // DO (body)
                _token.Type = TokenType.ReservedControl;
                statements.Add(new SyntaxUnary(Accept(), ParseStatements()));
                break;

            case "dowhile":
                _token.Type = TokenType.ReservedControl;
                statements.Add(new SyntaxUnary(Accept(), ParseExpr()));
                break;                        

            case "if":
                _token.Type = TokenType.ReservedControl;
                Accept();
                statements.Add(new SyntaxBinary(keyword, ParseExpr(), ParseStatements()));
                break;

            case "elif":
            case "else":
                _token.Type = TokenType.ReservedControl;
                Accept();
                if (_prevToken == "elif" || AcceptMatch("if"))
                {
                    // `elif` or `else if`
                    if (_prevToken.Name == "if")
                        RejectToken(_prevToken, "Shorten to 'elif'");
                    _prevToken.Type = TokenType.ReservedControl;
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
                _token.Type = TokenType.ReservedControl;
                Accept();
                if (AcceptMatch("@") || AcceptMatch("var"))
                    _prevToken.Type = TokenType.NewVarSymbol;

                var forVariable = new SyntaxToken(ParseIdentifier("Expecting a loop variable", s_rejectForCondition));
                forVariable.Token.Type = TokenType.DefineLocal;
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
            case "yield":
            case "ret":
                keyword.Type = TokenType.ReservedControl;
                Accept();
                if (s_statementEndings.Contains(_tokenName))
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
                keyword.Type = TokenType.ReservedControl;
                statements.Add(new SyntaxToken(Accept()));
                break;

            case "fun":
            case "afun":
                // TBD: Process local functions
                //      Need to pass scope into ParseFunction
                //      Require `local` keyword
                var qualifiers = new List<Token>() { Accept() };
                keyword.Type = TokenType.ReservedControl;  // Fix keyword to make it control
                keyword.AddWarning("Local function not working yet");
                ParseFunction(keyword, qualifiers, true);
                break;

            default:
                if ((s_reservedWords.Contains(_tokenName) || _tokenName == "")
                    && !s_reservedIdentifierVariables.Contains(_tokenName)
                    && (_tokenName != "@" && _tokenName != "let" && _tokenName != "var"))
                {
                    RejectToken(_token, "Unexpected token or reserved word");
                    Accept();
                    break;
                }

                var result = ParseExpr();
                InterceptAndReplaceGT();
                if (s_assignOps.Contains(_token))
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
        if (_tokenName != ">")
            return;
        var peek = _enum.PeekNoSpace();
        if (peek != "=" && peek != ">")
            return;
        var token = _token;
        Accept();
        var metaToken = token.Name + peek;
        if (metaToken == ">>")
        {
            peek = _enum.PeekNoSpace();
            if (peek == "=")
            {
                Accept();
                metaToken = ">>=";
            }
        }
        // Replace with a virtual token
        _token = NewMetaToken(token, metaToken);
        _tokenName = _token.Name;
    }

    /// <summary>
    /// Parse the right side of an assignment statement
    /// </summary>
    SyntaxExpr ParseRightSideOfAssignment()
    {
        var result = ParseExpr();
        if (s_assignOps.Contains(_tokenName))
            Reject("Assignment operator is not associative, must use separate statement)");
        return result;
    }

    SyntaxExpr ParsePair()
    {
        var result = ParseExpr();
        if (_tokenName == ":")
        {
            result = new SyntaxBinary(Accept(), result, ParseExpr());
            if (_tokenName == ":")
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

        // Lambda parameter list
        if (_tokenName == "|")
        {
            var parameters = new SyntaxUnary(_prevToken, ParseNewVars());
            if (!AcceptMatchOrReject("=>"))
                return new SyntaxToken(_token);
            return ParseLambdaBody(_prevToken, parameters);
        }

        var result = ParseConditionalOr();

        // Lambda with single variable parameter
        if (_tokenName == "=>")
        {
            if (result.Count == 0 && result.Token.Type == TokenType.Identifier)
            {
                // Create AST for single variable lambda capture without explicit type
                result.Token.Type = TokenType.DefineLocal;
                result = new SyntaxUnary(EmptyToken, new SyntaxUnary(EmptyToken, new SyntaxUnary(result.Token, EmptyExpr)));
            }
            else
            {
                RejectToken(_token, "Left side must be a variable or parameter list, e.g. '|a,b|', etc.");
            }
            return ParseLambdaBody(Accept(), result);

        }
        return result;
    }

    private SyntaxExpr ParseLambdaBody(Token lambdaToken, SyntaxExpr parameters)
    {
        if (_token == "{")
            return new SyntaxBinary(lambdaToken, parameters, ParseStatements());
        else
            return new SyntaxBinary(lambdaToken, parameters, ParseConditionalOr());
    }

    SyntaxExpr ParseConditionalOr()
    {
        var result = ParseConditionalAnd();
        while (_tokenName == "or")
            result = new SyntaxBinary(Accept(), result, ParseConditionalAnd());
        return result;
    }

    SyntaxExpr ParseConditionalAnd()
    {
        var result = ParseComparison();
        while (_tokenName == "and")
            result = new SyntaxBinary(Accept(), result, ParseComparison());
        return result;
    }

    SyntaxExpr ParseComparison()
    {
        var result = ParseRange();
        InterceptAndReplaceGT();
        if (s_compareOps.Contains(_tokenName))
        {
            result = new SyntaxBinary(Accept(), result, ParseRange());
            InterceptAndReplaceGT();
            if (s_compareOps.Contains(_tokenName))
                Reject("Compare operators are not associative, must use parentheses");
        }
        return result;
    }


    SyntaxExpr ParseRange()
    {
        var result = s_rangeOps.Contains(_tokenName) ? EmptyExpr : ParseAdd();
        if (s_rangeOps.Contains(_tokenName))
        {
            result = new SyntaxBinary(Accept(), result, 
                _tokenName == ")" || _tokenName == "]" ? EmptyExpr : ParseAdd());
            if (s_rangeOps.Contains(_tokenName))
                Reject("Range operator is not associative, must use parentheses");
        }
        return result;
    }

    SyntaxExpr ParseAdd()
    {
        var result = ParseXor();
        while (s_addOps.Contains(_tokenName))
            result = new SyntaxBinary(Accept(), result, ParseXor());
        return result;
    }

    SyntaxExpr ParseXor()
    {
        var result = ParseMultiply();
        while (s_xorOps.Contains(_tokenName))
            result = new SyntaxBinary(Accept(), result, ParseMultiply());
        return result;
    }

    SyntaxExpr ParseMultiply()
    {
        var result = ParseShift();
        while (s_multiplyOps.Contains(_tokenName))
            result = new SyntaxBinary(Accept(), result, ParseShift());
        return result;
    }

    SyntaxExpr ParseShift()
    {
        var result = ParseIsAsCapture();
        InterceptAndReplaceGT();
        while (_tokenName == "<<" || _tokenName == ">>")
        {
            result = new SyntaxBinary(Accept(), result, ParseIsAsCapture());
            InterceptAndReplaceGT();
            if (_tokenName == "<<" || _tokenName == ">>")
                RejectToken(_token, "Shift operators are not associative, must use parentheses");
        }
        return result;
    }

    SyntaxExpr ParseIsAsCapture()
    {
        var result = ParseUnary();
        if (_tokenName == "is" || _tokenName == "as")
            result = new SyntaxBinary(Accept(), result, ParseType());
        else if (_tokenName == "@")
            result = new SyntaxBinary(Accept(), result, ParseNewVars());
        else if (_tokenName == "?")
            result = new SyntaxBinary(Accept(), result, ParseUnary());

        return result;
    }

    SyntaxExpr ParseUnary()
    {
        if (_tokenName == "@" || _tokenName == "let" || _tokenName == "var")
            return new SyntaxUnary(Accept(), ParseNewVars());

        if (s_unaryOps.Contains(_tokenName))
        {
            if (_tokenName == "+")
                RejectToken(_token, "Unary '+' operator is not allowed");
            return new SyntaxUnary(Accept(), ParseUnary());
        }

        if (_tokenName == "ife")
            return ParseIfe();

        var result = ParsePrimary();

        if (_tokenName == "!" || _tokenName == "!!")
            result = new SyntaxUnary(Accept(), result);
        return result;
    }

    SyntaxExpr ParseIfe()
    {
        if (Accept() != "ife")
            throw new Exception("Compiler error: Expecting 'ife'");

        var operatorToken = _prevToken;
        var result = ParseConditionalOr();

        if (!AcceptMatchOrReject(":", "Expecting ':' to separate 'ife' condition", false))
            return result;
        _prevToken.Type = TokenType.BoldSymbol;
        Connect(_prevToken, operatorToken);

        if (_inTernary)
            RejectToken(operatorToken, "'ife' expressions may not be nested");
        _inTernary = true;

        var firstConditional = ParseRange();
        if (!AcceptMatchOrReject(":", "Expecting ':' to separate 'ife' expressions", false))
        {
            _inTernary = false;
            return result;
        }
        _prevToken.Type = TokenType.BoldSymbol;
        Connect(_prevToken, operatorToken);
        result = new SyntaxMulti(operatorToken, result, firstConditional, ParseRange());
        _inTernary = false;

        return result;
    }

    private SyntaxExpr ParseNewVars()
    {
        var newVarList = NewExprList();
        if (AcceptMatch("|"))
        {
            var open = _prevToken;
            do
            {
                ParseNewVar(newVarList);
            } while (AcceptMatch(","));
            if (AcceptMatchOrReject("|", $"Expecting '|' or ','"))
                Connect(open, _prevToken);
        }
        else
        {
            ParseNewVar(newVarList);
        }

        if (_tokenName == "@" || _tokenName == "let" || _token == "var")
            Reject($"New variable operator '{_tokenName}' is not associative");

        return new SyntaxMulti(EmptyToken, FreeExprList(newVarList));
    }

    void ParseNewVar(List<SyntaxExpr> newVars)
    {
        if (!AcceptIdentifier("Expecting variable name"))
            return;
        var name = _prevToken;
        name.Type = TokenType.DefineLocal;
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
            if (_tokenName == "(" || _tokenName == "[")
            {
                // Function call or array access
                accepted = true;
                result = ParseParen(_tokenName, result);
            }
            else if (_tokenName == ".")
            {
                if (_enum.PeekNoSpace() == "(")
                {
                    // Type assertion
                    result = new SyntaxBinary(NewMetaToken(Accept(), ".("), ParseFunTakingType(), result);
                }
                else
                {
                    // Dot operator
                    result = new SyntaxBinary(Accept(), result,
                        new SyntaxToken(ParseIdentifier("Expecting identifier", null, s_reservedMemberNames)));
                }
                accepted = true;
            }
            else if (_tokenName == ".*")
            {
                // Dereference
                accepted = true;
                result = new SyntaxUnary(Accept(), result);
            }
            else if (_tokenName == "<")
            {
                // Possibly a type argument list.  Let's try it and find out.
                var p = SaveParsePoint();
                _parseErrors = 0;
                var typeArgs = ParseTypeArguments(result);
                if (_parseErrors == 0 && s_typeArgumentParameterSymbols.Contains(_tokenName))
                {
                    // Yes, it is a type argument list.  Keep it
                    _parseErrors = p.ParseErrors;
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
    /// Parse a function taking a type name (sizeof, typeof, etc)
    /// </summary>
    private SyntaxExpr ParseFunTakingType()
    {
        if (!AcceptMatchOrReject("("))
            return new SyntaxError(_token);
        var funcOpenToken = _prevToken;
        var funType = ParseType();
        if (AcceptMatchOrReject(")"))
            Connect(_prevToken, funcOpenToken);
        return funType;
    }

    /// <summary>
    /// Parse an atom - identifier, number, string literal, or parentheses
    /// </summary>
    SyntaxExpr ParseAtom()
    {
        if (_tokenName == "")
        {
            Reject("Unexpected end of file");
            return new SyntaxToken(_token);
        }

        // Parse parentheses: (expression) - not a function call.
        // Example: @a = [1,2,3]
        if (_tokenName == "(" || _tokenName == "[")
        {
            // Use ")" or "]" to differentiate between function call and ordering
            var close = _tokenName == "(" ? ")" : "]";
            var result = ParseParen(_tokenName, null);

            // Disallow old style casts (and other)
            if (close == ")" && (_tokenName == "(" || _token.Type == TokenType.Identifier))
            {
                var message = "Old style cast not allowed";
                RejectToken(_prevToken, message);
                Reject(message);
            }
            return result;
        }

        // Number, string, identifier
        if (_token.Type == TokenType.Number)
        {
            var numberToken = Accept();

            // Optionally accept an identifier after the number (e.g. `0f32`, etc.)
            if (_token.Type == TokenType.Identifier && !_token.Name.StartsWith("_"))
                return new SyntaxUnary(numberToken, new SyntaxToken(Accept()));
            return new SyntaxToken(numberToken);
        }
        if (_tokenName == TOKEN_STR_LITERAL || _tokenName == TOKEN_STR_LITERAL_MULTI_BEGIN)
        {
            return ParseStringLiteral(null);
        }
        if (_token.Type == TokenType.Identifier)
        {
            var identifier = Accept();
            if (_tokenName == TOKEN_STR_LITERAL || _tokenName == TOKEN_STR_LITERAL_MULTI_BEGIN)
            {
                identifier.Type = TokenType.Reserved;
                return ParseStringLiteral(identifier);
            }
            return new SyntaxToken(identifier);
        }
        // Misc reserved words
        if (s_reservedIdentifierVariables.Contains(_tokenName))
        {
            _token.Type = TokenType.Reserved;
            return new SyntaxToken(Accept());
        }

        var errorToken = _token;
        Reject("Expecting an identifier, number, string literal, or parentheses");
        return new SyntaxToken(errorToken);
    }

    /// <summary>
    /// Parse interpolated string: "string {expr} continue {\rn}"
    /// Or multi line string: """string ${expr} continye ${\r\n}"
    /// Prefix may be null, or 'tr'.  Next token must be the begin quote symbol.
    /// TBD: Store "tr" in the parse tree.
    /// </summary>
    SyntaxExpr ParseStringLiteral(Token? syntax)
    {
        const string STR_PARAM = "{?}";
        const string STR_TEMP_REPLACE = "\uF127"; // Anything unlikely to ever be seen in source code

        var quote = _token;
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
        }

        return new SyntaxUnary(quote, new SyntaxMulti(new Token(str), FreeExprList(literalExpr)));

        void ParseQuote(string beginQuote)
        {
            var terminator = beginQuote == TOKEN_STR_LITERAL ? TOKEN_STR_LITERAL : TOKEN_STR_LITERAL_MULTI_END;
            var multiLine = beginQuote == TOKEN_STR_LITERAL_MULTI_BEGIN;

            while (_token == beginQuote)
            {
                // Read until end quote or end of line
                BeginScoop(_token);
                literalTokens.Add(Accept());
                while (_token != terminator && _token != "" && !(_token.Meta && _token == ";"))
                {
                    if (!multiLine && _token == "{" || multiLine && _token == TOKEN_STR_INTEPOLATE && _enum.PeekNoSpace() == "{")
                    {
                        EndScoop(_token);
                        if (multiLine)
                            Accept().Type = TokenType.Reserved;
                        ParseInterpolatedExpression();
                    }
                    else
                    {
                        _token.Type = TokenType.QuoteText;
                        literalTokens.Add(Accept());
                    }
                }
                EndScoop(_token);

                if (_token == terminator)
                {
                    var prev = _token;
                    literalTokens.Add(Accept());
                    if (_token == terminator && prev.Y == _token.Y)
                        RejectToken(_token, $"Double '{terminator}' not allowed in string");
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
                x = _lexer.GetLine(scoopStartY).Length;
            var len = Math.Max(0, x - scoopStartX);
            if (len > 0)
                literalSb.Append(_lexer.GetLine(scoopStartY).Substring(scoopStartX, len));
            scoopStartX = -1;
        }

        void ParseInterpolatedExpression()
        {
            Accept(); // "{"
            _prevToken.Type = TokenType.ReservedControl;

            if (_tokenName == "\\")
                ParseEscapes();
            else if (_tokenName != TOKEN_STR_LITERAL) // String not allowed in string (user is probably typing)
            {
                literalExpr.Add(ParseExpr());
                literalSb.Append(STR_TEMP_REPLACE);
            }
            if (AcceptMatchOrReject("}", "Expecting '}' to end string interpolation"))
                _prevToken.Type = TokenType.ReservedControl;

            BeginScoop(_prevToken);
        }

        void ParseEscapes()
        {
            while (AcceptMatch("\\"))
            {
                _prevToken.Type = TokenType.Reserved;
                if (!s_stringLiterals.Contains(_tokenName))
                {
                    RejectToken(_token, "Expecting string literal constant, 'r', 'n', 'rn', 't', etc.");
                    if (_token.Type == TokenType.Identifier)
                        Accept();
                    continue;
                }

                _token.Type = TokenType.Reserved;
                literalSb.Append(s_stringLiterals[_token.Name]);
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
            return new SyntaxError(_token);

        // Parse parameters
        var openToken = _prevToken;
        var expectedToken = openToken == "(" ? ")" : "]";
        if (_tokenName != expectedToken)
        {
            parameters.Add(ParsePair());
            while (AcceptMatch(","))
            {
                Connect(openToken, _prevToken);
                parameters.Add(ParsePair());
            }
        }

        var keyword = openToken;
        if (AcceptMatchOrReject(expectedToken, $"Expecting '{expectedToken}' or ','"))
        {
            Connect(openToken, _prevToken);
            if (left != null)
                keyword = _prevToken;

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
                    openToken.Type = TokenType.BoldSymbol;
                    _prevToken.Type = TokenType.BoldSymbol;
                    foreach (var e in parameters)
                        e.Token.Type = TokenType.BoldSymbol;
                }
            }
        }
        else
        {
            // Put syntax error into expression so code generator
            // doesn't mark more errors
            parameters.Add(new SyntaxError(_token));
        }

        return new SyntaxMulti(keyword, FreeExprList(parameters));
    }

    bool BeginsType()
    {
        return _token.Type == TokenType.Identifier
            || s_typeUnaryOps.Contains(_tokenName)
            || s_paramQualifiers.Contains(_tokenName)
            || _token == "fun" || _token == "afun"
            || _token == "(";
    }

    SyntaxExpr ParseType()
    {
        if (s_typeUnaryOps.Contains(_tokenName))
        {
            var token = Accept();
            if (token.Type != TokenType.Reserved)
                token.Type = TokenType.TypeName;

            var tArg = NewMetaToken(token, VT_TYPE_ARG);
            var tName = new SyntaxToken(token);
            if (token == "!" && !BeginsType())
                return new SyntaxBinary(tArg, tName, new SyntaxToken(NewMetaToken(token, "()")));
            return new SyntaxBinary(tArg, tName, ParseType());
        }

        // Tuple
        if (_token == "(")
            return ParseTypeTuple();

        if (_token == "fun" || _token == "afun")
            return ParseFunctionSignature(Accept(), false, false);

        if (_token.Type != TokenType.Identifier)
        {
            AcceptIdentifier("Expecting a type name", s_rejectTypeName);
            return SyntaxError;
        }

        // Identifier
        _token.Type = TokenType.TypeName;
        var result = (SyntaxExpr)new SyntaxToken(Accept());
        bool accepted;
        do
        {
            accepted = false;
            if (_tokenName == ".")
            {
                accepted = true;
                var dot = Accept();
                _token.Type = TokenType.TypeName;
                result = new SyntaxBinary(dot, result, new SyntaxToken(Accept()));
            }
            else if (_tokenName == "<")
            {
                accepted = true;
                result = ParseTypeArguments(result);
                if (_tokenName == "<")
                    RejectToken(_token, "Illegal type argument list after type argument list");
            }
        } while (accepted);

        return result;
    }

    /// <summary>
    /// Parse type argument list: <Arg...>
    /// </summary>
    SyntaxExpr ParseTypeArguments(SyntaxExpr left)
    {
        Debug.Assert(_tokenName == "<");
        var openToken = Accept();
        var typeArgs = NewExprList();
        typeArgs.Add(left);
        typeArgs.Add(ParseType());
        while (AcceptMatch(","))
        {
            Connect(openToken, _prevToken);
            typeArgs.Add(ParseType());
        }

        if (!AcceptMatch(">"))
        {
            FreeExprList(typeArgs);
            Reject("Expecting '>' to end the type argument list", s_rejectTypeName);
            return left; // Allow syntax error recovery
        }
        Connect(openToken, _prevToken);
        return new SyntaxMulti(NewMetaToken(openToken, VT_TYPE_ARG), FreeExprList(typeArgs));
    }

    SyntaxExpr ParseTypeTuple()
    {
        Debug.Assert(_tokenName == "(");
        var openToken = Accept();
        var tupleArgs = NewExprList();
        if (_tokenName != ")")
            tupleArgs.Add(ParseTypeTupleElement());
        while (AcceptMatch(","))
        {
            Connect(openToken, _prevToken);
            tupleArgs.Add(ParseTypeTupleElement());
        }

        if (AcceptMatchOrReject(")", "Expecting ')' to end tuple type argument list"))
            Connect(openToken, _prevToken);
        return new SyntaxMulti(openToken, FreeExprList(tupleArgs));
    }

    SyntaxExpr ParseTypeTupleElement()
    {
        // Parse type or variable name
        var typeOrVariable = ParseType();
        if (!BeginsType())
            return new SyntaxUnary(EmptyToken, typeOrVariable);  // Just type

        // Variable name and type
        typeOrVariable.Token.Type = TokenType.Identifier;
        if (typeOrVariable.Count != 0)
            RejectToken(typeOrVariable.Token, "Illegal variable name");
        return new SyntaxUnary(typeOrVariable.Token, ParseType());
    }

    /// <summary>
    /// Parse a qualified identifier.  
    /// Error causes reject until errorStop and returns null.
    /// </summary>
    List<Token> ParseQualifiedIdentifier(string errorMessage)
    {
        // Parse first identifier
        var tokens = new List<Token>();
        if (!AcceptIdentifier(errorMessage))
            return tokens;
        tokens.Add(_prevToken);

        // Parse the rest
        while (AcceptMatch(".")
                &&  AcceptIdentifier(errorMessage))
        { 
            tokens.Add(_prevToken);
        }
        return tokens;
    }

    /// <summary>
    /// Parse an identifier.  Error causes reject until errorStop unless errorStop is null.
    /// </summary>
    Token ParseIdentifier(string errorMessage, WordSet? extraStops = null, WordSet? allowExtraReservedWords = null)
    {
        AcceptIdentifier(errorMessage, extraStops, allowExtraReservedWords);
        return _prevToken;
    }

    /// <summary>
    /// Parse an identifier.  Error returns false and causes
    /// reject until end of statement or extraStops hit
    /// </summary>
    bool AcceptIdentifier(string errorMessage, WordSet? extraStops = null, WordSet? allowExtraReservedWords = null)
    {
        if (allowExtraReservedWords != null && allowExtraReservedWords.Contains(_tokenName))
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
        if (_token.Type == TokenType.Identifier)
            return true;

        if (extraStops == null)
            extraStops = s_emptyWordSet;
        if (_token.Type == TokenType.Reserved || _token.Type == TokenType.ReservedControl)
            Reject(errorMessage + ", must not be a reserved word", extraStops);
        else
            Reject(errorMessage + ", must begin with a letter", extraStops);
        return false;
    }

    // Accept the token if it matches.  Returns true if it was accepted.
    bool AcceptMatch(string matchToken)
    {
        if (_tokenName == matchToken)
        {
            Accept();
            return true;
        }
        return false;
    }

    bool AcceptMatchPastMetaSemicolon(string match, Token keyword)
    {
        if (AcceptMatch(match))
        {
            _prevToken.Type = TokenType.Reserved;
            return true;
        }
        if (!_token.Meta || _tokenName != ";")
            return false;

        // Token on next line must match and line up under first token of this line
        if (_token.Y + 1 >= _lexer.LineCount)
            return false;
        var nextLineTokens = _lexer.GetLineTokens(_token.Y + 1);
        if (nextLineTokens.Length == 0 || nextLineTokens[0].Name != match || nextLineTokens[0].X != _lexer.GetLineTokens(keyword.Y)[0].X) 
            return false;
        Accept();
        Accept();
        Debug.Assert(_prevToken.Name == match);
        _prevToken.Type = TokenType.Reserved;
        return true;
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
        if (_token != "}")
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
        public TokenType TokenType;
        public int ParseErrors;
        public int MetaTokenCount;
        public int InsertedIndex;
    }

    ParsePoint SaveParsePoint()
    {
        var p = new ParsePoint();
        p.Enum = _enum;
        p.PrevToken = _prevToken;
        p.Token = _token;
        p.TokenType = _token.Type;
        p.ParseErrors = _parseErrors;
        p.MetaTokenCount = _lexer.MetaTokens.Count;
        p.InsertedIndex = _insertedIndex;
        return p;
    }

    void RestoreParsePoint(ParsePoint p)
    {
        _enum = p.Enum;
        _prevToken = p.PrevToken;
        _token = p.Token;
        _token.Type = p.TokenType;
        _tokenName = _token.Name;
        _parseErrors = p.ParseErrors;
        while (_lexer.MetaTokens.Count > p.MetaTokenCount)
            _lexer.MetaTokensRemoveAt(_lexer.MetaTokens.Count-1);
        _insertedIndex = p.InsertedIndex;
    }

    Token Accept()
    {
        _prevToken = _token;

        GetNextToken();
        SkipComments();

        // Clear all errors and formatting, but preserve meta and continuation bits.
        // TBD: This would be better done in ScanScopeStructure, but since we currently
        //      mark errors, then backtrack, we can't do this there.  Add flag to
        bool meta = _token.Meta;
        bool continuation = _token.Continuation;
        TokenVerticalLine []? verticalLine = _token.VerticalLine ? _token.GetInfos<TokenVerticalLine>() : null;
        _token.Clear();
        _token.Meta = meta;
        _token.Continuation = continuation;
        if (verticalLine != null)
            foreach (var info in verticalLine)
                _token.AddInfo(info);

        // Set token type
        if (_tokenName.Length == 0)
            _token.Type = TokenType.Normal;
        else if (_tokenName == TOKEN_STR_LITERAL || _tokenName == TOKEN_STR_LITERAL_MULTI_BEGIN || _tokenName == TOKEN_STR_LITERAL_MULTI_END)
            _token.Type = TokenType.QuoteMark;
        else if (_tokenName[0] >= '0' && _tokenName[0] <= '9')
            _token.Type = TokenType.Number;
        else if (s_reservedWords.Contains(_tokenName))
            _token.Type = TokenType.Reserved;
        else if (char.IsLetter(_tokenName[0]) || _tokenName[0] == '_')
            _token.Type = TokenType.Identifier;
        else
            _token.Type = TokenType.Normal;

        if (_token.OnlyTokenOnLine && (_tokenName == "{" || _tokenName == "}"))
            _token.Shrink = true;

        return _prevToken;
    }

    private void SkipComments()
    {
        // Skip comments and record then in mComments
        while (_token.Type == TokenType.Comment)
        {
            // Any non-comment line (including blank lines) clears the comment buffer
            if (_token.Y > _commentLineIndex + 1)
                _comments.Clear();
            _commentLineIndex = _token.Y;

            // Retrieve comment.
            var x = _token.X + _token.Name.Length;
            var comment = _lexer.GetLine(_token.Y).Substring(x);
            var commentTr = comment.Trim();

            // Simple markdown
            if (commentTr == "")
                _comments.Append("\n\n"); // Blank is a paragraph
            else if (comment.StartsWith("  ") || comment.StartsWith("\t"))
            {
                _comments.Append("\n");   // Indented is a line
                _comments.Append(commentTr);
            }
            else
                _comments.Append(commentTr);

            _comments.Append(" ");
            _enum.SkipToEndOfLine();
            GetNextToken();
        }
    }


    // Read past comments and insert meta tokens
    void GetNextToken()
    {
        // Read next token if previous one wasn't inserted
        if (!_token.Meta || (_tokenName != ";" && _tokenName != "{" && _tokenName != "}"))
            _enum.MoveNext();
        _token = _enum.Current ?? EmptyToken;
        _tokenName = _token.Name;

        // Insert meta tokens if necessary
        if (_insertedIndex < _insertedTokens.Count
                && _insertedTokens[_insertedIndex].Location <= _token.Location)
        {
            _token = _insertedTokens[_insertedIndex++];
            _tokenName = _token.Name;
        }
    }


    // Reject definitions beginning or ending with '_'
    void RejectUnderscoreDefinition(Token token)
    {
        if (_allowUnderscoreDefinitions)
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
        _parseErrors++;
        token.AddError(new ParseError(errorMessage));
    }

    // Reject the current token, then advance until the
    // end of line token or extraStops.
    // Returns TRUE if any token was accepted
    bool Reject(string errorMessage, WordSet? extraStops = null)
    {
        RejectToken(_token, errorMessage);
        if (extraStops == null)
            extraStops = s_emptyWordSet;

        bool accepted = false;
        while (!s_rejectAnyStop.Contains(_token)
                && !extraStops.Contains(_token))
        {
            _token.Grayed = true;
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
        _lexer.MetaTokensAdd(token);
        return token;
    }


}
