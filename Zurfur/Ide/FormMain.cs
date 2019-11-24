﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using Gosub.Zurfur.Compiler;
using Gosub.Zurfur.Ide; // TBD: Change all namespaces in IDE directory

namespace Gosub.Zurfur
{
    public partial class FormMain:Form
    {
        bool                mInActivatedEvent;
        BuildPackage mZurfBuilder;
        ZurfEditController mZurfEditController;

        static readonly WordSet sTextEditorExtensions = new WordSet(".txt .json .md .htm .html .css");
        static readonly WordSet sImageEditorExtensions = new WordSet(".jpg .jpeg .png .bmp");

        static readonly string ZURFUR_PROJ_EXT = ".zurfproj";
        static readonly string ZURFUR_SRC_MAIN = "Example.zurf";
        static readonly string EXE_DIR = Path.GetDirectoryName(Application.ExecutablePath);
        static readonly string LICENSE_FILE_NAME = Path.Combine(EXE_DIR, "License.txt");
        static readonly string EXAMPLE_PROJECT = Path.Combine(EXE_DIR, "ZurfurLib\\ZurfurLib.zurfproj");
        static readonly string DEFAULT_NEW_FILE_NAME = "(new file)";

        public FormMain()
        {
            mZurfBuilder = new BuildPackage();
            mZurfEditController = new ZurfEditController(mZurfBuilder);
            InitializeComponent();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            Text += " - " + "V" + App.Version;
        }

        private void FormMain_Shown(object sender, EventArgs e)
        {
            if (System.Diagnostics.Debugger.IsAttached)
            {
                // This will be removed
                LoadProject(EXAMPLE_PROJECT);
                return;
            }
            try
            {
                // This will be removed
                LoadProject(EXAMPLE_PROJECT);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error loading example: " + ex.Message, App.Name);
            }
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!CanCloseAll())
                e.Cancel = true;
        }

        bool CanCloseAll()
        {
            while (mvEditors.EditorViewActive != null)
            {
                if (!CanClose(mvEditors.EditorViewActive))
                    return false;
                mvEditors.CloseEditor(mvEditors.EditorViewActive);
            }
            return true;
        }

        /// <summary>
        /// Can we close this editor?  Give user a chance to save first.
        /// </summary>
        bool CanClose(IEditor editor)
        {
            if (!editor.Modified)
                return true;
            mvEditors.EditorViewActive = editor;
            projectTree.OpenAndSelect(editor.FilePath);
            var dialogResult = MessageBox.Show(editor.FilePath + " has unsaved changes.  \r\n\r\n"
                + "Do you want to save this file?", App.Name, MessageBoxButtons.YesNoCancel);
            if (dialogResult == DialogResult.No)
                return true;
            if (dialogResult == DialogResult.Cancel)
                return false;
            if (!SaveFile(editor, false))
                return false;
            return true;
        }

        private void mvEditors_EditorCanClose(IEditor editor, ref bool doNotClose)
        {
            if (!CanClose(editor))
            {
                doNotClose = true;
            }
        }

        private void mvEditors_EditorAdded(IEditor editor)
        {
            var textEditor = editor as TextEditor;
            if (textEditor != null)
                mZurfEditController.AddEditor(textEditor);
        }

        private void mvEditors_EditorRemoved(IEditor editor)
        {
            var textEditor = editor as TextEditor;
            if (textEditor != null)
                mZurfEditController.RemoveEditor(textEditor);
        }

        private void mvEditors_EditorActiveViewChanged(IEditor editor)
        {
            mZurfEditController.ActiveViewChanged(editor as TextEditor);

            if (editor != null)
                projectTree.Select(editor.FilePath);
            else
                projectTree.OpenAndSelect(""); // NoSelection
        }

        void menuFileOpenProject_Click(object sender, EventArgs e)
        {
            openFileDialog1.FileName = "";
            openFileDialog1.Title = "Load Project";
            openFileDialog1.Multiselect = false;
            openFileDialog1.Filter = "Zurfur Project|*" + ZURFUR_PROJ_EXT;
            var dialogResult = openFileDialog1.ShowDialog(this);
            if (dialogResult != DialogResult.OK || openFileDialog1.FileName == "")
                return;
            TryLoadFileOrProject(openFileDialog1.FileName);
        }

