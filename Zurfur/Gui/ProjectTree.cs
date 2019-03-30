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

namespace Gosub.Zurfur
{
    public partial class ProjectTree : UserControl
    {
        string mRootDir = "";
        DirInfo mDirInfo = new DirInfo();
        Dictionary<string, FileInfoExtra> mFileInfo = new Dictionary<string, FileInfoExtra>();

        public delegate void FileInfoDelegate(object sender, FileInfo file);

        public event FileInfoDelegate FileDoubleClicked;

        
        class FileInfoExtra
        {
            public FileInfo Info;
            public TreeNode Tree;

            public FileInfoExtra(FileInfo info, TreeNode tree)
            {
                Info = info;
                Tree = tree;
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
                treeView.Nodes.Clear();
                mFileInfo.Clear();
                mDirInfo = new DirInfo();
                if (mRootDir != "")
                {
                    mDirInfo = LoadFiles(mRootDir);
                    AddFileNodes(mDirInfo, treeView.Nodes);
                }
            }
        }

        public void RefreshFiles()
        {
            var root = mRootDir;
            RootDir = "";
            RootDir = root;
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
        /// Add the nodes and build mFileInfo with lower cased paths.
        /// Does not add the root dir.
        /// </summary>
        void AddFileNodes(DirInfo dir, TreeNodeCollection nodes)
        {
            foreach (var subdir in dir.Directories)
            {
                var node = nodes.Add(subdir.Path, subdir.FileName);
                node.ToolTipText = subdir.Path;
                mFileInfo[subdir.Path.ToLower()] = new FileInfoExtra(dir, node);
                AddFileNodes(subdir, node.Nodes);
            }
            foreach (var file in dir.Files)
            {
                var node = nodes.Add(file.Path, file.FileName);
                node.ToolTipText = file.Path;
                mFileInfo[file.Path.ToLower()] = new FileInfoExtra(file, node);
            }
        }

        public void OpenAndSelect(string fileName)
        {
            if (!mFileInfo.TryGetValue(fileName.ToLower(), out var info))
            {
                treeView.SelectedNode = null;
                return;
            }
            var node = info.Tree;
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
            if (!mFileInfo.TryGetValue(fileName.ToLower(), out var info))
            {
                treeView.SelectedNode = null;
                return;
            }
            treeView.SelectedNode = info.Tree;
        }

        public IEnumerator<FileInfo> GetEnumerator()
        {
            foreach (var info in mFileInfo)
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
            if (!mFileInfo.TryGetValue(e.Node.Name.ToLower(), out var info))
                return;
            FileDoubleClicked?.Invoke(this, info.Info);
        }
    }

}
