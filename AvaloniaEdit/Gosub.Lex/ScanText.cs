
namespace Gosub.Lex;

/// <summary>
/// Default scanner for a generic text file.  Just split it by white space, nothing else.
/// </summary>
public sealed class ScanText : Scanner
{
    public override Scanner Clone()
    {
        return new ScanText();
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
    /// NOTE: The token's LineIndex is set to zero
    /// NOTE: The token is stripped of TABs
    /// </summary>
    void ScanToken(string line, ref int charIndex, List<Token> tokens)
    {
        // Skip white space
        while (charIndex < line.Length && char.IsWhiteSpace(line[charIndex]))
            charIndex++;

        // End of line?
        if (charIndex >= line.Length)
            return;

        // Until white space
        int startIndex = charIndex;
        while (charIndex < line.Length && !char.IsWhiteSpace(line[charIndex]))
            charIndex++;
        tokens.Add(new Token(line.Substring(startIndex, charIndex - startIndex), startIndex, 0));
    }
}
