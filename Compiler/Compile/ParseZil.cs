using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Zurfur.Lex;

namespace Zurfur.Compiler
{
    class ParseZil
    {
        Lexer mLexer;

        public ParseZil(Lexer lexer)
        {
            mLexer = lexer;
        }

        public void Parse() 
        {
            for (var i = 0;  i < mLexer.LineCount;  i++)
            {
                var tokens = mLexer.GetLineTokens(i);
                if (tokens.Length == 0)
                    continue;
                
                // Show comments
                bool isComment = false;
                foreach (var token in tokens) 
                {
                    if (token == "#")
                        isComment = true;
                    if (isComment)
                        token.Type = eTokenType.Comment;
                }

                // Show bold lines
                if (tokens[0] == "fun" || tokens[0].Name.EndsWith(":"))
                {
                    foreach (var token in tokens)
                        if (token.Type != eTokenType.Comment)
                            token.Bold = true;
                }

            }

        }

    }
}
