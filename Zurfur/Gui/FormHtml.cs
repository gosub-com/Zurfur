using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Gosub.Zurfur
{
    public partial class FormHtml:Form
    {
        int mTabSize = 4;
        Lexer mLexer;
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
                html.Append(""
                    + "<style>"
                    + ".s0 { color:black }" // Normal
                    + ".s1 { color:blue; font-weight: bold }" // Reserved word
                    + ".s2 { color:blue }" // Reserved name
                    + ".s3 { color:black }" // Identifier
                    + ".s4 { color:green }" // Comment
                    + ".s5 { color:red }" // Unknown
                    + ".s6 { color:red }" // Unknown
                    + ".s7 { color:red }" // Unknown
                    + ".s8 { color:red }" // Unknown
                    + ".s9 { color:red }" // Unknown
                    + ".s10 { color:red }" // Unknown
                    + "</style>");
                html.Append("<pre>");

                int line = 0;
                int column = 0;
                eTokenType tokenType = eTokenType.Normal;
                html.Append("<span class=s0>");
                foreach (var token in mLexer)
                {
                    // Append new line when moving to next line
                    if (token.Line != line)
                    {
                        html.Append("\r\n");
                        line = token.Line;
                        column = 0;
                    }
                    // Prepend white space
                    int tokenColumn = IndexToCol(mLexer.GetLine(token.Line), token.Char);
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
        public void ShowLexer(Lexer lexer)
        {
            mLexer = lexer;
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
