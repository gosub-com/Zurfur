using System.Collections.Generic;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reflection;

using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Zurfur.Build;
using Gosub.Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using System.Linq;
using System.Threading;
using Avalonia.VisualTree;
using Gosub.Lex;


namespace AvaloniaEditor.Views;


public partial class MainView : UserControl
{
    const string ZURFUR_LIB_URL = "avares://ZurfurLib/ZurfurLibForAvalonia";

    BuildSystem mBuildPackage = new (new FileSystemAvalonia());
    ZurfEditController mEditController = new();

    // Helper class to show items in the tree view
    record class NamedItem<T>(string Name, T Item)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    public MainView()
    {
        InitializeComponent();
    }
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        labelStatus.Content = $"*{Assembly.GetExecutingAssembly().Location}*";
        mBuildPackage.StatusUpdate += mBuildPackage_StatusUpdate;
        mBuildPackage.FileUpdate += mBuildPackage_FileUpdate;
        mEditController.OnNavigateToSymbol += mEditController_OnNavigateToSymbol;
        mEditController.SetHoverMessageParent(panelMain);
        mvCodeEditors.SelectionChanged += mvCodeEditors_SelectionChanged;
        mvCodeEditors.TabCloseRequest += mvCodeEditors_TabCloseRequest;
        treeProject.SelectionChanged += treeProject_SelectionChanged;


        var exeLocation = Assembly.GetExecutingAssembly().Location;
        var dirName = Path.GetDirectoryName(exeLocation) ?? "";
        LoadProject();
    }

    async void LoadProject()
    {
        await Task.Delay(1);
        try
        {
            // Browser gets single threaded with await
            if (Assembly.GetExecutingAssembly().Location == "")
            {
                mBuildPackage.DisableVerificationAndReports = true;
                mBuildPackage.Threading = BuildSystem.ThreadingModel.SingleAwait;
            }

            var urls = AssetLoader.GetAssets(new Uri(ZURFUR_LIB_URL), null).ToList();
            if (urls.Count <= 0)
                throw new Exception("Zurfur library was not found");
            labelStatus.Content = $"{urls.Count} files found";
            foreach (var url in urls)
            {
                mBuildPackage.LoadFile(url.AbsoluteUri);
                treeProject.Items.Add(new NamedItem<Uri>(Path.GetFileName(url.AbsolutePath), url));
            }
        }
        catch (Exception ex)
        {
            labelStatus.Content = $"ERROR: {ex.Message}";
        }
    }

    void mBuildPackage_StatusUpdate(object sender, BuildSystem.UpdatedEventArgs e)
    {
        labelStatus.Content = e.Message;
    }

    private void mBuildPackage_FileUpdate(object sender, BuildSystem.UpdatedEventArgs e)
    {
        if (e.Message.ToLower().Contains("example"))
        {
            // At boot, always show the example
            LoadOrUpdateLexer(e.Message);
        }
        else
        {
            // Any other time, update when the editor window is open
            var editor = mvCodeEditors.FindTabContent(e.Message) as Editor;
            if (editor != null)
                LoadOrUpdateLexer(e.Message);
        }
    }

    private void LoadOrUpdateLexer(string path)
    {
        var lexer = mBuildPackage.GetLexer(path);
        Debug.Assert(lexer != null);
        if (lexer == null)
            return;

        if (mvCodeEditors.FindTabContent(path) is Editor editor)
        {
            // Update the editor
            editor.Lexer = lexer;
        }
        else
        {
            // Add new editor
            var newEditor = new Editor() { Lexer = lexer };
            newEditor.TextChanged += editor_TextChanged;
            mvCodeEditors.SetTab(path, newEditor, Path.GetFileName(path));
            mEditController.AddEditor(newEditor);
        }
    }


    private void mvCodeEditors_SelectionChanged(object? sender, string key)
    {
        var editor = mvCodeEditors.FindTabContent(key) as Editor;
        Debug.Assert(editor != null); // Currently only editors added to mvCodeEditors
        if (editor == null)
            return;

        mEditController.ActiveViewChanged(editor);

        var item = treeProject.Items.FirstOrDefault(i => (i as NamedItem<Uri>)?.Item.AbsoluteUri == editor.Lexer.Path);
        if (item != null)
            treeProject.SelectedItem = item;

        //FormSearchInstance.SetEditor(textEditor);
        //labelPos.Text = textEditor == null ? "" : $"{textEditor.CursorLoc.Y + 1}:{textEditor.CursorLoc.X + 1}";
        //if (editor != null)
        //    projectTree.Select(editor.FilePath);
        //else
        //    projectTree.OpenAndSelect(""); // NoSelection
    }

    private void mvCodeEditors_TabCloseRequest(object? sender, string key)
    {
        mvCodeEditors.RemoveTab(key);
    }

    private void treeProject_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var si = treeProject.SelectedItem as NamedItem<Uri>;
        if (si != null)
        {
            var path = si.Item.AbsoluteUri;
            LoadOrUpdateLexer(path);
            mvCodeEditors.ShowTab(path);
        }
    }


    private void editor_TextChanged(object? sender, EventArgs e)
    {
        // Notify build system
        var editor = sender as Editor;
        if (editor == null)
            return;

        // Recompile if file is part of the build project
        var lexer = mBuildPackage.GetLexer(editor.Lexer.Path);
        if (lexer != null && lexer.Path == editor.Lexer.Path)
        {
            mBuildPackage.SetLexer(editor.Lexer);
        }
    }

    private void mEditController_OnNavigateToSymbol(string path, int x, int y)
    {
    }

}
