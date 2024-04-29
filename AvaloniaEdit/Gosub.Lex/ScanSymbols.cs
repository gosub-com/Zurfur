
namespace Gosub.Lex;

/// <summary>
/// Scan for text, symbols, and numbers.  By default, all symbols are one character.
/// A list of multi-character symbols can be included in the constructor.
/// </summary>
public sealed class ScanSymbols : Scanner
{
    // NOTE: >=, >>, and >>= are omitted and handled at parser level.
    readonly string mMultiCharSymbols;
    readonly Dictionary<long, bool> mSpecialSymbols = new Dictionary<long, bool>();
    readonly bool mSpecialSymbolsHas3Chars;

    // Use less memory by consolidating strings (not sure it even matters)
    LruMap<string,string> mMintern = new(512);

    /// <summary>
    /// By default, symbols are one character long, or pass
    /// a string of multi-char symbols to constructor.
    /// </summary>
    public ScanSymbols()
    {
        mMultiCharSymbols = "";
    }

    /// <summary>
    /// Symbols are one character long unless included in this list.
    /// e.g ">= <=" will scoop these two symbols into one token.
    /// NOTE: Current limitation is 3 character maximum.
    /// </summary>
    public ScanSymbols(string multiCharSymbols)
    {
        mMultiCharSymbols = multiCharSymbols;
        mSpecialSymbols.Clear();
        mSpecialSymbolsHas3Chars = false;
        var sa = multiCharSymbols.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var symbol in sa)
        {
            if (symbol.Length > 3)
                throw new Exception("SetSpecialSymbols: Symbols may not be more than 3 characters long");
            mSpecialSymbolsHas3Chars = mSpecialSymbolsHas3Chars || symbol.Length == 3;
            long code = 0;
            foreach (var ch in symbol)
                code = code * 65536 + ch;
            mSpecialSymbols[code] = true;
        }
    }

    public override Scanner Clone()
    {
        return new ScanSymbols(mMultiCharSymbols);
    }

    /// <summary>
    /// Scan a line
    /// </summary>
    public override void ScanLine(string line, List<Token> tokens)
    {
        // Build an array of tokens for this line
        int charIndex = 0;
        while (charIndex < line.Length)
            ScanToken(line, ref charIndex, tokens);
    }

    /// <summary>
    /// Get the next token on the line.  
    /// Returns a "" token if there are none left.
    /// NOTE: The token's LineIndex is set to zero
    /// NOTE: The token is stripped of TABs
    /// </summary>
    void ScanToken(string line, ref int charIndex, List<Token> tokens)
    {
        // Skip white space
        while (charIndex < line.Length && char.IsWhiteSpace(line[charIndex]))
            charIndex++;

        // End of line?
        int startIndex = charIndex;
        if (charIndex >= line.Length)
            return;

        // Identifier
        char ch1 = line[charIndex];
        if (char.IsLetter(ch1) || ch1 == '_')
        {
            tokens.Add(ScanIdentifier(line, ref charIndex, startIndex));
            return;
        }

        // Number
        if (IsAsciiDigit(ch1))
        {
            charIndex = SkipNumber(line, startIndex);
            string number = Mintern(line.Substring(startIndex, charIndex - startIndex));
            tokens.Add(new Token(number, startIndex, 0, eTokenType.Number));
            return;
        }

        // Special symbols
        if (mSpecialSymbols.Count != 0 && charIndex + 1 < line.Length)
        {
            long code = ch1 * 65536 + line[charIndex + 1];
            if (mSpecialSymbolsHas3Chars
                && charIndex + 2 < line.Length
                && mSpecialSymbols.ContainsKey(code * 65536 + line[charIndex + 2]))
            {
                charIndex += 3;
                tokens.Add(new Token(Mintern(line.Substring(startIndex, 3)), startIndex, 0));
                return;
            }
            if (mSpecialSymbols.ContainsKey(code))
            {
                charIndex += 2;
                tokens.Add(new Token(Mintern(line.Substring(startIndex, 2)), startIndex, 0));
                return;
            }
        }

        // Single character
        tokens.Add(new Token(Mintern(line[charIndex++].ToString()), startIndex, 0));
    }


    int SkipNumber(string line, int i)
    {
        // Check for hexadecimal
        int len = line.Length;
        if (i + 2 < len && line[i] == '0' && line[i + 1] == 'x' && IsHexDigit(line[i + 2]))
        {
            i += 3;
            while (i < len && IsHexDigit(line[i]))
                i++;
            return i;
        }

        // Scoop integer part
        while (i < len && IsAsciiDigit(line[i]))
            i++;

        // Scoop fraction part ('.' followed by digit)
        if (i + 1 < len && line[i] == '.' && IsAsciiDigit(line[i + 1]))
        {
            i += 2;
            while (i < len && IsAsciiDigit(line[i]))
                i++;
        }

        // Scoop exponentiation part ('e' followed by digit or '+'/'-'digit)
        if (i + 2 < len && IsE(line[i]) && IsAsciiDigit(line[i + 1])
            || i + 3 < len && IsE(line[i]) && IsPlusOrMinus(line[i + 1]) && IsAsciiDigit(line[i + 2]))
        {
            i += 2;
            while (i < len && IsAsciiDigit(line[i]))
                i++;
        }
        return i;

    }

    bool IsE(char ch) => ch == 'e' || ch == 'E';
    bool IsPlusOrMinus(char ch) => ch == '+' || ch == '-';

    Token ScanIdentifier(string line, ref int charIndex, int startIndex)
    {
        // Hop over identifier
        int endIndex = charIndex;
        while (endIndex < line.Length &&
                (char.IsLetterOrDigit(line, endIndex) || line[endIndex] == '_'))
            endIndex++;
        string token = Mintern(line.Substring(charIndex, endIndex - charIndex));
        charIndex = endIndex; // Skip token
        return new Token(token, startIndex, 0, eTokenType.Identifier);
    }


    // Consolidate strings
    string Mintern(string s)
    {
        if (s.Length == 0)
            return "";

        var isInterned = string.IsInterned(s);
        if (isInterned != null)
            return isInterned;

        var s2 = mMintern.Get(s);
        if (s2 != null)
            return s2;

        mMintern.Add(s, s);
        return s;
    }
}
