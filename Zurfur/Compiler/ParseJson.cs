using System;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class ParseJson
    {
        Lexer mLexer;			// Lexer to be paresed
        Lexer.Enumerator mLexerEnum;		// Enumerator for the Lexer
        string mTokenName = "*"; // Skipped by first accept
        Token mToken;
        Token mPrevToken;
        public int ParseErrors { get; private set; }

        static WordSet sValueTokens = new WordSet("true false null");
        static WordSet sEndArrayValue = new WordSet(", ]", true);
        static WordSet sEndObjectKey = new WordSet(": }", true);
        static WordSet sEndObjectValue = new WordSet(", }", true);

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public ParseJson(Lexer tokens)
        {
            mLexer = tokens;
            mLexerEnum = new Lexer.Enumerator(mLexer);
            Accept();
        }

        public void Parse()
        {
            if (mTokenName == "")
                return;

            ParseValue();

            if (mTokenName != "")
            {
                RejectToken(mToken, "Expecting end of file");
                Accept();
                while (mTokenName != "")
                {
                    mToken.Grayed = true;
                    Accept();
                }
            }
        }

        void ParseValue()
        {
            if (mTokenName == "")
            {
                RejectToken(mToken, "Execting a value, not end of file");
                return;
            }
            if (sValueTokens.Contains(mTokenName))
                Accept();
            else if (mTokenName == "\"")
                ParseString();
            else if (mTokenName[0] >= '0' && mTokenName[0] <= '9' || mToken == "-" || mToken == "+")
            {
                Accept();
                while (mTokenName != "" && mTokenName[0] >= '0' && mTokenName[0] <= '9' || mToken == "-" || mToken == "+")
                    Accept();
            }
            else if (mTokenName == "{")
                ParseObject();
            else if (mTokenName == "[")
                ParseArray();
            else
                RejectToken(mToken, "Expecting a value, 'true', 'false', 'null', '{', '[', number, or string");
        }

        void ParseString()
        {
            if (mTokenName != "\"")
            {
                Reject("Expecting a quote to begin a string", sEndObjectKey);
                return;
            }
            // TBD: Accept and build json style strings
            Accept().Type = eTokenType.Quote;
            while (mTokenName != "" && mTokenName != "\"" && !mToken.Boln && !mToken.Meta)
            {
                if (mTokenName == "\\")
                {
                    if (mLexerEnum.PeekNoSpace() == "\"")
                        Accept().Type = eTokenType.Quote;
                }
                Accept().Type = eTokenType.Quote;
            }
            if (!mToken.Boln && mTokenName == "\"")
                Accept().Type = eTokenType.Quote;
        }

        void ParseArray()
        {
            var open = Accept();
            if (mToken != "]")
            {
                ParseValue();
                if (!sEndArrayValue.Contains(mTokenName))
                    Reject("Expecting ',' or ']'", sEndArrayValue);
                while (mTokenName == ",")
                {
                    Accept();
                    ParseValue();
                    if (!sEndArrayValue.Contains(mTokenName))
                        Reject("Expecting ',' or ']'", sEndArrayValue);
                }
            }
            if (AcceptMatch("]"))
            {
                Token.AddScopeLines(mLexer, open.Y, mPrevToken.Y - open.Y - 1, false );
                Connect(open, mPrevToken);
            }
        }

        void ParseObject()
        {
            var open = Accept();
            if (mToken != "}")
            {
                ParseObjectKv();
                while (mTokenName == ",")
                {
                    Accept();
                    ParseObjectKv();
                }
            }
            if (AcceptMatch("}"))
            {
                Token.AddScopeLines(mLexer, open.Y, mPrevToken.Y - open.Y - 1, false);
                Connect(open, mPrevToken);
            }
            else
            {
                RejectToken(mToken, "Expecting '}'");
            }
        }

        void ParseObjectKv()
        {
            ParseString();
            if (mTokenName != ":")
                Reject("Expecting ':'", sEndObjectKey);
            if (mTokenName == ":")
            {
                Accept();
                ParseValue();
            }
            if (!sEndObjectValue.Contains(mTokenName))
                Reject("Expecting ',' or '}'", sEndObjectValue);
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
            RejectToken(mToken, errorMessage);
            while (!stopTokens.Contains(mToken))
            {
                mToken.Grayed = true;
                Accept();
            }
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

        // Accept the current token and advance to the next, skipping all comments.
        // The new token is saved in mToken and the token name is saved in mTokenName.
        // Returns the token that was accepted.  Token is pre-maked with token type
        Token Accept()
        {
            // Already at end of file?
            mPrevToken = mToken;
            if (mTokenName == "")
                return mPrevToken;

            // Read next token, and skip comments
            do
            {
                // Read next token (set EOF flag if no more tokens on line)
                if (mLexerEnum.MoveNext())
                    mToken = mLexerEnum.Current;
            } while (mToken.Type == eTokenType.Comment);

            // Reset token info
            mTokenName = mToken.Name;
            mToken.Clear();
            if (mTokenName.Length == 0)
                mToken.Type = eTokenType.Normal;
            else if (mTokenName[0] == '\"')
                mToken.Type = eTokenType.Quote;
            else if (char.IsDigit(mTokenName[0]))
                mToken.Type = eTokenType.Number;
            else if (char.IsLetter(mTokenName[0]))
                mToken.Type = eTokenType.Identifier;
            else
                mToken.Type = eTokenType.Normal;

            return mPrevToken;
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


}

