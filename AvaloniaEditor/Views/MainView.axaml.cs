using System.Collections.Generic;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reflection;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Zurfur.Build;
using Gosub.Avalonia.Controls;
using Avalonia.Platform;
using System.Linq;
using Gosub.Lex;
using Avalonia.Input;
using Zurfur;
using Avalonia.Layout;

namespace AvaloniaEditor.Views;


public partial class MainView : UserControl
{
    const string ZURFUR_LIB_URL = "avares://ZurfurLib/ZurfurLib";
    static readonly WordSet s_textEditorExtensions = new WordSet(".txt .json .md .htm .html .css .zurf .zil");
    static readonly WordSet s_imageEditorExtensions = new WordSet(".jpg .jpeg .png .bmp");

    FileSystemAvalonia _fileSystem = new();
    BuildSystem _buildPackage;
    ZurfEditController _editController = new();

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
        _buildPackage = new(_fileSystem);
        InitializeComponent();
    }
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        labelStatus.Content = $"*{Assembly.GetExecutingAssembly().Location}*";
        _buildPackage.StatusUpdate += mBuildPackage_StatusUpdate;
        _buildPackage.FileUpdate += mBuildPackage_FileUpdate;
        _editController.OnNavigateToSymbol += mEditController_OnNavigateToSymbol;
        _editController.SetHoverMessageParent(panelMain);
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
                _buildPackage.Threading = BuildSystem.ThreadingModel.SingleAwait;
            }

            var urls = AssetLoader.GetAssets(new Uri(ZURFUR_LIB_URL), null).ToList();
            if (urls.Count <= 0)
                throw new Exception("Zurfur library was not found");
            labelStatus.Content = $"{urls.Count} files found";
            foreach (var url in urls)
            {
                _buildPackage.LoadFile(url.AbsoluteUri);
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
            LoadOrUpdateEditorFromBuildSystem(e.Message);
        }
        else if (e.Message == "")
        {
            // Update all open editors
            foreach (var tabKey in mvCodeEditors.TabKeys)
            {
                var editor = mvCodeEditors.FindTabContent(tabKey) as Editor;
                if (editor != null)
                    LoadOrUpdateEditorFromBuildSystem(tabKey);
            }
        }
        else
        {
            // Any other time, update when the editor window is open
            var editor = mvCodeEditors.FindTabContent(e.Message) as Editor;
            if (editor != null)
                LoadOrUpdateEditorFromBuildSystem(e.Message);
        }
    }

    private void mvCodeEditors_SelectionChanged(object? sender, string key)
    {
        var editor = mvCodeEditors.FindTabContent(key) as Editor;
        Debug.Assert(editor != null); // Currently only editors added to mvCodeEditors
        if (editor == null)
            return;

        _editController.ActiveViewChanged(editor);

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
            LoadOrUpdateEditorFromBuildSystem(path);
            mvCodeEditors.ShowTab(path);
        }
    }

    private async void mEditController_OnNavigateToSymbol(string path, int x, int y)
    {
        await LoadOrUpdateEditor(path);
        var editor = mvCodeEditors.ShowTab(path) as Editor;
        if (editor != null)
        {
            var loc = new TokenLoc(x, y);
            editor.SelSet(loc, loc);
            editor.CursorLoc = loc;
        }
    }

    public async void buttonGenerateTapped(object source, TappedEventArgs args)
    {
        await _buildPackage.GeneratePackage();

        // projectTree.RefreshFiles();
        List<string> files =  [
            _buildPackage.OutputFileHeader,
            _buildPackage.OutputFileHeaderCode,
            _buildPackage.OutputFileReport,
        ];
        foreach (var name in files)
            await LoadOrUpdateEditor(name);

        mvCodeEditors.ShowTab(_buildPackage.OutputFileReport);
    }

    public void buttonSearchTapped(object source, TappedEventArgs args)
    {
        _editController.ShowSearchForm();
    }

    // Load or update editor from build system (if possible) or file system (if not possible)
    async Task LoadOrUpdateEditor(string path)
    {
        if (!LoadOrUpdateEditorFromBuildSystem(path))
            await LoadOrUpdateEditorFromFileSystem(path);
    }

    // Returns TRUE if the file was found in the build system
    private bool LoadOrUpdateEditorFromBuildSystem(string path)
    {
        // Attempt to open from build system
        var lexer = _buildPackage.GetLexer(path);
        if (lexer == null)
            return false;

        if (mvCodeEditors.FindTabContent(path) is Editor editor)
        {
            // Update the editor
            editor.Lexer = lexer;
        }
        else
        {
            // Add new editor
            var newEditor = new Editor() { Lexer = lexer };
            AddNewEditor(path, newEditor);
        }
        return true;
    }


    async Task LoadOrUpdateEditorFromFileSystem(string path)
    {
        // Attempt to load from file system
        try
        {
            var buildFile = Path.GetExtension(path).ToLower();
            if (s_textEditorExtensions.Contains(buildFile))
            {
                // Text file
                var l = new Lexer();
                l.Scan(await _fileSystem.ReadAllLinesAsync(path));
                var newEditor = new Editor() { Lexer = l };
                AddNewEditor(path, newEditor);
            }
            else if (s_imageEditorExtensions.Contains(buildFile))
            {
                // TBD: Open an image editor
                throw new Exception("File type not supported");
            }
        }
        catch (Exception ex)
        {
            // Ignore file not found of there is no editor for this file type
            // TBD: Display some kind of message
            Debug.Assert(false, ex.Message);
            var l = new Lexer();
            l.Scan([$"ERROR: {ex.Message}", "", $"STACK: {ex.StackTrace}"]);
            mvCodeEditors.SetTab("PopupError", new Editor() { Lexer = l }, "Error");
        }
    }

    private void AddNewEditor(string path, Editor newEditor)
    {
        newEditor.TextChanged += editor_TextChanged;
        mvCodeEditors.SetTab(path, newEditor, Path.GetFileName(path));
        _editController.AttachEditor(newEditor);
    }

    private void editor_TextChanged(object? sender, EventArgs e)
    {
        // Notify build system
        var editor = sender as Editor;
        if (editor == null)
            return;

        // Recompile if file is part of the build project
        var lexer = _buildPackage.GetLexer(editor.Lexer.Path);
        if (lexer != null && lexer.Path == editor.Lexer.Path)
        {
            _buildPackage.SetLexer(editor.Lexer);
        }
    }



}
