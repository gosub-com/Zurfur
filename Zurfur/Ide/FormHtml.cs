using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur
{
    public partial class FormHtml:Form
    {
        int mTabSize = 4;
        Lexer mLexer;
        int mLineStart;
        int mLineCount;
        string []mText;

        public FormHtml()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Convert character index to column index.  If charIndex is
        /// too large, the end of the line is returned.
        /// NOTE: The column index accounts for extra spaces
        /// inserted because of TABS.
        /// </summary>
        public int IndexToCol(string line, int charIndex)
        {
            int col = 0;
            for (int i = 0; i < charIndex && i < line.Length; i++)
            {
                if (line[i] == '\t')
                    col += mTabSize - col%mTabSize;
                else
                    col++;
            }
            return col;
        }

        private void FormHtml_Load(object sender, EventArgs e)
        {
            Show();
            Refresh();

            if (mText != null)
            {
                // Show the text in mText
                StringBuilder html = new StringBuilder();
                foreach (string s in mText)
                    html.Append(System.Security.SecurityElement.Escape(s)  + "<br>");
                html.Append("</pre>");

                webBrowser1.DocumentText = html.ToString();
            }

            if (mLexer != null)
            {
                StringBuilder html = new StringBuilder();

                // These should come from the text editor, which should eventually come from settings
                html.Append(""
                    + "<style>"
                    + ".s0 { color:black }" // Normal
                    + ".s1 { color:blue }" // Reserved
                    + ".s2 { color:blue; font-weight: bold }" // Reserved Control
                    + ".s3 { color:black }" // Identifier
                    + ".s4 { color:black }" // Number
                    + ".s5 { color:brown }" // Quote
                    + ".s6 { color:green }" // Comment
                    + ".s7 { color:#147DA0 }" // TypeName
                    + ".s8 { color:red }" // Unknown
                    + ".s9 { color:red }" // Unknown
                    + ".s10 { color:red }" // Unknown
                    + "</style>");
                html.Append("<pre>");

                int line = mLineCount == 0 ? 0 : mLineStart;
                int endLine = mLineCount == 0 ? int.MaxValue : mLineStart + mLineCount;
                int column = 0;
                eTokenType tokenType = eTokenType.Normal;
                html.Append("<span class=s0>");
                foreach (var token in mLexer.GetEnumeratorStartAtLine(line))
                {
                    if (token.Y > endLine)
                        break;

                    // Append new line when moving to next line
                    while (line < token.Y)
                    {
                        html.Append("\r\n");
                        line++;
                        column = 0;
                    }
                    // Prepend white space
                    int tokenColumn = IndexToCol(mLexer.GetLine(token.Y), token.X);
                    while (column < tokenColumn)
                    {
                        html.Append(" ");
                        column++;
                    }
                    // Begin span if necessary
                    if (tokenType != token.Type)
                    {
                        html.Append("</span>");
                        tokenType = token.Type;
                        html.Append("<span class=s" + (int)tokenType + ">");
                    }
                    // Append token
                    html.Append(System.Security.SecurityElement.Escape(token.Name));
                    column += token.Name.Length;
                }
                html.Append("</span>");
                tokenType = eTokenType.Normal;
                html.Append("</pre>");
                webBrowser1.DocumentText = html.ToString();

            }
        }

        /// <summary>
        /// Show the lexer (with syntax coloring)
        /// </summary>
        public void ShowLexer(Lexer lexer, int lineStart, int lineCount)
        {
            mLexer = lexer;
            mLineStart = lineStart;
            mLineCount = lineCount;
            Show();
        }

        /// <summary>
        /// Show a text file
        /// </summary>
        public void ShowText(string[] text)
        {
            mText = text;
            Show();
        }
    }
}
