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


namespace AvaloniaEditor.Views;


public partial class MainView : UserControl
{
    const string ZURFUR_LIB_URL = "avares://ZurfurLib/ZurfurLibForAvalonia";

    BuildSystem mBuildPackage = new (new FileSystemAvalonia());
    ZurfEditController mEditController = new();

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
        mEditController.SetHoverMessageForm(hoverMessage);
        mvCodeEditors.SelectionChanged += MvCodeEditors_SelectionChanged;


        var exeLocation = Assembly.GetExecutingAssembly().Location;
        var dirName = Path.GetDirectoryName(exeLocation) ?? "";
        LoadProject();
    }

    private void MvCodeEditors_SelectionChanged(object? sender, Control e)
    {
        var editor = e as Editor;
        if (editor == null)
            return;

        mEditController.ActiveViewChanged(editor);
        //FormSearchInstance.SetEditor(textEditor);
        //labelPos.Text = textEditor == null ? "" : $"{textEditor.CursorLoc.Y + 1}:{textEditor.CursorLoc.X + 1}";
        //if (editor != null)
        //    projectTree.Select(editor.FilePath);
        //else
        //    projectTree.OpenAndSelect(""); // NoSelection
    }



    async void LoadProject()
    {
        await Task.Delay(1);
        try
        {
            if (Assembly.GetExecutingAssembly().Location == "")
            {
                mBuildPackage.DisableVerificationAndReports = true;
                mBuildPackage.Threading = BuildSystem.ThreadingModel.SingleAwait;
            }

            var urls = AssetLoader.GetAssets(new Uri(ZURFUR_LIB_URL), null).ToList();
            if (urls.Count < 0)
                throw new Exception("Zurfur library was not found");
            labelStatus.Content = $"{urls.Count} files found";
            foreach (var url in urls)
            {
                mBuildPackage.LoadFile(url.AbsoluteUri);
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
            var lexer = mBuildPackage.GetLexer(e.Message);
            if (lexer != null)
            {
                if (mvCodeEditors.FindTab(e.Message) is Editor editor)
                {
                    // Update the editor
                    editor.Lexer = lexer;
                }
                else
                {
                    // Add new editor
                    var newEditor = new Editor() { Lexer = lexer };
                    newEditor.TextChanged += editor_TextChanged;
                    newEditor.MouseHoverTokenChanged += editor_MouseHoverTokenChanged;
                    mvCodeEditors.SetTab(e.Message, newEditor, Path.GetFileName(e.Message));
                    mEditController.AddEditor(newEditor);
                }
            }
        }
    }

    private void editor_MouseHoverTokenChanged(object sender, Gosub.Lex.Token? previousToken, Gosub.Lex.Token? newToken)
    {
        //hoverMessage.IsVisible = newToken != null;
        //if (newToken != null)
        //{            
        //    hoverMessage.Message = newToken.Name;
        //}
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


    void buttonTest_Click(object? sender, RoutedEventArgs e)
    {
    }

}
