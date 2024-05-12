using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Runtime.CompilerServices;

namespace Gosub.Avalonia.Controls;

public partial class MultiViewEditor : UserControl
{
    TabControl _tabControl;
    Dictionary<string, MultiViewTabItem> _tabs = new();

    public event EventHandler<Control>? SelectionChanged;

    public MultiViewEditor()
    {
        InitializeComponent();
        var tabControl = Content as TabControl;
        ArgumentNullException.ThrowIfNull(tabControl, "Expecting XML to be TabControl");
        _tabControl = tabControl;
    }


    public void SetTab(string key, Control tabContent, string tabText)
    {
        // Replace
        if (_tabs.TryGetValue(key, out var rtab))
        {
            rtab.tabText.Text = tabText;
            rtab.tabContent.Content = tabContent;
            return;
        }

        // Add
        var tabItem = new MultiViewTabItem();
        tabItem.tabText.Text = tabText;
        tabItem.tabContent.Content = tabContent;
        _tabs[key] = tabItem;
        _tabControl.Items.Add(tabItem);
    }

    public Control? FindTab(string key)
    {
        return _tabs.GetValueOrDefault(key)?.tabContent?.Content as Control;
    }

    public void RemoveTab(string key) 
    {
        if (_tabs.TryGetValue(key, out var tabItem))
        {
            _tabs.Remove(key);
            _tabControl.Items.Remove(tabItem);
        }
    }


    void OnSelectionChanged(Object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;
        var addedItem = (e.AddedItems[0] as MultiViewTabItem)?.tabContent.Content as Control;
        if (addedItem != null)
            SelectionChanged?.Invoke(this, addedItem);
    }


}