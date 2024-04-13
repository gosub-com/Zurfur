using System;
using System.Collections.Generic;
using System.Text;

namespace Zurfur.Lex;

/// <summary>
/// Basic types recognized by the lexer
/// </summary>
public enum eTokenType : byte
{
    // Lexer recognized types:
    Normal,
    Identifier,
    Reserved,
    ReservedControl,
    ReservedVar,
    ReservedType,
    Number,
    Quote,
    Comment,
    NewVarSymbol,
    DefineField,
    DefineMethod,
    DefineFunParam,
    DefineTypeParam,
    DefineLocal,
    TypeName,
    BoldSymbol
}

public enum eTokenSubtype : byte
{
    Normal,
    Error,
    Warn,
    CodeInComment
}

public enum eTokenBits : short
{
    Eoln = 1, // Read only (set only once by lexer)
    Boln = 2, // Read only (set only once by lexer)
    ReadOnlyMask = Eoln | Boln,
    Grayed = 4,
    Underline = 8,
    Bold = 16,
    Meta = 32,
    Shrink = 64,
    Continuation = 128,
    VerticalLine = 256,
}

/// <summary>
/// Each lexical element is assigned a new token.  This class is
/// also used by the parser and code generator to mark tokens with 
/// error codes and other information.
/// </summary>
sealed public class Token
{
    public static readonly Token[] EmptyArray = new Token[0];

    // Fields
    public readonly string Name = "";
    public TokenLoc Location;
    public eTokenType Type;
    public eTokenSubtype Subtype;
    eTokenBits mBits;
    ObjectBag mInfo;

    /// <summary>
    /// The path, which should only be set by the Lexer that owns it
    /// </summary>
    public string Path { get; private set; } = "";

    public Token()
    {
    }

    public Token(string name)
    {
        Name = name;
    }

    public Token(string name, int x, int y)
    {
        Name = name;
        X = x;
        Y = y;
    }
    public Token(string name, TokenLoc loc)
    {
        Name = name;
        X = loc.X;
        Y = loc.Y;
    }

    public Token(string name, int x, int y, eTokenType tokenType)
    {
        Name = name;
        X = x;
        Y = y;
        Type = tokenType;
    }

    public Token(string name, int x, int y, eTokenBits tokenBits)
    {
        Name = name;
        X = x;
        Y = y;
        mBits = tokenBits;
    }

    /// <summary>
    /// Clone the token, but lose all the markup info
    /// </summary>
    public Token Clone()
    {
        var token = new Token(Name, X, Y);
        token.Path = Path;
        if (Boln)
            token.SetBolnByLexerOnly();
        if (Eoln)
            token.SetEolnByLexerOnly();
        return token;
    }

    public int Y
    {
        get { return Location.Y; }
        set { Location.Y = value; }
    }
    public int X
    {
        get { return Location.X; }
        set { Location.X = value; }
    }

    public override string ToString()
    {
        return Name;
    }

    /// <summary>
    /// Alias for Subtype == Error
    /// TBD: Resetting an error does not check to see if there is a warning
    /// </summary>
    public bool Error
    {
        get => Subtype == eTokenSubtype.Error;
        private set { Subtype = value ? eTokenSubtype.Error : eTokenSubtype.Normal; }
    }

    /// <summary>
    /// Alias for Subtype == Warn (but does not set if the token already has an error)
    /// </summary>
    public bool Warn
    {
        get => Subtype == eTokenSubtype.Warn;
        private set { if (Subtype != eTokenSubtype.Error) Subtype = value ? eTokenSubtype.Warn : eTokenSubtype.Normal; }
    }

    public bool Grayed
    {
        get => (mBits & eTokenBits.Grayed) != 0;
        set { mBits = mBits & ~eTokenBits.Grayed | (value ? eTokenBits.Grayed : 0); }
    }
    public bool Underline
    {
        get => (mBits & eTokenBits.Underline) != 0;
        set { mBits = mBits & ~eTokenBits.Underline | (value ? eTokenBits.Underline : 0); }
    }
    public bool Bold
    {
        get => (mBits & eTokenBits.Bold) != 0;
        set { mBits = mBits & ~eTokenBits.Bold | (value ? eTokenBits.Bold : 0); }
    }

    public bool Meta
    {
        get => (mBits & eTokenBits.Meta) != 0;
        set { mBits = mBits & ~eTokenBits.Meta | (value ? eTokenBits.Meta : 0); }
    }

    public bool Eoln
        => (mBits & eTokenBits.Eoln) != 0;

    public bool Boln
        => (mBits & eTokenBits.Boln) != 0;

    public bool OnlyTokenOnLine
        => (~mBits & (eTokenBits.Boln | eTokenBits.Eoln)) == 0;

    public bool Shrink
    {
        get => (mBits & eTokenBits.Shrink) != 0;
        set { mBits = mBits & ~eTokenBits.Shrink | (value ? eTokenBits.Shrink : 0); }
    }
    public bool Continuation
    {
        get => (mBits & eTokenBits.Continuation) != 0;
        set { mBits = mBits & ~eTokenBits.Continuation | (value ? eTokenBits.Continuation : 0); }
    }

