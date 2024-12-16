using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AvaloniaEditor.Views;

public partial class HoverMessage : UserControl
{
    public HoverMessage()
    {
        InitializeComponent();
    }

    public string Message
    {
        get => hoverMessage.Text??"";
        set => hoverMessage.Text = value;
    }

}
