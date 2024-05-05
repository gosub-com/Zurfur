using System;
using System.Windows.Forms;
using Gosub.Lex;

namespace Zurfur.Ide;

/// <summary>
/// Search an editor for a string.  Call Show(Form, Editor) to show
/// the search box.  This form hides when deleted so it keeps the
/// previously searched text.  Call FindNext to find the previous
/// search text without displaying this form.
/// </summary>
public partial class FormSearch:Form
{
    // The form
    TextEditor	mEditor;
    MatchText mMatch = new MatchText();


    /// <summary>
    /// Initialize form
    /// </summary>
    public FormSearch()
    {
        InitializeComponent();
    }

    public void SetEditor(TextEditor editor)
    {
        if (mEditor != null)
            mEditor.TextChanged2 -= Editor_TextChanged2;
        if (editor != null)
        {
            mEditor = editor;
            editor.TextChanged2 += Editor_TextChanged2;
            SearchParamsChanged();
        }
    }

    void Editor_TextChanged2(object sender, EventArgs e)
    {
        if (mEditor == null || IsDisposed)
            return;
        SearchParamsChanged();
    }


    public void FindNext()
    {
        var (found, pastEnd) = mMatch.FindNextAndSelect(mEditor, textSearch.Text, true);
        if (!found)
        {
            MessageBox.Show(this, "The search text was not found", Text);
        }
        else if (pastEnd)
        {
            MessageBox.Show(this, "Find reached the starting point of the search", Text);
        }
    }

    void FormSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F3)
            FindNext();
        if (e.KeyCode == Keys.Escape)
        {
            // For whatever reason, if we don't do this
            // the main window can go under every other window
            // in the system
            Owner.BringToFront();
            Owner.Focus();
            Hide();
        }
    }

    void FormSearch_KeyPress(object sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == 27)
            e.Handled = true;
    }

    void FormSearch_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            // For whatever reason, if we don't do this
            // the main window can go under every other window
            // in the system
            Owner.BringToFront();
            Owner.Focus();
            Hide();
        }
    }

    void buttonFindNext_Click(object sender, EventArgs e)
    {
        FindNext();
    }

    /// <summary>
    /// Start a new search whenever the search text changes
    /// </summary>
    void textSearch_TextChanged(object sender, EventArgs e)
    {
        mMatch.ResetLocation();
        SearchParamsChanged();
    }

    void SearchParamsChanged()
    {
        mMatch.MatchCase = checkMatchCase.Checked;
        mMatch.MatchWholeWord = checkMatchWholeWord.Checked;
        var count = mMatch.CountMatches(mEditor.Lexer.GetText(), textSearch.Text);
        labelMatches.Text = $"{count} match(es)";
        buttonReplaceNext.Enabled = count != 0;
        buttonReplaceAll.Enabled = count != 0;
    }

    void buttonReplaceNext_Click(object sender, EventArgs e)
    {
        var (found, pastEnd) = mMatch.FindNextAndSelect(mEditor, textSearch.Text, false);
        if (!found || !mEditor.HasSel())
        {
            MessageBox.Show(this, "The search text was not found", Text);
            return;
        }
        var selEnd = mEditor.ReplaceText(new string[] { textReplace.Text }, mEditor.SelStart, mEditor.SelEnd);
        mEditor.SelClear(true);
        mEditor.SelSet(selEnd, selEnd);
        mMatch.FindNextAndSelect(mEditor, textSearch.Text, false);
    }

    void buttonReplaceAll_Click(object sender, EventArgs e)
    {
        var cursor = mEditor.CursorLoc;
        mEditor.SelClear(true);
        var lastLoc = new TokenLoc();
        mEditor.CursorLoc = lastLoc;
        var (found, pastEnd) = mMatch.FindNextAndSelect(mEditor, textSearch.Text, false);
        while (found && mEditor.HasSel())
        {
            var selEnd = mEditor.ReplaceText(new string[] { textReplace.Text }, mEditor.SelStart, mEditor.SelEnd);
            if (selEnd <= lastLoc)
                break;
            lastLoc = selEnd;
            mEditor.SelClear(true);
            mEditor.SelSet(selEnd, selEnd);
            (found, pastEnd) = mMatch.FindNextAndSelect(mEditor, textSearch.Text, false);
        }
        mEditor.CursorLoc = cursor;
    }

    void checkMatchWholeWord_CheckedChanged(object sender, EventArgs e)
    {
        SearchParamsChanged();
    }

    void checkMatchCase_CheckedChanged(object sender, EventArgs e)
    {
        SearchParamsChanged();
    }
}