    public bool VerticalLine
    {
        get => (mBits & eTokenBits.VerticalLine) != 0;
    }

    public void SetPathByLexer(string path)
    {
        Path = path;
    }

    public void SetEolnByLexerOnly()
    {
        mBits |= eTokenBits.Eoln;
    }
    public void SetBolnByLexerOnly()
    {
        mBits |= eTokenBits.Boln;
    }

    /// <summary>
    /// Clear info, bits, type, and subtype, but not location or eoln bit
    /// </summary>
    public void Clear()
    {
        Type = eTokenType.Normal;
        Subtype = eTokenSubtype.Normal;
        Type = eTokenType.Normal;
        mBits = mBits & eTokenBits.ReadOnlyMask;
        mInfo = new ObjectBag();
    }

    public void AddInfo<T>(T info)
    {
        if (info is TokenVerticalLine)
            mBits |= eTokenBits.VerticalLine;
        if (info is TokenError)
            Error = true;
        if (info is TokenWarn)
            Warn = true;
        mInfo.AddInfo(info);
    }
    public void RemoveInfo<T>()
    {
        mInfo.RemoveInfo<T>();
        if (typeof(T) == typeof(TokenVerticalLine) || typeof(T).IsSubclassOf(typeof(TokenVerticalLine)))
            mBits &= ~eTokenBits.VerticalLine;
        if (typeof(T) == typeof(TokenError) || typeof(T).IsSubclassOf(typeof(TokenError)))
            Error = GetInfo<TokenError>() != null;
        if (typeof(T) == typeof(TokenWarn) || typeof(T).IsSubclassOf(typeof(TokenWarn)))
            Warn = GetInfo<TokenWarn>() != null;
    }
    public void SetInfo<T>(T info)
    {
        RemoveInfo<T>();
        AddInfo(info);
    }
    public T? GetInfo<T>() => mInfo.GetInfo<T>();
    public T[] GetInfos<T>() => mInfo.GetInfos<T>();

    /// <summary>
    /// Add an error to this token, set error flag and append TokenError(errorMessage)
    /// </summary>
    public void AddError(string errorMessage)
    {
        // Display error message
        AddInfo(new TokenError(errorMessage));
    }

    /// <summary>
    /// Add an error to this token, set error flag and append TokenError(errorMessage)
    /// </summary>
    public void AddError(TokenError error)
    {
        AddInfo(error);
    }

    /// <summary>
    /// Add a warning to the token (set warning bit and append the message)
    /// </summary>
    public void AddWarning(string warnMessage)
    {
        AddInfo(new TokenWarn(warnMessage));
    }

    /// <summary>
    /// Add a warning to the token (set warning bit and append the message)
    /// </summary>
    public void AddWarning(TokenWarn warnMessage)
    {
        AddInfo(warnMessage);
    }

    /// <summary>
    /// Link a token to one in a different file.  File://filename?x=0&y=0
    /// </summary>
    public void SetUrl(string file, Token token)
    {
        SetInfo(new TokenUrl("File://" + file + "?x=" + token.X + "&y=" + token.Y));
    }

    public void SetVerticalLine(TokenVerticalLine info)
    {
        SetInfo(info);
    }

    public string Url
    {
        get
        {
            var token = GetInfo<TokenUrl>();
            return token == null ? "" : token.Url;
        }
        set
        {
            SetInfo(new TokenUrl(value));
        }
    }

    /// <summary>
    /// Convert this token to a string
    /// </summary>
    public static implicit operator string(Token token)
    {
        return token.Name;
    }

    /// <summary>
    /// Connect the tokens because they are logically connected, such as
    /// matching '(' and ')' or invisible replacement tokens.
    /// Use GetInfo<Token[]>() to find connected tokens.
    /// </summary>
    static public void Connect(Token t1, Token t2)
    {
        // Find tokens that are already connected
        var tokens = new List<Token>();
        var s1Connectors = t1.GetInfo<Token[]>();
        if (s1Connectors != null)
            tokens.AddRange(s1Connectors);
        var s2Connectors = t2.GetInfo<Token[]>();
        if (s2Connectors != null)
            tokens.AddRange(s2Connectors);

        // Add these tokens to the list
        tokens.Remove(t1);
        tokens.Remove(t2);
        tokens.Add(t1);
        tokens.Add(t2);

        // Set token info
        Token[] sa = tokens.ToArray();
        foreach (Token s in sa)
            s.SetInfo(sa);
    }

    /// <summary>
    /// Add a vertical line on the left most token of the line with the open symbol.
    /// </summary>
    public static void AddScopeLines(Lexer lexer, int y, int lines, bool error)
    {
        if (lines <= 0 || lexer.GetLineTokens(y).Length == 0)
            return;
        var leftToken = lexer.GetLineTokens(y)[0];
        leftToken.SetInfo(new TokenVerticalLine
        {
            X = leftToken.X,// openToken.X,
            Y = leftToken.Y + 1,
            Lines = lines,
            Error = error
        });
    }


