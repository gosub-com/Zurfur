
namespace Gosub.Lex;

/// <summary>
/// Base class for Lexer scanners
/// </summary>
public abstract class Scanner
{
    /// <summary>
    /// Logic to tokenize text based on the language. 
    /// </summary>
    public abstract void ScanLine(string line, List<Token> tokens);

    /// <summary>
    /// Returns TRUE if it's an ASCII digit (ignoring unicode digits)
    /// </summary>
    protected static bool IsAsciiDigit(char ch)
    {
        return ch >= '0' && ch <= '9';
    }

    /// <summary>
    /// Returns true if it's a hex digit
    /// </summary>
    protected static bool IsHexDigit(char ch)
    {
        return ch >= '0' && ch <= '9'
                || ch >= 'A' && ch <= 'F'
                || ch >= 'a' && ch <= 'f';
    }

}
