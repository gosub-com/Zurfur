using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace AvaloniaEditor;

public partial class FormSearch : UserControl
{
    public event EventHandler? CloseClicked;

    public FormSearch()
    {
        InitializeComponent();
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
}