    /// <summary>
    /// Minimalist implementation of List of object, but is much
    /// smaller and doesn't use any heap to store one object since
    /// that's the most common case.  Do not copy, because the
    /// copy may or may not point to the same array.
    /// </summary>
    struct ObjectBag
    {
        object? mInfo;

        public void AddInfo(object ?info)
        {
            if (info == null)
                return;
            var obj = mInfo;
            if (obj == null)
            {
                mInfo = info;
                return;
            }
            var list = obj as object[];
            if (list != null && list.GetType() == typeof(object[]))
            {
                for (int i = 0;  i < list.Length;  i++)
                    if (list[i] == null)
                    {
                        list[i] = info;
                        return;
                    }
                // Expand array
                var list2 = new object[list.Length * 2];
                Array.Copy(list, list2, list.Length);
                list2[list.Length] = info;
                mInfo = list2;
                return;
            }
            list = new object[4];
            list[0] = obj;
            list[1] = info;
            mInfo = list;
        }

        public void RemoveInfo<T>()
        {
            var obj = mInfo;
            if (obj == null)
                return;
            if (obj is T)
            {
                mInfo = null;
                return;
            }
            var list = obj as object?[];
            if (list != null && list.GetType() == typeof(object[]))
            {
                for (int i = 0; i < list.Length; i++)
                    if (list[i] is T)
                        list[i] = null;
            }
        }

        public T? GetInfo<T>()
        {
            var obj = mInfo;
            if (obj is T)
                return (T)obj;
            var list = obj as object[];
            if (list != null && list.GetType() == typeof(object[]))
                foreach (var elem in list)
                    if (elem is T)
                        return (T)elem;
            return default(T);
        }

        public T[] GetInfos<T>()
        {
            var obj = mInfo;
            if (obj is T)
                return new T[1] { (T)obj };
            var list = obj as object[];
            if (list == null || list.GetType() != typeof(object[]))
                return Array.Empty<T>();
            var infos = new List<T>();
            foreach (var elem in list)
                if (elem is T)
                    infos.Add((T)elem);
            return infos.ToArray();
        }

    }

}

/// <summary>
/// Keep track of the token location (Y is line number, X is column)
/// </summary>
public struct TokenLoc : IComparable<TokenLoc>
{
    /// <summary>
    /// Line number of token
    /// </summary>
    public int Y;

    /// <summary>
    /// Column of token
    /// </summary>
    public int X;

    public TokenLoc(int x, int y)
    {
        Y = y;
        X = x;
    }

    /// <summary>
    /// Ensure that low >= high (swap them if they are not)
    /// </summary>
    public static void FixOrder(ref TokenLoc low, ref TokenLoc high)
    {
        if (low > high)
        {
            TokenLoc temp = low;
            low = high;
            high = temp;
        }
    }

    public static bool operator==(TokenLoc a, TokenLoc b)
    {
        return a.Y == b.Y && a.X == b.X;
    }
    public static bool operator!=(TokenLoc a, TokenLoc b)
    {
        return !(a == b);
    }
    public static bool operator>(TokenLoc a, TokenLoc b)
    {
        return a.Y > b.Y || a.Y == b.Y && a.X > b.X;
    }
    public static bool operator<(TokenLoc a, TokenLoc b)
    {
        return a.Y < b.Y || a.Y == b.Y && a.X < b.X;
    }
    public static bool operator>=(TokenLoc a, TokenLoc b)
    {
        return a.Y > b.Y || a.Y == b.Y && a.X >= b.X;
    }
    public static bool operator<=(TokenLoc a, TokenLoc b)
    {
        return a.Y < b.Y || a.Y == b.Y && a.X <= b.X;
    }
    public override int GetHashCode()
    {
        return (Y << 12) + X;
    }
    public override bool Equals(object ?obj)
    {
        if (obj == null)
            return false;
        if (!(obj is TokenLoc))
            return false;
        return this == (TokenLoc)obj;
    }
    public int CompareTo(TokenLoc value)
    {
        if (X == value.X && Y == value.Y)
            return 0;
        return this > value ? 1 : -1;
    }

    public override string ToString()
    {
        return "" + "X=" + X + ", Y=" + Y;
    }
}

/// <summary>
/// Attach to a token to provide a link (standard URL, or file name with location info)
/// </summary>
public class TokenUrl
{
    public readonly string Url = "";

    /// <summary>
    /// Standard URL
    /// </summary>
    public TokenUrl(string url)
    {
        Url = url;
    }
}

public class TokenError
{
    public string Message = "";
    public TokenError(string message) { Message = message; }
    public override string ToString()
    {
        return Message;
    }
}

public class TokenWarn
{
    public string Message = "";
    public TokenWarn(string message) { Message = message; }
    public override string ToString()
    {
        return Message;
    }
}

public class TokenVerticalLine
{
    public int X;
    public int Y;
    public int Lines;
    public string HoverMessage = "";
    public bool Error;
}
