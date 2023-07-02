using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Zurfur.Ide
{
    public partial class FormHoverMessage : Form
    {
        public FormHoverMessage()
        {
            InitializeComponent();
        }

        protected override bool ShowWithoutActivation => true;

        private void FormMessage_Load(object sender, EventArgs e)
        {
        }

    }
}
