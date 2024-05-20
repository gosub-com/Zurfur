using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Runtime.CompilerServices;

namespace Gosub.Avalonia.Controls;

public partial class MultiViewEditor : UserControl
{
    TabControl _tabControl;
    public event EventHandler<string>? SelectionChanged;
    public event EventHandler<string>? TabCloseRequest;

    public MultiViewEditor()
    {
        InitializeComponent();
        var tabControl = Content as TabControl;
        ArgumentNullException.ThrowIfNull(tabControl, "Expecting XML to be TabControl");
        _tabControl = tabControl;
    }


    public void SetTab(string key, Control tabContent, string tabText)
    {
        // Replace existing tab with new control
        var tabItem = FindMvti(key);
        if (tabItem != null)
        {
            tabItem.tabText.Text = tabText;
            tabItem.tabContent.Content = tabContent;
            return;
        }

        // Add new tab
        tabItem = new MultiViewTabItem(key);
        tabItem.tabText.Text = tabText;
        tabItem.tabContent.Content = tabContent;
        tabItem.TabCloseRequest += TabItem_TabCloseRequest;
        _tabControl.Items.Add(tabItem);
    }

    private void TabItem_TabCloseRequest(object? sender, MultiViewTabItem e)
    {
        TabCloseRequest?.Invoke(this, e.Key);
    }

    /// <summary>
    /// Find the tab content for the given key
    /// </summary>
    public Control? FindTabContent(string key)
    {
        return FindMvti(key)?.tabContent?.Content as Control;
    }

    /// <summary>
    /// Find the MultiViewTabItem for the given key
    /// </summary>
    MultiViewTabItem? FindMvti(string key)
        => _tabControl.Items.FirstOrDefault(i => (i as MultiViewTabItem)?.Key == key) as MultiViewTabItem;

    public void ShowTab(string key)
    {
        var tabItem2 = FindMvti(key);
        if (tabItem2 != null) 
            tabItem2.IsSelected = true;
    }

    public void RemoveTab(string key) 
    {
        var tab = FindMvti(key);
        if (tab != null)
            _tabControl.Items.Remove(tab);
    }

    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;
        var addedItem = e.AddedItems[0] as MultiViewTabItem;
        if (addedItem != null)
            SelectionChanged?.Invoke(this, addedItem.Key);
    }


}