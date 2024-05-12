using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using System.ComponentModel;

namespace Gosub.Avalonia.Controls;

public partial class MultiViewTabItem : TabItem
{
    protected override Type StyleKeyOverride => typeof(TabItem);

    public MultiViewTabItem()
    {
        InitializeComponent();
        buttonClose.Opacity = 0;
    }

    public void buttonClose_Click(object source, RoutedEventArgs args)
    {
        tabText.FontWeight = tabText.FontWeight == FontWeight.Normal ?
            FontWeight.Bold : FontWeight.Normal;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property.Name == "IsPointerOver"
            || change.Property.Name == "IsSelected")
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