        void menuFileOpenFile_Click(object sender, EventArgs e)
        {
            openFileDialog1.FileName = "";
            openFileDialog1.Title = "Load File";
            openFileDialog1.Multiselect = false;
            openFileDialog1.Filter = "All|*.*";
            var dialogResult = openFileDialog1.ShowDialog(this);
            if (dialogResult != DialogResult.OK || openFileDialog1.FileName == "")
                return;
            TryLoadFileOrProject(openFileDialog1.FileName);
        }

        /// <summary>
        /// Show file dialog, then load file or project
        /// </summary>
        void TryLoadFileOrProject(string fileName)
        {
            var isProject = Path.GetExtension(fileName).ToLower() == ZURFUR_PROJ_EXT;
            try
            {
                if (isProject)
                    LoadProject(fileName);
                else
                    LoadFile(fileName);
            }
            catch (Exception ex)
            {
                var errorMessage = isProject ? "Error loading project: "
                                             : "Error loading file: ";
                MessageBox.Show(this, errorMessage + ex.Message, App.Name);
            }
        }

        void LoadProject(string fileName)
        {
            if (!CanCloseAll())
                return;

            projectTree.RootDir = "";
            var project = ZurfProject.Load(fileName);
            projectTree.RootDir = project.ProjectDirectory;

            // For the time being, we'll open the default file.
            foreach (var file in projectTree)
                if (file.FileName.ToLower() == ZURFUR_SRC_MAIN.ToLower())
                {
                    projectTree.OpenAndSelect(file.Path);
                    LoadFile(file.Path);
                    break;
                }
        }

        void LoadFile(string path)
        {
            // Check for aleady loaded file path
            path = Path.GetFullPath(path);
            foreach (var editor in mvEditors.Editors)
            {
                if (editor.FilePath.ToLower() == path.ToLower())
                {
                    mvEditors.EditorViewActive = editor;
                    return;
                }
            }

            // For now just use extension to see if we can open it
            var ext = Path.GetExtension(path).ToLower();
            if (ext == ".zurf")
            {
                // TBD: Move lexer/parser setup to it's own class
                var newEditor = new TextEditor();
                newEditor.Lexer.SetSpecialSymbols(ZurfParse.MULTI_CHAR_TOKENS);
                newEditor.Lexer.TokenizeComments = true;
                newEditor.LoadFile(path);
                mvEditors.AddEditor(newEditor);
            }
            else if (sTextEditorExtensions.Contains(ext))
            {
                var newEditor = new TextEditor();
                newEditor.Lexer.TokenizeComments = ext == ".json";
                newEditor.LoadFile(path);
                mvEditors.AddEditor(newEditor);
            }
            else if (sImageEditorExtensions.Contains(ext))
            {
                var newEditor = new ImageEditor();
                newEditor.LoadFile(path);
                mvEditors.AddEditor(newEditor);
            }
            else if (ext == ".zurfproj")
            {
                var newEditor = new ProjectEditor();
                newEditor.LoadFile(path);
                mvEditors.AddEditor(newEditor);
            }
            else
            {
                MessageBox.Show(this, "Can't open this file type", App.Name);
                return;
            }
        }

        private void menuFileNewFile_Click(object sender, EventArgs e)
        {
            var editor = new TextEditor();
            editor.FilePath = DEFAULT_NEW_FILE_NAME;
            mvEditors.AddEditor(editor);
        }

