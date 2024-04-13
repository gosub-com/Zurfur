using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace Zurfur.Ide;

/// <summary>
/// Editor controls must implement this interface.
/// For the time being, the editor does not keep the file open.
/// Instead, the FileInfo is used to see if the file has changed outside the editor.
/// </summary>
public interface IEditor
{
    Control GetControl();
    event EventHandler ModifiedChanged;
    event EventHandler FilePathChanged;
    bool Modified { get; }
    string FilePath { get; set;  }
    void LoadFile(string fileName);
    void SaveFile(string fileName);
    FileInfo FileInfo { get; }
}
