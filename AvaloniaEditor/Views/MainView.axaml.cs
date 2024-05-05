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


namespace AvaloniaEdit.Views;


public partial class MainView : UserControl
{
    const string ZURFUR_LIB_URL = "avares://ZurfurLib/ZurfurLibForAvalonia";

    BuildSystem mBuildPackage = new BuildSystem(new FileSystemAvalonia());

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

        var exeLocation = Assembly.GetExecutingAssembly().Location;
        var dirName = Path.GetDirectoryName(exeLocation) ?? "";
        LoadProject();
        lexerEdit.TextChanged += TextEdit_TextChanged;
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
    private void TextEdit_TextChanged(object? sender, EventArgs e)
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
            //mRecompileForChangedLine = lexerEdit.CursorLoc.Y;
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
                lexerEdit.Lexer = lexer;
        }
    }

}
