﻿
using System.Diagnostics;

namespace Gosub.Lex;

/// <summary>
/// Lexical analyzer - scan and separate tokens in a file.
/// Provide services for the editor to modify the text and re-tokenize whenever it changes.  
/// Tokens never contain new lines, but may contain spaces.
/// File always end with "" (i.e. `EndToken`), but never contain one in the middle.
/// Defaults to ScanText scanner.
/// </summary>
sealed public class Lexer
{
    // Strings and tokens buffer
    List<string> _lines = new();
    List<Token[]> _tokens = new();
    List<Token> _metaTokens = new();
    Scanner _scanner = new ScanText();

    /// <summary>
    /// Get or set the lexer path or url.
    /// </summary>
    public string Path { get; set; } = "";

    // Be kind to the GC
    List<Token> _tokenBuffer = new();


    /// <summary>
    /// This token returned at end of file, always "".
    /// It is also the first metatoken.
    /// </summary>
    public Token EndToken { get; private set; } = new Token("", 0, 2, TokenFlags.Boln | TokenFlags.Eoln | TokenFlags.Meta);

    /// <summary>
    /// Optional location of cursor (y = -1 if not set)
    /// </summary>
    public TokenLoc Cursor = new TokenLoc(0, -1);

    public Lexer()
    {
        // Must have at least one line of text
        _lines.Add("");
        _tokens.Add([]);
        _metaTokens.Add(EndToken);
    }

    public Lexer(Scanner scanner) : this()
    {
        _scanner = scanner;
    }

    /// <summary>
    /// NOTE: Lexer dafaults to object comparison, so don't change that
    /// behavior now.  Use this function to compare text files.
    /// </summary>
    public bool Equals(Lexer lex)
    {
        if (lex == this)
            return true;
        if (LineCount != lex.LineCount)
            return false;
        for (int i = 0; i < LineCount; i++)
            if (_lines[i] != lex._lines[i])
                return false;
        return true;
    }

    public override string ToString()
    {
        return _lines.Count.ToString() + " Lines";
    }

    /// <summary>
    /// Defaults to ScanText.  Re-scans all text when set (unless it's the same object)
    /// </summary>
    public Scanner Scanner
    {
        set
        {
            if (_scanner == value)
                return;
            _scanner = value;
            Scan(_lines.ToArray());
        }
        get
        {
            return _scanner;
        }
    }

    /// <summary>
    /// Clone the lexer and all tokens.  All markup information is discarded.
    /// </summary>
    public Lexer Clone()
    {
        var lex = new Lexer();
        lex.Path = Path;
        lex._lines = new List<string>(_lines);
        lex._tokens.Clear();
        foreach (var tokenLine in _tokens)
        {
            var newTokens = new Token[tokenLine.Length];
            for (int i = 0; i < newTokens.Length; i++)
                newTokens[i] = tokenLine[i].Clone();
            lex._tokens.Add(newTokens);
        }
        lex.EndToken.Y = lex._tokens.Count;
        foreach (var token in _metaTokens.Skip(1))
            lex._metaTokens.Add(token.Clone());
        lex.Scanner = Scanner;
        lex.Cursor = Cursor;
        return lex;
    }

    /// <summary>
    /// Meta tokens, a fancy word for "extra" tokens used to markup the text.
    /// Includes invisible tokens with errors (like the ';') and other stuff
    /// that could be generated by the parser. 
    /// </summary>
    public IReadOnlyList<Token> MetaTokens
    {
        get => _metaTokens;
    }

    public void MetaTokensAdd(Token meta)
    {
        meta.Meta = true;
        _metaTokens.Add(meta);
    }

    public void MetaTokensClear()
    {
        _metaTokens.Clear();
        _metaTokens.Add(EndToken);
    }
    // Don't delete 
    public void MetaTokensRemoveAt(int index)
    {
        Debug.Assert(index != 0);
        _metaTokens.RemoveAt(index);
    }

    /// <summary>
    /// Returns the number of lines of text
    /// </summary>
    public int LineCount { get { return _lines.Count; } }

    /// <summary>
    /// Returns a line of text
    /// </summary>
    public string GetLine(int index) { return _lines[index]; }

    public Token[] GetLineTokens(int line) { return _tokens[line]; }

    /// <summary>
    /// Returns v, bounded by min and max (or min if min >= max)
    /// </summary>
    int Bound(int v, int min, int max)
    {
        return Math.Max(Math.Min(v, max), min);
    }

