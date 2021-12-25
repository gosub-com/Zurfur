using System;
using System.Windows.Forms;
using Gosub.Zurfur.Lex;


namespace Gosub.Zurfur
{
    static class FormSearchInstance
    {
        static FormSearch sFormSearch;

        public static void SetEditor(TextEditor editor)
        {
            if (editor == null || sFormSearch == null || sFormSearch.IsDisposed)
                return;
            sFormSearch.SetEditor(editor);
        }

        /// <summary>
        /// Call this function to show the search form
        /// </summary>
        public static void Show(Form owner, TextEditor editor)
        {
            // Create the form
            if (sFormSearch == null || sFormSearch.IsDisposed)
                sFormSearch = new FormSearch();

            // Display the form (possibly with a new owner)
            sFormSearch.SetEditor(editor);
            if (owner != sFormSearch.Owner)
                sFormSearch.Visible = false;
            if (!sFormSearch.Visible)
                sFormSearch.Show(owner);

            // Take selected text for search box (if any)
            TokenLoc selStart = editor.SelStart;
            TokenLoc selEnd = editor.SelEnd;
            if (selStart != selEnd && selStart.Y == selEnd.Y)
            {
                string[] search = editor.Lexer.GetText(selStart, selEnd);
                if (search.Length == 1)
                    sFormSearch.textSearch.Text = search[0];
            }

            // Set search box focus
            sFormSearch.textSearch.Focus();
            sFormSearch.textSearch.SelectAll();
        }

        /// <summary>
        /// Call this function to "FindNext"
        /// </summary>
        public static void FindNext(Form owner, TextEditor editor)
        {
            if (sFormSearch == null || sFormSearch.textSearch.Text.Trim() == "")
            {
                Show(owner, editor);
                sFormSearch.SetEditor(editor);
            }
            else
            {
                if (sFormSearch.Owner != owner)
                    Show(owner, editor);
                sFormSearch.SetEditor(editor);
                sFormSearch.FindNext();
            }
        }

    }
}
