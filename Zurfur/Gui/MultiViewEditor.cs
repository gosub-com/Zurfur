using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Collections;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Manage a group of editors.
    /// </summary>
    public partial class MultiViewEditor : UserControl
    {
        int mHoverTab;
        Editor mEditorViewActive;

        class EditorTabPage : TabPage
        {
            public Editor Editor { get; set; }
        }

        public delegate void EditorDelegate(Editor editor);
        public delegate void EditorCanCloseDelegate(Editor editor, ref bool doNotClose);
        public event EditorDelegate EditorAdded;
        public event EditorDelegate EditorRemoved;
        public event EditorDelegate EditorActiveViewChanged;
        public event EditorCanCloseDelegate EditorCanClose;

        public MultiViewEditor()
        {
            InitializeComponent();
        }

        /// <summary>
        /// When set, must be set to an editor that is being manged by this control.
        /// This property can be NULL, but setting it to NULL is ignored.
        /// </summary>
        public Editor EditorViewActive
        {
            get { return mEditorViewActive; }
            set
            {
                if (value == null)
                    return;
                foreach (var tab in mainTabControl.TabPages)
                    if (((EditorTabPage)tab).Editor == value)
                    {
                        mainTabControl.SelectedTab = (TabPage)tab;
                        return;
                    }
                throw new IndexOutOfRangeException("EditorViewActive must be set to an editor that is managed by this control");
            }
        }


        /// <summary>
        /// This should be called if you change the FileTitle of the text editor control
        /// </summary>
        public void TouchTitles()
        {
            foreach (var tabPage in mainTabControl.TabPages)
            {
                var edTabPage = (EditorTabPage)tabPage;
                var editor = edTabPage.Editor;
                edTabPage.Text = "" + editor.FileTitle + (editor.Modified ? "*" : " ") + "      ";
                edTabPage.ToolTipText = editor.FilePath == "" ? editor.FileTitle : editor.FilePath;
            }
        }

        /// <summary>
        /// Load a file into a new tab.  
        /// Sets the Editor's FilePath, FileTitle, and FileInfo
        /// </summary>
        public async Task LoadFile(string path)
        {
            path = Path.GetFullPath(path);
            foreach (var editor in Editors)
            {
                if (path.ToLower() == editor.FilePath.ToLower())
                {
                    EditorViewActive = editor;
                    return;
                }
            }

            var newEditor = new Editor();
            FileInfo fileInfo = null;
            await Task.Run(() => 
            {
                newEditor.Lexer.ScanLines(File.ReadAllLines(path));
                fileInfo = new FileInfo(path);
                fileInfo.Refresh();
            });

            // Setup tab
            var oldTabPageCount = mainTabControl.TabPages.Count;
            var tabPage = CreateEditorTab(newEditor);
            mainTabControl.ShowToolTips = true;
            tabPage.Editor.FileInfo = fileInfo;
            tabPage.Editor.FilePath = path;
            tabPage.Editor.FileTitle = Path.GetFileName(path);
            TouchTitles();

            // Select page
            mainTabControl.SelectedTab = tabPage;
            if (oldTabPageCount == 0)
                mainTabControl_Selected(null, null); // Need to show manually the first time
            EditorAdded?.Invoke(tabPage.Editor);
        }

        EditorTabPage CreateEditorTab(Editor editor)
        {
            // Mostly copied from deisgner
            var tabPage = new EditorTabPage();
            tabPage.Editor = editor;

            mainTabControl.SuspendLayout();
            tabPage.SuspendLayout();
            editor.SuspendLayout();
            mainTabControl.TabPages.Add(tabPage);

            tabPage.Controls.Add(editor);
            tabPage.Location = new Point(4, 22);
            tabPage.Padding = new Padding(0);
            tabPage.Size = new Size(700, 385);
            tabPage.TabIndex = 0;
            tabPage.Text = "tabPageEdit";
            tabPage.UseVisualStyleBackColor = true;
            editor.BackColor = SystemColors.Window;
            editor.Cursor = Cursors.IBeam;
            editor.Dock = DockStyle.Fill;
            editor.Font = new Font("Courier New", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            editor.Location = new Point(3, 3);
            editor.OverwriteMode = false;
            editor.ReadOnly = false;
            editor.Size = new Size(694, 379);
            editor.TabIndex = 0;
            editor.TabSize = 4;
            editor.TokenColorOverrides = null;
            editor.ModifiedChanged += Editor_ModifiedChanged;

            editor.ResumeLayout();
            tabPage.ResumeLayout();
            mainTabControl.ResumeLayout();
            return tabPage;
        }

        public void NewFile()
        {
            var tab = CreateEditorTab(new Editor());
            tab.Editor.FileTitle = "(new file)";
            EditorViewActive = tab.Editor;
            TouchTitles();
        }

        public void Save(Editor editor, string filePath)
        {
            filePath = Path.GetFullPath(filePath);
            File.WriteAllLines(filePath, editor.Lexer.GetText());
            editor.Modified = false;
            editor.FilePath = filePath;
            editor.FileTitle = Path.GetFileName(filePath);
            editor.FileInfo = new FileInfo(filePath);
            editor.FileInfo.Refresh();
            TouchTitles();
        }

        private void Editor_ModifiedChanged(object sender, EventArgs e)
        {
            TouchTitles();
        }

        private void mainTabControl_Selected(object sender, TabControlEventArgs e)
        {
            var editTab = mainTabControl.SelectedTab as EditorTabPage;
            if (editTab != null)
            {
                ActiveControl = editTab.Editor;
                mEditorViewActive = editTab.Editor;
                EditorActiveViewChanged?.Invoke(editTab.Editor);
            }
        }

        private void editorTabControl_MouseMove(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < mainTabControl.TabCount; i++)
            {
                var rect = mainTabControl.GetTabRect(i);
                if (rect.Contains(e.X, e.Y))
                {
                    mHoverTab = i;
                    buttonClose.Location = new Point(rect.Right - buttonClose.Width, rect.Y + 1);
                    buttonClose.Visible = true;
                    buttonClose.BringToFront();
                }
            }
        }

        private void editorTabControl_MouseUp(object sender, MouseEventArgs e)
        {
            // Close editor?
            if (buttonClose.Visible && buttonClose.Bounds.Contains(e.X, e.Y)
                && mHoverTab < mainTabControl.TabPages.Count)
            {
                // User wants to close this tab. 
                // Allow control owner to intercept and change behavior
                var editor = ((EditorTabPage)mainTabControl.TabPages[mHoverTab]).Editor;
                var doNotClose = false;
                EditorCanClose?.Invoke(editor, ref doNotClose);
                if (!doNotClose)
                    CloseEditor(editor);
            }
        }

        /// <summary>
        /// Close this editor, no questions asked
        /// </summary>
        public void CloseEditor(Editor editor)
        {
            foreach (var tab in mainTabControl.TabPages)
            {
                if (((EditorTabPage)tab).Editor == editor)
                {
                    mainTabControl.TabPages.Remove((TabPage)tab);
                    EditorRemoved?.Invoke(editor);
                    if (mainTabControl.TabPages.Count == 0)
                    {
                        // SelectedTab does this for all other cases
                        mEditorViewActive = null;
                        EditorActiveViewChanged?.Invoke(null);
                    }
                }
            }
        }

        private void editorTabControl_MouseLeave(object sender, EventArgs e)
        {
            buttonClose.Visible = false;
        }

        /// <summary>
        /// Enumerate the editors
        /// </summary>
        public IEnumerable<Editor> Editors
            => new EditorEnum(this);

        public struct EditorEnum : IEnumerable<Editor>
        {
            MultiViewEditor mMultiEditor;
            public EditorEnum(MultiViewEditor multiViewEditors)
            {
                mMultiEditor = multiViewEditors;
            }
            public IEnumerator<Editor> GetEnumerator()
            {
                foreach (var tab in mMultiEditor.mainTabControl.TabPages)
                    yield return ((EditorTabPage)tab).Editor;
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                throw new NotImplementedException();
            }
        }

    }
}
