using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Gosub.Lex;

namespace Zurfur.Compiler;

class ParseZil
{
    Lexer _lexer;

    public ParseZil(Lexer lexer)
    {
        _lexer = lexer;
    }

    public void Parse() 
    {
        for (var i = 0;  i < _lexer.LineCount;  i++)
        {
            var tokens = _lexer.GetLineTokens(i);
            if (tokens.Length == 0)
                continue;
            
            // Show comments
            bool isComment = false;
            foreach (var token in tokens) 
            {
                if (token == "#")
                    isComment = true;
                if (isComment)
                    token.Type = TokenType.Comment;
            }

            // Show bold lines
            if (tokens[0] == "fun" || tokens[0].Name.EndsWith(":"))
            {
                foreach (var token in tokens)
                    if (token.Type != TokenType.Comment)
                        token.Bold = true;
            }

        }

    }

}