        private void menuFileNewProject_Click(object sender, EventArgs e)
        {
            try
            {
                saveFileDialog1.DefaultExt = "";
                saveFileDialog1.FileName = "";
                saveFileDialog1.Title = "Create Zurfur Project";
                saveFileDialog1.Filter = "All (*.*) |*.*";
                saveFileDialog1.CheckFileExists = false;
                saveFileDialog1.CheckPathExists = false;
                var dialogResult = saveFileDialog1.ShowDialog(this);
                var projectDir = saveFileDialog1.FileName;
                if (dialogResult != DialogResult.OK || projectDir == "")
                    return;

                if (Path.GetFileName(projectDir).Contains("."))
                {
                    MessageBox.Show(this, "Do not include an extension in the file name", App.Name);
                    return;
                }
                if (File.Exists(projectDir) || Directory.Exists(projectDir))
                {
                    MessageBox.Show(this, "Select a name that does not already exist", App.Name);
                    return;
                }
                if (!CanCloseAll())
                    return;

                // Create empty project
                var projectName = Path.GetFileName(projectDir);
                var configPath = Path.Combine(projectDir, projectName + ZURFUR_PROJ_EXT);
                var projectSubdir = Path.Combine(projectDir, projectName);
                var zurfSourcePath = Path.Combine(projectSubdir, ZURFUR_SRC_MAIN);
                Directory.CreateDirectory(projectDir);
                Directory.CreateDirectory(projectSubdir);
                new ZurfProject().Save(configPath);
                File.WriteAllText(zurfSourcePath,
                      "using Zurfur;\r\n"
                    + "namespace " + projectName + ";\r\n"
                    + "pub static func main(args[]string)\r\n"
                    + "{\r\n}\r\n");

                // Load the proejct
                TryLoadFileOrProject(configPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error creating project: " + ex.Message, App.Name);
            }
        }

        private void menuFileSave_Click(object sender, EventArgs e)
        {
            if (mvEditors.EditorViewActive == null)
                return;
            SaveFile(mvEditors.EditorViewActive, false);
        }

        private void menuFileSaveAll_Click(object sender, EventArgs e)
        {
            foreach (var editor in mvEditors.Editors)
                SaveFile(editor, false);
        }

        private void menuFileSaveAs_Click(object sender, EventArgs e)
        {
            if (mvEditors.EditorViewActive == null)
                return;
            SaveFile(mvEditors.EditorViewActive, true);
        }

        /// <summary>
        /// Returns true if the file was saved
        /// </summary>
        bool SaveFile(IEditor editor, bool forceSaveAs)
        {
            var filePath = editor.FilePath;
            if (filePath == "" || filePath == DEFAULT_NEW_FILE_NAME || forceSaveAs)
            {
                saveFileDialog1.DefaultExt = "";
                saveFileDialog1.FileName = "";
                saveFileDialog1.Title = "Create Zurfur Project";
                saveFileDialog1.Filter = "All (*.*) |*.*";
                saveFileDialog1.CheckFileExists = false;
                saveFileDialog1.CheckPathExists = true;
                var dialogResult = saveFileDialog1.ShowDialog(this);
                if (dialogResult != DialogResult.OK || saveFileDialog1.FileName == "")
                    return false;
                filePath = saveFileDialog1.FileName;
            }
            try
            {
                editor.SaveFile(filePath);
                projectTree.RefreshFiles();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error saving file: " + ex.Message, App.Name);
            }
            return false;
        }

        /// <summary>
        /// Recompile or display hover message when necessary
        /// </summary>
        private void timer1_Tick(object sender, EventArgs e)
        {
            mZurfEditController.Timer();
            mZurfBuilder.Timer();
        }

        private void menuHelpAbout_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, App.Name + " version " + App.Version, App.Name);
        }

        private void menuHelpLicense_Click(object sender, EventArgs e)
        {
            // Read license from resource
            List<string> lines = new List<string>();
            Stream s = File.OpenRead(LICENSE_FILE_NAME);
            StreamReader sr = new StreamReader(s);
            while (!sr.EndOfStream)
                lines.Add(sr.ReadLine());
            sr.Close();

            // Display the license
            FormHtml form = new FormHtml();
            form.ShowText(lines.ToArray());
        }

        private void menuEditFind_Click(object sender, EventArgs e)
        {
            var activeTextEditor = mvEditors.EditorViewActive as TextEditor;
            if (activeTextEditor != null)
                FormSearch.Show(this, activeTextEditor);
        }

        private void menuEditFindNext_Click(object sender, EventArgs e)
        {
            var activeTextEditor = mvEditors.EditorViewActive as TextEditor;
            if (activeTextEditor != null)
                FormSearch.FindNext(this, activeTextEditor);
        }

        private void viewRTFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ed = mvEditors.EditorViewActive as TextEditor;
            if (ed == null)
                return;
            FormHtml form = new FormHtml();

            form.ShowLexer(ed.Lexer, ed.SelStart.Y, ed.SelEnd.Y - ed.SelStart.Y);
        }

        private void FormMain_Activated(object sender, EventArgs e)
        {
            // Prevent from getting called again when user answers a question
            if (mInActivatedEvent)
                return;
            mInActivatedEvent = true;

            try
            {
                foreach (var editor in mvEditors.Editors)
                {
                    if (editor.FileInfo == null || editor.FilePath == "")
                        continue;
                    var newFileInfo = new FileInfo(editor.FilePath);
                    newFileInfo.Refresh();
                    if (newFileInfo.LastWriteTimeUtc == editor.FileInfo.LastWriteTimeUtc)
                        continue;

                    mvEditors.EditorViewActive = editor;
                    var message = editor.Modified ?
                        "This file has UNSAVED CHANGES inside this editor and "
                        + "has been changed externlly.\r\n\r\n Do you want to "
                        + "reload it and lose the changes made in the editor?"
                        : "This file has been changed externally, and has no "
                        + "unsaved changes inside the editor.\r\n\r\n"
                        + "Do you want to reload it?";
                    if (MessageBox.Show(this, editor.FilePath + "\r\n\r\n" + message,
                        App.Name, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        editor.LoadFile(editor.FilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error: " + ex.Message, App.Name);
            }
            finally
            {
                mInActivatedEvent = false;
            }
        }

        private void menuFile_DropDownOpening(object sender, EventArgs e)
        {
            var activeEditor = mvEditors.EditorViewActive;
            menuFileSave.Enabled = activeEditor != null && activeEditor.Modified;
            menuFileSaveAs.Enabled = activeEditor != null;
            menuFileSaveAll.Enabled = false;
            foreach (var editor in mvEditors.Editors)
                if (editor.Modified)
                    menuFileSaveAll.Enabled = true;
        }

        private void menuEdit_DropDownOpening(object sender, EventArgs e)
        {
            var textEditor = mvEditors.EditorViewActive as TextEditor;
            menuEditFind.Enabled = textEditor != null;
            menuEditFindNext.Enabled = textEditor != null;
            viewRTFToolStripMenuItem.Enabled = textEditor != null;
        }

        private void menuDebug_DropDownOpening(object sender, EventArgs e)
        {
            menuDebugRun.Enabled = projectTree.RootDir != "";
        }



        private void projectTree_FileDoubleClicked(object sender, ProjectTree.FileInfo file)
        {
            if (file.IsDir)
                return;

            try
            {
                LoadFile(file.Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error opening file: " + ex.Message, App.Name);
            }

        }

        private void projectTree_FileMoved(object sender, ProjectTree.FileInfo oldFile, ProjectTree.FileInfo newFile)
        {
            var oldFileStr = oldFile.Path.ToLower();
            foreach (var editor in mvEditors.Editors)
                if (editor.FilePath.ToLower() == oldFileStr)
                    editor.FilePath = newFile.Path;
        }

        private void menuDebugRun_Click(object sender, EventArgs e)
        {
            // TBD: Load and store tatget from ZurfProject file.
            // TBD: Need to copy all files into a \bin\debug folder
            foreach (var file in projectTree)
            {
                if (file.Path.Contains("www") && Path.GetFileName(file.Path).ToLower() == "index.html")
                {
                    System.Diagnostics.Process.Start(file.Path);
                    return;
                }
            }
            MessageBox.Show(this, "'index.html' file not found in www directory", App.Name);
        }

        private void FormMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
                menuDebugRun_Click(null, null);
        }
    }
}
