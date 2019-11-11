using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{
    class JsonParse
    {
        const bool SHOW_PARSE_TREE = true;

        Lexer mLexer;			// Lexer to be paresed
        Lexer.Enumerator mLexerEnum;		// Enumerator for the Lexer
        string mTokenName = "*"; // Skipped by first accept
        Token mToken;
        Token mPrevToken;

        static WordSet sValueTokens = new WordSet("true false null");
        static WordSet sEndArray = new WordSet("]", true);
        static WordSet sEndArrayValue = new WordSet(", ]", true);
        static WordSet sEndObject = new WordSet("}", true);
        static WordSet sEndObjectKey = new WordSet(": }", true);
        static WordSet sEndObjectValue = new WordSet(", }", true);

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public JsonParse(Lexer tokens)
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
                mToken.AddError("Expecting end of file");
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
                mToken.AddError("Execting a value, not end of file");
                return;
            }
            if (sValueTokens.Contains(mTokenName))
                Accept();
            else if (mTokenName[0] == '\"')
                Accept();
            else if (mTokenName[0] >= '0' && mTokenName[0] <= '9')
                Accept();
            else if (mTokenName == "{")
                ParseObject();
            else if (mTokenName == "[")
                ParseArray();
            else
                mToken.AddError("Expecting a value, 'true', 'false', 'null', '{', '[', number, or string");
        }

        void ParseArray()
        {
            var open = Accept();
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
            if (AcceptMatch("]"))
                Connect(open, mPrevToken);
        }

        void ParseObject()
        {
            var open = Accept();
            ParseObjectKv();
            while (mTokenName == ",")
            {
                Accept();
                ParseObjectKv();
            }
            if (AcceptMatch("}"))
                Connect(open, mPrevToken);
        }

        void ParseObjectKv()
        {
            if (mTokenName != "" && mTokenName[0] == '\"')
                Accept();
            else
                mToken.AddError("Expecting a string");

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

        // Reject the current token, then advance until the first stopToken
        void Reject(string errorMessage, WordSet stopTokens)
        {
            mToken.AddError(errorMessage);
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
                else
                    mToken = new Token("", 0, 0, eTokenType.Reserved);

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
            // Find tokens that are already connected
            List<Token> tokens = new List<Token>();
            Token[] s1Connectors = s1.GetInfo<Token[]>();
            if (s1Connectors != null)
                foreach (Token s in s1Connectors)
                    tokens.Add(s);
            Token[] s2Connectors = s2.GetInfo<Token[]>();
            if (s2Connectors != null)
                foreach (Token s in s2Connectors)
                    tokens.Add(s);

            // Add these tokens to the list
            tokens.Add(s1);
            tokens.Add(s2);

            // Set token info
            Token[] sa = tokens.ToArray();
            foreach (Token s in sa)
                s.ReplaceInfo(sa);
        }

    }


}

