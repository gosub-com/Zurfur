﻿
namespace Gosub.Lex;

/// <summary>
/// Scan for text, symbols, and numbers.  By default, all symbols are one character.
/// A list of multi-character symbols can be included in the constructor.
/// </summary>
public class ScanSymbols : Scanner
{
    // NOTE: >=, >>, and >>= are omitted and handled at parser level.
    readonly Dictionary<long, bool> _specialSymbols = new Dictionary<long, bool>();
    readonly bool _specialSymbolsHas3Chars;

    /// <summary>
    /// By default, symbols are one character long, or pass
    /// a string of multi-char symbols to constructor.
    /// </summary>
    public ScanSymbols()
    {
    }

    /// <summary>
    /// Symbols are one character long unless included in this list.
    /// e.g ">= <=" will scoop these two symbols into one token.
    /// NOTE: Current limitation is 3 character maximum.
    /// </summary>
    public ScanSymbols(string multiCharSymbols)
    {
        _specialSymbols.Clear();
        _specialSymbolsHas3Chars = false;
        var sa = multiCharSymbols.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var symbol in sa)
        {
            if (symbol.Length > 3)
                throw new Exception("SetSpecialSymbols: Symbols may not be more than 3 characters long");
            _specialSymbolsHas3Chars = _specialSymbolsHas3Chars || symbol.Length == 3;
            long code = 0;
            foreach (var ch in symbol)
                code = code * 65536 + ch;
            _specialSymbols[code] = true;
        }
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
            tokens.Add(new Token(number, startIndex, 0, TokenType.Number));
            return;
        }

        // Special symbols
        if (_specialSymbols.Count != 0 && charIndex + 1 < line.Length)
        {
            long code = ch1 * 65536 + line[charIndex + 1];
            if (_specialSymbolsHas3Chars
                && charIndex + 2 < line.Length
                && _specialSymbols.ContainsKey(code * 65536 + line[charIndex + 2]))
            {
                charIndex += 3;
                tokens.Add(new Token(Mintern(line.Substring(startIndex, 3)), startIndex, 0));
                return;
            }
            if (_specialSymbols.ContainsKey(code))
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
        return new Token(token, startIndex, 0, TokenType.Identifier);
    }

    // Consolidate some interned strings
    protected string Mintern(string s)
    {
        return string.IsInterned(s) ?? s;
    }

}