    /// <summary>
    /// Gets a section of text.
    /// </summary>
    public string[] GetText(TokenLoc start, TokenLoc end)
    {
        // Bounds check parameters
        start.Y = Bound(start.Y, 0, _lines.Count - 1);
        start.X = Bound(start.X, 0, _lines[start.Y].Length);
        end.Y = Bound(end.Y, 0, _lines.Count - 1);
        end.X = Bound(end.X, 0, _lines[end.Y].Length);
        if (end.Y < start.Y)
            end.Y = start.Y;
        if (start.Y == end.Y && end.X < start.X)
            end.X = start.X;

        int startIndex = start.X;
        int endIndex = end.X;

        if (start.Y == end.Y && startIndex >= endIndex)
            return new string[0];

        if (start.Y == end.Y)
        {
            return new string[] { _lines[start.Y].Substring(startIndex, endIndex - startIndex) };
        }

        // Break up the first and last line at the start position
        string[] lines = new string[end.Y - start.Y + 1];
        lines[0] = _lines[start.Y].Substring(startIndex);
        for (int i = 1; i < lines.Length - 1; i++)
            lines[i] = _lines[start.Y + i];
        lines[end.Y - start.Y] = _lines[end.Y].Substring(0, endIndex);
        return lines;
    }

    /// <summary>
    /// Returns a copy of all the text
    /// </summary>
    public string[] GetText()
    {
        return _lines.ToArray();
    }

    /// <summary>
    /// Replace a section of text.  This function is used to insert, 
    /// delete, and change a section of text.  It will re-analyze the
    /// text (replacing all tokens on all lines that were changed)
    /// and updating the positions of the tokens.
    /// RETURNS: The new end location of the inserted text
    /// </summary>
    public TokenLoc ReplaceText(string[] ?replacementText,
                     TokenLoc start, TokenLoc end)
    {
        // Bounds check parameters
        start.Y = Bound(start.Y, 0, _lines.Count - 1);
        start.X = Bound(start.X, 0, _lines[start.Y].Length);
        end.Y = Bound(end.Y, 0, _lines.Count - 1);
        end.X = Bound(end.X, 0, _lines[end.Y].Length);
        if (end.Y < start.Y)
            end.Y = start.Y;
        if (start.Y == end.Y && end.X < start.X)
            end.X = start.X;

        int startIndex = start.X;
        int endIndex = end.X;

        // Adjust first line
        if (start.Y != end.Y || startIndex != endIndex)
            _lines[start.Y] = _lines[start.Y].Substring(0, startIndex)
                                + _lines[end.Y].Substring(endIndex);

        // Remove unused lines
        if (start.Y != end.Y)
        {
            _lines.RemoveRange(start.Y + 1, end.Y - start.Y);
            _tokens.RemoveRange(start.Y + 1, end.Y - start.Y);
        }

        // Start and end are the same
        end.Y = start.Y;
        endIndex = startIndex;

        // Insert new text
        if (replacementText != null && replacementText.Length != 0)
        {
            // Break up the first line at the start position
            string startStr = _lines[start.Y].Substring(0, startIndex);
            string endStr = _lines[start.Y].Substring(startIndex);

            if (replacementText.Length <= 1)
            {
                _lines[start.Y] = startStr + replacementText[0] + endStr;
                endIndex = startStr.Length + replacementText[0].Length;
            }
            else
            {
                // Insert new lines
                _lines[start.Y] = startStr + replacementText[0];
                for (int i = 1; i < replacementText.Length; i++)
                {
                    _lines.Insert(start.Y + i, replacementText[i]);
                    _tokens.Insert(start.Y + i, Array.Empty<Token>());
                }
                end.Y = start.Y + replacementText.Length - 1;
                endIndex = replacementText[replacementText.Length - 1].Length;
                _lines[end.Y] += endStr;
            }
        }

        // Re-scan the updated text lines
        for (int i = start.Y; i <= end.Y; i++)
            _tokens[i] = ScanLine(_lines[i], i);

        // Re-adjust token line positions
        for (int i = start.Y; i < _tokens.Count; i++)
            foreach (Token token in _tokens[i])
                token.Y = i;

        // Calculate end of inserted text
        EndToken.Location = new TokenLoc(0, _lines.Count);
        end.X = endIndex;
        return end;
    }

