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
    /// Manage a group of editors that implement IEditor interface.
    /// </summary>
    public partial class MultiViewEditor : UserControl
    {
        int mHoverTab;
        IEditor mEditorViewActive;

        class EditorTabPage : TabPage
        {
            public IEditor Editor { get; set; }
        }

        public delegate void EditorDelegate(IEditor editor);
        public delegate void EditorCanCloseDelegate(IEditor editor, ref bool doNotClose);
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
        public IEditor EditorViewActive
        {
            get { return mEditorViewActive; }
            set
            {
                if (value == null)
                    return;
                mEditorViewActive = value;
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
                edTabPage.Text = Path.GetFileName(editor.FilePath) + (editor.Modified ? "*" : " ") + "      ";
                edTabPage.ToolTipText = editor.FilePath;
            }
        }

        /// <summary>
        /// Load a file into a new tab.  NOTE: This does not check for duplicate file paths. 
        /// </summary>
        public void AddEditor(IEditor newEditor)
        {
            // Check for aleady loaded editor control (not path)
            foreach (var editor in Editors)
            {
                if (editor == newEditor)
                {
                    EditorViewActive = editor;
                    return;
                }
            }

            // Setup tab
            var oldTabPageCount = mainTabControl.TabPages.Count;
            var tabPage = CreateEditorTab(newEditor);
            mainTabControl.ShowToolTips = true;
            tabPage.Editor = newEditor;
            TouchTitles();

            // Select page
            mainTabControl.SelectedTab = tabPage;
            if (oldTabPageCount == 0)
                mainTabControl_Selected(null, null); // Need to show manually the first time
            EditorAdded?.Invoke(tabPage.Editor);
        }

        EditorTabPage CreateEditorTab(IEditor ieditor)
        {
            // Mostly copied from deisgner
            var tabPage = new EditorTabPage();
            tabPage.Editor = ieditor;
            var editor = ieditor.GetControl();

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
            editor.Size = new Size(694, 379);
            editor.TabIndex = 0;
            ieditor.ModifiedChanged += Editor_UpdateTitles;
            ieditor.FilePathChanged += Editor_UpdateTitles;

            editor.ResumeLayout();
            tabPage.ResumeLayout();
            mainTabControl.ResumeLayout();
            return tabPage;
        }

        private void Editor_UpdateTitles(object sender, EventArgs e)
        {
            TouchTitles();
        }

        private void mainTabControl_Selected(object sender, TabControlEventArgs e)
        {
            var editTab = mainTabControl.SelectedTab as EditorTabPage;
            if (editTab != null)
            {
                ActiveControl = editTab.Editor.GetControl();
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
        public void CloseEditor(IEditor editor)
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
                    return;
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
        public IEnumerable<IEditor> Editors
            => new EditorEnum(this);

        public struct EditorEnum : IEnumerable<IEditor>
        {
            MultiViewEditor mMultiEditor;
            public EditorEnum(MultiViewEditor multiViewEditors)
            {
                mMultiEditor = multiViewEditors;
            }
            public IEnumerator<IEditor> GetEnumerator()
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
