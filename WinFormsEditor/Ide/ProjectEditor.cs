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

namespace Zurfur.Ide;

public partial class ProjectEditor : UserControl, IEditor
{
    public ProjectEditor()
    {
        InitializeComponent();
    }

    bool mModified;

    public bool Modified => mModified;
    public string FilePath { get; set; } = "";
    public FileInfo FileInfo { get; set; }

    public event EventHandler ModifiedChanged;
    public event EventHandler FilePathChanged;

    public Control GetControl()
    {
        return this;
    }

    public void LoadFile(string fileName)
    {
        FilePath = fileName;
    }

    public void SaveFile(string fileName)
    {
        FilePath = fileName;
    }
}
