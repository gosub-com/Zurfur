using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Gosub.Zurfur.Compiler;

namespace Gosub.Zurfur
{
    public partial class ProjectTree : UserControl
    {
        string mRootDir = "";
        DirInfo mDirInfo = new DirInfo();
        Dictionary<string, FileExtra> mFileExtra = new Dictionary<string, FileExtra>();

        public delegate void FileInfoDelegate(object sender, FileInfo file);
        public event FileInfoDelegate FileDoubleClicked;

        public delegate void FileMovedDelegate(object sender, FileInfo oldFile, FileInfo newFile);
        public event FileMovedDelegate FileMoved;

        FormHoverMessage mHoverForm;

        class FileExtra
        {
            public FileInfo Info;
            public TreeNode Node;
            public FileExtra(FileInfo info, TreeNode tree) { Info = info; Node = tree; }
            public override string ToString()
            {
                return Info == null ? base.ToString() : Info.Path;
            }
        }

        public ProjectTree()
        {
            InitializeComponent();
        }

        public string RootDir
        {
            get { return mRootDir; }

            set
            {
                if (mRootDir == value)
                    return;
                mRootDir = value;
                RefreshFiles();
            }
        }

        public void RefreshFiles()
        {
            var oldExtra = mFileExtra;
            var oldTopNode = treeView.TopNode;
            mFileExtra = new Dictionary<string, FileExtra>();
            if (mRootDir == "")
            {
                treeView.Nodes.Clear();
                mDirInfo = new DirInfo();
                return;
            }
            mRootDir = Path.GetFullPath(mRootDir);
            while (mRootDir.EndsWith("\\") || mRootDir.EndsWith("/"))
                mRootDir = mRootDir.Substring(0, mRootDir.Length - 1);
            treeView.BeginUpdate();
            treeView.Nodes.Clear();
            mDirInfo = LoadFiles(mRootDir);
            AddFileNodes(mDirInfo, treeView.Nodes);
            CopyOldNodeInfo(oldExtra, oldTopNode);
            treeView.EndUpdate();
        }

        DirInfo LoadFiles(string dir)
        {
            var dirInfo = new DirInfo(dir);
            var subDirInfo = new List<DirInfo>();
            var fileInfo = new List<FileInfo>();
            foreach (var subdir in Directory.GetDirectories(dir))
                subDirInfo.Add(LoadFiles(subdir));
            foreach (var fileName in Directory.GetFiles(dir))
                fileInfo.Add(new FileInfo(fileName));
            dirInfo.Directories = subDirInfo.ToArray();
            dirInfo.Files = fileInfo.ToArray();
            return dirInfo;
        }

        /// <summary>
        /// Add nodes to the tree.  Does not add the root dir.
        /// </summary>
        void AddFileNodes(DirInfo dir, TreeNodeCollection nodes)
        {
            foreach (var subdir in dir.Directories)
            {
                var node = nodes.Add(subdir.Path, subdir.FileName);
                node.ToolTipText = subdir.Path;
                mFileExtra[subdir.Path.ToLower()] = new FileExtra(subdir, node);
                AddFileNodes(subdir, node.Nodes);
            }
            foreach (var file in dir.Files)
            {
                var node = nodes.Add(file.Path, file.FileName);
                node.ToolTipText = file.Path;
                mFileExtra[file.Path.ToLower()] = new FileExtra(file, node);
            }
        }

        private void CopyOldNodeInfo(Dictionary<string, FileExtra> oldExtraInfo, TreeNode oldTopNode)
        {
            TreeNode newSelectedNode = null;
            TreeNode newTopNode = null;
            foreach (var newExtra in mFileExtra)
            {
                if (!oldExtraInfo.TryGetValue(newExtra.Key, out var oldExtra))
                    continue;
                var oldNode = oldExtra.Node;
                var newNode = newExtra.Value.Node;
                if (oldNode.IsSelected)
                    newSelectedNode = newNode;
                if (oldNode == oldTopNode)
                    newTopNode = newNode;
                if (oldNode.IsExpanded)
                    newNode.Expand();
            }
            if (newSelectedNode != null)
                treeView.SelectedNode = newSelectedNode;
            if (newTopNode != null)
                treeView.TopNode = newTopNode;
        }

        public void OpenAndSelect(string fileName)
        {
            if (!mFileExtra.TryGetValue(fileName.ToLower(), out var extra))
            {
                treeView.SelectedNode = null;
                return;
            }
            var node = extra.Node;
            treeView.SelectedNode = node;
            node.Expand();
            while (node.Parent != null)
            {
                node.Parent.Expand();
                node = node.Parent;
            }
        }

        public void Select(string fileName)
        {
            if (!mFileExtra.TryGetValue(fileName.ToLower(), out var info))
            {
                treeView.SelectedNode = null;
                return;
            }
            treeView.SelectedNode = info.Node;
        }

