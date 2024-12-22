using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

using Gosub.Avalonia.Controls;
using Gosub.Lex;
using static System.Net.Mime.MediaTypeNames;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AvaloniaEditor;

public partial class FormSearch : UserControl
{
    public event EventHandler? CloseClicked;

    Editor? _editor;
    MatchText _match = new();

    public FormSearch()
    {
        InitializeComponent();
        labelMatches.Text = "";
        if (Design.IsDesignMode)
            checkShowReplace.IsChecked = true;
        checkShowReplace_Changed(null, null);
    }

    public void buttonClose_Click(object? sender, RoutedEventArgs e)
    {
        CloseClicked?.Invoke(this, EventArgs.Empty);
    }

    public void checkShowReplace_Changed(object? sender, RoutedEventArgs? e)
    {
        var showReplace = checkShowReplace.IsChecked ?? false;
        textReplace.IsVisible = showReplace;
        buttonReplaceAll.IsVisible = showReplace;
        buttonReplaceNext.IsVisible = showReplace;
    }

    public void SetEditor(Editor editor)
    {
        if (_editor != null)
            _editor.TextChanged -= Editor_TextChanged;

        if (editor != null)
        {
            _editor = editor;
            editor.TextChanged += Editor_TextChanged;
            SearchParamsChanged();
        }
    }

    /// <summary>
    /// Show this form, take selected text, and focus the search edit box
    /// </summary>
    public async void ShowAndFocus()
    {
        IsVisible = true;

        await Task.Delay(1); // NOTE: Can't focus unless we delay a bit

        Focus();
        textSearch.Focus();

        if (_editor == null)
            return;

        // Take selected text for search box (if any)
        var selStart = _editor.SelStart;
        var selEnd = _editor.SelEnd;
        if (selStart != selEnd && selStart.Y == selEnd.Y)
        {
            var search = _editor.Lexer.GetText(selStart, selEnd);
            if (search.Length >= 1)
                textSearch.Text = search[0];
        }
        textSearch.SelectAll();
    }

    void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_editor == null || !IsVisible)
            return;
        SearchParamsChanged();
    }

    public void FindNext()
    {
        if (_editor == null || textSearch.Text == null)
            return;

        var (found, pastEnd) = _match.FindNextAndSelect(_editor, textSearch.Text, true);
        if (!found)
        {
            // TBD: Port
            // MessageBox.Show(this, "The search text was not found", Text);
        }
        else if (pastEnd)
        {
            // TBD: Port
            // MessageBox.Show(this, "Find reached the starting point of the search", Text);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.F3)
            FindNext();

        if (e.Key == Key.Escape)
        {
            IsVisible = false;
            e.Handled = true;
        }
    }


    void buttonFindNext_Click(object sender, RoutedEventArgs e)
    {
        FindNext();
    }

    /// <summary>
    /// Start a new search whenever the search text changes
    /// </summary>
    void textSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _match.ResetLocation();
        SearchParamsChanged();
    }

    void SearchParamsChanged()
    {
        if (_editor == null || textSearch.Text == null) 
            return;
        _match.MatchCase = checkMatchCase.IsChecked ?? false;
        _match.MatchWholeWord = checkMatchWholeWord.IsChecked ?? false;
        var count = _match.CountMatches(_editor.Lexer.GetText(), textSearch.Text);
        labelMatches.Text = $"{count} match(es)";
        buttonReplaceNext.IsEnabled = count != 0;
        buttonReplaceAll.IsEnabled = count != 0;
    }

    void buttonReplaceNext_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null || textSearch.Text == null || textReplace.Text == null) 
            return;

        var (found, pastEnd) = _match.FindNextAndSelect(_editor, textSearch.Text, false);

        if (!found || !_editor.HasSel())
        {
            // TBD: Port
            // MessageBox.Show(this, "The search text was not found", Text);
            return;
        }
        var selEnd = _editor.ReplaceText([textReplace.Text], _editor.SelStart, _editor.SelEnd);
        _editor.SelClear(true);
        _editor.SelSet(selEnd, selEnd);
        _match.FindNextAndSelect(_editor, textSearch.Text, false);
    }

    void buttonReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null || textSearch.Text == null || textReplace.Text == null)
            return;

        var cursor = _editor.CursorLoc;
        _editor.SelClear(true);
        var lastLoc = new TokenLoc();
        _editor.CursorLoc = lastLoc;
        var (found, pastEnd) = _match.FindNextAndSelect(_editor, textSearch.Text, false);
        while (found && _editor.HasSel())
        {
            var selEnd =_editor.ReplaceText([textReplace.Text], _editor.SelStart, _editor.SelEnd);
            if (selEnd <= lastLoc)
                break;
            lastLoc = selEnd;
            _editor.SelClear(true);
            _editor.SelSet(selEnd, selEnd);
            (found, pastEnd) = _match.FindNextAndSelect(_editor, textSearch.Text, false);
        }
        _editor.CursorLoc = cursor;
    }

    void checkMatchWholeWord_Changed(object sender, RoutedEventArgs e)
    {
        SearchParamsChanged();
    }

    void checkMatchCase_Changed(object sender, RoutedEventArgs e)
    {
        SearchParamsChanged();
    }


}