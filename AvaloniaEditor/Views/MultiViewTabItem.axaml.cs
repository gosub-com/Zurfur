using System;
using System.Linq;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using System.ComponentModel;

namespace AvaloniaEditor.Views;

public partial class MultiViewTabItem : TabItem
{
    protected override Type StyleKeyOverride => typeof(TabItem);

    public string Key { get; }
    public event EventHandler<MultiViewTabItem>? TabCloseRequest;

    public MultiViewTabItem(string key)
    {
        Key = key;
        InitializeComponent();
        buttonClose.Opacity = 0;
    }

    public void buttonClose_Click(object source, RoutedEventArgs args)
    {
        TabCloseRequest?.Invoke(this, this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property.Name == "IsPointerOver" || change.Property.Name == "IsSelected")
        {
            if (IsPointerOver)
                buttonClose.Opacity = 1;
            else if (IsSelected)
                buttonClose.Opacity = 1;
            else
                buttonClose.Opacity = 0;
        }
        base.OnPropertyChanged(change);
    }
}