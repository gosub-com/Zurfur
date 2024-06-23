using System;
using System.Collections.Generic;

using Gosub.Lex;

namespace Zurfur.Compiler;

class ParseJson
{
    Lexer _lexer;			// Lexer to be paresed
    Lexer.Enumerator _lexerEnum;		// Enumerator for the Lexer
    string _tokenName = "*"; // Skipped by first accept
    Token _token = new();
    Token _prevToken = new();
    public int ParseErrors { get; private set; }

    static WordSet s_valueTokens = new WordSet("true false null");
    static WordSet s_endArrayValue = new WordSet(", ]", true);
    static WordSet s_endObjectKey = new WordSet(": }", true);
    static WordSet s_endObjectValue = new WordSet(", }", true);

    /// <summary>
    /// Parse the given lexer
    /// </summary>
    public ParseJson(Lexer tokens)
    {
        _lexer = tokens;
        _lexerEnum = new Lexer.Enumerator(_lexer);
        Accept();
    }

    public void Parse()
    {
        if (_tokenName == "")
            return;

        ParseValue();

        if (_tokenName != "")
        {
            RejectToken(_token, "Expecting end of file");
            Accept();
            while (_tokenName != "")
            {
                _token.Grayed = true;
                Accept();
            }
        }
    }

    void ParseValue()
    {
        if (_tokenName == "")
        {
            RejectToken(_token, "Execting a value, not end of file");
            return;
        }
        if (s_valueTokens.Contains(_tokenName))
            Accept();
        else if (_tokenName == "\"")
            ParseString();
        else if (_tokenName[0] >= '0' && _tokenName[0] <= '9' || _token == "-" || _token == "+")
        {
            Accept();
            while (_tokenName != "" && _tokenName[0] >= '0' && _tokenName[0] <= '9' || _token == "-" || _token == "+")
                Accept();
        }
        else if (_tokenName == "{")
            ParseObject();
        else if (_tokenName == "[")
            ParseArray();
        else
            RejectToken(_token, "Expecting a value, 'true', 'false', 'null', '{', '[', number, or string");
    }

    void ParseString()
    {
        if (_tokenName != "\"")
        {
            Reject("Expecting a quote to begin a string", s_endObjectKey);
            return;
        }
        // TBD: Accept and build json style strings
        Accept().Type = eTokenType.Quote;
        while (_tokenName != "" && _tokenName != "\"" && !_token.Boln && !_token.Meta)
        {
            if (_tokenName == "\\")
            {
                if (_lexerEnum.PeekNoSpace() == "\"")
                    Accept().Type = eTokenType.Quote;
            }
            Accept().Type = eTokenType.Quote;
        }
        if (!_token.Boln && _tokenName == "\"")
            Accept().Type = eTokenType.Quote;
    }

    void ParseArray()
    {
        var open = Accept();
        if (_token != "]")
        {
            ParseValue();
            if (!s_endArrayValue.Contains(_tokenName))
                Reject("Expecting ',' or ']'", s_endArrayValue);
            while (_tokenName == ",")
            {
                Accept();
                ParseValue();
                if (!s_endArrayValue.Contains(_tokenName))
                    Reject("Expecting ',' or ']'", s_endArrayValue);
            }
        }
        if (AcceptMatch("]"))
        {
            Token.AddScopeLines(_lexer, open.Y, _prevToken.Y - open.Y - 1, false );
            Connect(open, _prevToken);
        }
    }

    void ParseObject()
    {
        var open = Accept();
        if (_token != "}")
        {
            ParseObjectKv();
            while (_tokenName == ",")
            {
                Accept();
                ParseObjectKv();
            }
        }
        if (AcceptMatch("}"))
        {
            Token.AddScopeLines(_lexer, open.Y, _prevToken.Y - open.Y - 1, false);
            Connect(open, _prevToken);
        }
        else
        {
            RejectToken(_token, "Expecting '}'");
        }
    }

    void ParseObjectKv()
    {
        ParseString();
        if (_tokenName != ":")
            Reject("Expecting ':'", s_endObjectKey);
        if (_tokenName == ":")
        {
            Accept();
            ParseValue();
        }
        if (!s_endObjectValue.Contains(_tokenName))
            Reject("Expecting ',' or '}'", s_endObjectValue);
    }

    // Reject the given token
    public void RejectToken(Token token, string errorMessage)
    {
        ParseErrors++;
        token.AddError(errorMessage);
    }


    // Reject the current token, then advance until the first stopToken
    void Reject(string errorMessage, WordSet stopTokens)
    {
        RejectToken(_token, errorMessage);
        while (!stopTokens.Contains(_token))
        {
            _token.Grayed = true;
            Accept();
        }
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

    // Accept the current token and advance to the next, skipping all comments.
    // The new token is saved in mToken and the token name is saved in mTokenName.
    // Returns the token that was accepted.  Token is pre-maked with token type
    Token Accept()
    {
        // Already at end of file?
        _prevToken = _token;
        if (_tokenName == "")
            return _prevToken;

        // Read next token, and skip comments
        do
        {
            // Read next token (set EOF flag if no more tokens on line)
            if (_lexerEnum.MoveNext())
                _token = _lexerEnum.Current;
        } while (_token.Type == eTokenType.Comment);

        // Reset token info
        _tokenName = _token.Name;
        _token.Clear();
        if (_tokenName.Length == 0)
            _token.Type = eTokenType.Normal;
        else if (_tokenName[0] == '\"')
            _token.Type = eTokenType.Quote;
        else if (char.IsDigit(_tokenName[0]))
            _token.Type = eTokenType.Number;
        else if (char.IsLetter(_tokenName[0]))
            _token.Type = eTokenType.Identifier;
        else
            _token.Type = eTokenType.Normal;

        return _prevToken;
    }

    /// <summary>
    /// Connect the tokens so the user sees the same info
    /// for both tokens (and both are grayed out when
    /// hovering with the mouse).  
    /// </summary>
    void Connect(Token s1, Token s2)
    {
        Token.Connect(s1, s2);
    }

}

