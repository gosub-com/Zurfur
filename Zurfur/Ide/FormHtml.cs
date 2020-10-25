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
            webBrowser1.DocumentText = GetHtml();
        }

        private string GetHtml()
        {
            if (mText != null)
            {
                // Show the text in mText
                StringBuilder html = new StringBuilder();
                foreach (string s in mText)
                    html.Append(System.Security.SecurityElement.Escape(s) + "<br>");
                html.Append("</pre>");
                return html.ToString();
            }

            if (mLexer != null)
            {
                StringBuilder html = new StringBuilder();

                // These should come from the text editor, which should eventually come from settings
                const int SHRINK_TEXT = 14;  // See table below
                const int SHRINK_SPACE = 15;
                html.Append(""
                    + "<style>"
                    + ".s0{color:black}" // Normal
                    + ".s1{color:blue}" // Reserved
                    + ".s2{color:blue;font-weight: bold}" // Reserved Control
                    + ".s3{color:black}" // Identifier
                    + ".s4{colot:black}" // Number
                    + ".s5{color:brown}" // Quote
                    + ".s6{color:green}" // Comment
                    + ".s7{color:green;font-weight:bold}" // Public comment
                    + ".s8{color:black;font-weight:bold}" // Define field
                    + ".s9{color:black;font-weight:bold}" // Define method
                    + ".s10{color:black;font-weight:bold}" // Define param
                    + ".s11{color:black;font-weight:bold}" // Define local
                    + ".s12{color:#147DA0}" // TypeName
                    + ".s13{color:black;font-weight:bold}" // Bold symbol
                    + ".s14{color:black;font-weight:900;font-size: 0.5vw;line-height:50%}"
                    + ".s15{line-height:50%}"
                    + "</style>\r\n");
                html.Append("<pre>");

                int line = mLineCount == 0 ? 0 : mLineStart;
                int endLine = mLineCount == 0 ? int.MaxValue : mLineStart + mLineCount;
                int column = 0;
                int tokenType = (int)eTokenType.Normal;
                html.Append("<span class=s0>");
                foreach (var token in mLexer.GetEnumeratorStartAtLine(line))
                {
                    if (token.Y > endLine)
                        break;

                    // Append new line when moving to next line
                    bool alreadyHasLine = false;
                    while (line < token.Y)
                    {
                        html.Append(alreadyHasLine ? "<br>" : "\r\n");
                        alreadyHasLine = true;
                        line++;
                        column = 0;
                    }

                    // Shrink space before shrunken symbol
                    if (token.Shrink)
                    {
                        html.Append("</span><span class=s" + SHRINK_SPACE + ">");
                    }

                    // Prepend white space
                    int tokenColumn = IndexToCol(mLexer.GetLine(token.Y), token.X);
                    while (column < tokenColumn)
                    {
                        html.Append(" ");
                        column++;
                    }
                    var newTokenType = (int)token.Type;
                    if (token.Shrink)
                        newTokenType = SHRINK_TEXT;

                    // Begin span if necessary
                    if (tokenType != newTokenType)
                    {
                        tokenType = newTokenType;
                        html.Append("</span><span class=s" + tokenType + ">");
                    }
                    // Append token
                    html.Append(System.Security.SecurityElement.Escape(token.Name));
                    column += token.Name.Length;

                    // Shrunken space after shrunken symbol
                    if (tokenType == SHRINK_TEXT)
                    {
                        html.Append("</span><span class=s" + SHRINK_SPACE + ">");
                        tokenType = 0;
                    }
                }
                html.Append("</span>");
                html.Append("</pre>");
                return html.ToString();
            }
            return "";
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

        private void buttonCopyHtmlAsText_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(GetHtml());
        }
    }
}
