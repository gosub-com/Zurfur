using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Gosub.Edit;

namespace AvaloniaSynth.Views;

public partial class MainView : UserControl
{
    int _count;

    public MainView()
    {
        InitializeComponent();
    }

    public void OnButtonIncrementClicked(object source, RoutedEventArgs args)
    {
        LabelStatus1.Content = "Incremented counter:";
        TextEdit.ReplaceText([$"The lexer had {TextEdit.Lexer.LineCount} lines", ""], new(), new());
    }

}