        public IEnumerator<FileInfo> GetEnumerator()
        {
            foreach (var info in mFileExtra)
                if (!info.Value.Info.IsDir)
                    yield return info.Value.Info;
        }

        public class FileInfo
        {
            public readonly string Path = "";
            public FileInfo() { }
            public FileInfo(string path) { Path = path; }
            public virtual bool IsDir { get { return false; } }
            public override string ToString() => Path;
            public string FileName => System.IO.Path.GetFileName(Path);
        }

        public class DirInfo : FileInfo
        {
            static readonly FileInfo[] sEmptyFiles = new FileInfo[0];
            static readonly DirInfo[] sEmptyDirs = new DirInfo[0];

            public DirInfo() { }
            public DirInfo(string path) : base(path) { }
            public override bool IsDir { get { return true; } }
            public FileInfo[] Files = sEmptyFiles;
            public DirInfo[] Directories = sEmptyDirs;
        }

        private void treeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (!mFileExtra.TryGetValue(e.Node.Name.ToLower(), out var extra))
                return;
            FileDoubleClicked?.Invoke(this, extra.Info);
        }

        private void treeView_Validating(object sender, CancelEventArgs e)
        {

        }

        private void treeView_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (mFileExtra.TryGetValue(e.Node.Name.ToLower(), out var extra)
                && extra.Info.IsDir)
            {
                MessageBox.Show(this, "Cannot rename a directory yet", App.Name);
                e.CancelEdit = true;
            }
        }

        private void treeView_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e.Label == null)
                return; // Canceled by user or initializing tree
            if (!mFileExtra.TryGetValue(e.Node.Name.ToLower(), out var extra))
                return;

            try
            {
                var newName = Path.Combine(Path.GetDirectoryName(e.Node.Name), e.Label);
                File.Move(e.Node.Name, newName);
                mFileExtra[newName.ToLower()] = extra;  // Move old node info
                FileMoved?.Invoke(this, extra.Info, new FileInfo(newName));

                // This is a work around because refreshing the files here
                // causes the tree to edit the node again
                timerRefreshTree.Enabled = true;
            }
            catch (Exception ex)
            {
                e.CancelEdit = true;
                MessageBox.Show(this, "Can't rename file: " + ex.Message, App.Name);
            }

        }

        private void timerRefreshTree_Tick(object sender, EventArgs e)
        {
            timerRefreshTree.Enabled = false;
            RefreshFiles();
        }

        private void treeView_MouseMove(object sender, MouseEventArgs e)
        {

        }

        static readonly WordSet sImageExtensions = new WordSet(".jpg .jpeg .bmp .png");
        private async void treeView_NodeMouseHover(object sender, TreeNodeMouseHoverEventArgs e)
        {
            if (e.Node == null || !mFileExtra.TryGetValue(e.Node.Name.ToLower(), out var extra))
            {
                if (mHoverForm != null)
                    mHoverForm.Visible = false;
                return;
            }
            if (extra.Info.IsDir || !sImageExtensions.Contains(Path.GetExtension(extra.Info.Path).ToLower()))
            {
                if (mHoverForm != null)
                    mHoverForm.Visible = false;
                return;
            }
            if (mHoverForm == null)
            {
                mHoverForm = new FormHoverMessage();
                mHoverForm.Message.Visible = false;
                mHoverForm.Picture.Visible = true;
                mHoverForm.Picture.Dock = DockStyle.Fill;
            }

            Bitmap bm = null;
            await Task.Run(() => 
            {
                try
                {
                    using (var im = Image.FromFile(extra.Info.Path))
                    {
                        var scale = Math.Min(1, 256.0 / Math.Max(im.Width, im.Height));
                        bm = new Bitmap((int)(im.Width*scale), (int)(im.Height*scale), PixelFormat.Format32bppArgb);
                        using (var gr = Graphics.FromImage(bm))
                            gr.DrawImage(im, 0, 0, bm.Width, bm.Height);
                    }
                }
                catch
                {
                }
            });
            if (bm == null)
            {
                if (mHoverForm != null)
                    mHoverForm.Visible = false;
                return;
            }

            var oldIm = mHoverForm.Picture.Image;
            mHoverForm.Location = new Point(MousePosition.X, MousePosition.Y + 25);
            mHoverForm.Visible = true;
            mHoverForm.BringToFront();
            mHoverForm.Picture.Image = bm;
            mHoverForm.Size = new Size(bm.Width, bm.Height);
            mHoverForm.Picture.SizeMode = PictureBoxSizeMode.CenterImage;
            if (oldIm != null)
                oldIm.Dispose();
        }

        private void treeView_MouseLeave(object sender, EventArgs e)
        {
            if (mHoverForm != null)
                mHoverForm.Visible = false;
        }
    }

}
