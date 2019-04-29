using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Gosub.Zurfur
{
    public partial class FormViewText : Form
    {
        public string []TextLines = new string[]{""};

        public FormViewText()
        {
            InitializeComponent();
        }

        private void FormViewText_Load(object sender, EventArgs e)
        {
        }

        private void FormViewText_Shown(object sender, EventArgs e)
        {
            editor1.Lexer = new Lexer(TextLines);
            editor1.ReadOnly = true;
        }

        private void editor1_BlockedByReadOnly(object sender, EventArgs e)
        {
            MessageBox.Show(this, "The code window is read-only");
        }
    }
}
