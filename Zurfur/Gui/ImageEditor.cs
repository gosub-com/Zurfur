using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.IO;

namespace Gosub.Zurfur
{

    /// <summary>
    /// Class to display images (and eventually edit them too)
    /// </summary>
    public partial class ImageEditor : UserControl, IEditor
    {
        string mFilePath;
        bool mModified;
        Bitmap mBitmap;

        public event EventHandler ModifiedChanged;
        public event EventHandler FilePathChanged;

        public ImageEditor()
        {
            InitializeComponent();
        }


        /// <summary>
        /// File info from when file was last loaded or saved (or null when not loaded)
        /// </summary>
        public FileInfo FileInfo { get; set; }

        public string FilePath
        {
            get { return mFilePath; }
            set
            {
                if (mFilePath == value)
                    return;
                mFilePath = value;
                FilePathChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Set this to false when the file is saved
        /// </summary>
        public bool Modified
        {
            get { return mModified; }
            set
            {
                if (value == mModified)
                    return;
                mModified = value;
                ModifiedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Control GetControl() { return this; }

        public void LoadFile(string filePath)
        {
            pictureBox1.Image = null;
            if (mBitmap != null)
                mBitmap.Dispose();
            mBitmap = null;

            // Make copy, so we don't hold it open and it can be renamed
            Image im = Image.FromFile(filePath);
            mBitmap = new Bitmap(im.Width, im.Height, PixelFormat.Format32bppArgb);
            using (var gr = Graphics.FromImage(mBitmap))
                gr.DrawImage(im, new Point(0, 0));
            im.Dispose();

            pictureBox1.Image = mBitmap;
            FileInfo = new FileInfo(filePath);
            FileInfo.Refresh(); // This seems to be needed for some reason
            Modified = false;
            FilePath = filePath;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            pictureBox1.SizeMode = mBitmap.Width < pictureBox1.Width && mBitmap.Height < pictureBox1.Height
                                        ? PictureBoxSizeMode.CenterImage : PictureBoxSizeMode.Zoom;
            base.OnSizeChanged(e);
        }

        public void SaveFile(string filePath)
        {
            throw new NotImplementedException("SaveFile not implemented for ImageEditor");
        }

    }
}