    /// <summary>
    /// Delete all data, then scan tokens from strings using the current `Scanner`.
    /// </summary>
    public void Scan(string[] lines)
    {
        _tokens.Clear();

        // Must have at least one line of text
        if (lines.Length == 0)
            lines = new string[1] { "" };
        _lines = new List<string>(lines.Length);

        // For each line
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            _lines.Add(lines[lineIndex]);
            _tokens.Add(ScanLine(lines[lineIndex], lineIndex));
        }
        EndToken.Location = new TokenLoc(0, _lines.Count + 1);
    }

    /// <summary>
    /// Delete all data, then scan tokens from a stream using the current `Scanner`.
    /// </summary>
    public void Scan(Stream s)
    {
        _tokens.Clear();
        _lines.Clear();
        var tr = new StreamReader(s);
        while (!tr.EndOfStream)
        {
            var line = tr.ReadLine();
            ArgumentNullException.ThrowIfNull(line);
            _lines.Add(line);
            _tokens.Add(ScanLine(line, _tokens.Count));
        }
        if (_lines.Count == 0)
        {
            // Must have at least one line of text
            _lines.Add("");
            _tokens.Add([]);
        }
        EndToken.Location = new TokenLoc(0, _lines.Count + 1);
    }

    Token[] ScanLine(string line, int lineIndex)
    {
        _tokenBuffer.Clear();
        Scanner.ScanLine(line, _tokenBuffer);

        var tokens = _tokenBuffer.ToArray();
        _tokenBuffer.Clear();

        // Set line index
        foreach (var token in tokens)
        {
            token.Y = lineIndex;
        }

        // Set begin/end index
        if (tokens.Length != 0)
        {
            tokens[0].SetBolnByLexerOnly();
            tokens[tokens.Length - 1].SetEolnByLexerOnly();
        }

        return tokens;
    }

    /// <summary>
    /// Iterator to return all tokens
    /// </summary>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    /// <summary>
    /// Iterate through all tokens, starting at startLine
    /// </summary>
    public Enumerator GetEnumeratorStartAtLine(int startLine)
    {
        return new Enumerator(this, startLine);
    }

    /// <summary>
    /// Enumerate tokens in the lexer.  Call MoveNextLine to skip to next line
    /// </summary>
    public struct Enumerator : IEnumerator<Token>
    {
        Lexer mLexer;
        int mIndexLine;
        int mIndexToken;
        Token mCurrent;
        Token[] mCurrentLine;

        public IEnumerator<Token> GetEnumerator() { return this; }
        public void Dispose() { }
        public Token Current => mCurrent;
        public int CurrentLineTokenCount => mCurrentLine.Length;
        public Token[] CurrentLineTokens => mCurrentLine;
        public int CurrentLineTokenIndex => mIndexToken;
        public int CurrentLineIndex => mIndexLine;
        object System.Collections.IEnumerator.Current => mCurrent;

        /// <summary>
        /// Enumerate all tokens
        /// </summary>
        public Enumerator(Lexer lexer)
        {
            mLexer = lexer;
            mIndexLine = 0;
            mIndexToken = 0;
            mCurrentLine = mLexer._tokens.Count <= 0 ? Array.Empty<Token>() : mLexer._tokens[0];
            mCurrent = null;
        }

        /// <summary>
        /// Enumerate all tokens, starting at startLine
        /// </summary>
        public Enumerator(Lexer lexer, int startLine)
        {
            mLexer = lexer;
            mIndexLine = Math.Max(0, startLine);
            mIndexToken = 0;
            mCurrentLine = mLexer._tokens.Count <= mIndexLine ? Array.Empty<Token>() : mLexer._tokens[mIndexLine];
            mCurrent = null;
        }

        /// <summary>
        /// Returns the next token on the line, or "" if at end of line
        /// </summary>
        public Token PeekOnLine(int i = 0)
        {
            if (mIndexToken + i < mCurrentLine.Length)
                return mCurrentLine[mIndexToken + i];
            return mLexer.EndToken;
        }

        /// <summary>
        /// Returns the next token on the line only if there is no space (or "" if at end)
        /// </summary>
        /// <returns></returns>
        public Token PeekNoSpace()
        {
            if (mIndexToken < mCurrentLine.Length)
            {
                var t = mCurrentLine[mIndexToken];
                if (mCurrent.X + mCurrent.Name.Length == t.X)
                    return t;
                return mLexer.EndToken;
            }
            return mLexer.EndToken;
        }

        public void Reset()
        {
            throw new NotSupportedException("Reset on lexer enumerator is not supported");
        }

        public bool MoveNext(out Token t)
        {
            var e = MoveNext();
            t = mCurrent;
            return e;
        }

        /// <summary>
        /// Move to next token, skipping blank lines, returns EndToken at end of file
        /// </summary>
        public bool MoveNext()
        {
            // More tokens on this line?
            if (mIndexToken < mCurrentLine.Length)
            {
                mCurrent = mCurrentLine[mIndexToken++];
                return true;
            }
            // Move to next non-empty line
            mIndexToken = 0;
            do
            {
                mIndexLine++;
            } while (mIndexLine < mLexer._tokens.Count && mLexer._tokens[mIndexLine].Length == 0);

            // Return next token
            if (mIndexLine < mLexer._tokens.Count)
            {
                mCurrentLine = mLexer._tokens[mIndexLine];
                mCurrent = mCurrentLine[mIndexToken++];
                return true;
            }
            if (mCurrent != mLexer.EndToken)
            {
                mCurrent = mLexer.EndToken;
                mIndexToken = mCurrentLine.Length + 1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Skip to the end of the line, but do not advance to next line.
        /// The next call to MoveNext returns the beginning of the next line.
        /// If already at the end of the line, do nothing.
        /// </summary>
        public void SkipToEndOfLine()
        {
            mIndexToken = mCurrentLine.Length - 1;
            mCurrent = mCurrentLine[mIndexToken++];
        }

        /// <summary>
        /// Move to next line, blank lines return empty token
        /// </summary>
        public bool MoveNextLine()
        {
            mIndexToken = 0;
            if (mIndexLine < mLexer._tokens.Count)
            {
                mCurrentLine = mLexer._tokens[mIndexLine++];
                mCurrent = mCurrentLine.Length == 0 ? mLexer.EndToken : mCurrentLine[mIndexToken++];
                return true;
            }
            mCurrent = null;
            return false;
        }

    }
}
