namespace Gosub.Zurfur
{
    partial class MultiViewEditor
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.buttonClose = new System.Windows.Forms.Button();
            this.editorTabControl = new Gosub.Zurfur.TablessTabControl();
            this.timerCheckMouseLeave = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            // 
            // buttonClose
            // 
            this.buttonClose.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(225)))), ((int)(((byte)(255)))));
            this.buttonClose.FlatAppearance.BorderSize = 0;
            this.buttonClose.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Yellow;
            this.buttonClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonClose.Location = new System.Drawing.Point(108, 247);
            this.buttonClose.Name = "buttonClose";
            this.buttonClose.Size = new System.Drawing.Size(20, 20);
            this.buttonClose.TabIndex = 2;
            this.buttonClose.Text = "X";
            this.buttonClose.UseVisualStyleBackColor = false;
            this.buttonClose.Visible = false;
            this.buttonClose.Click += new System.EventHandler(this.buttonClose_Click);
            // 
            // editorTabControl
            // 
            this.editorTabControl.ItemSize = new System.Drawing.Size(0, 24);
            this.editorTabControl.Location = new System.Drawing.Point(19, 31);
            this.editorTabControl.Name = "editorTabControl";
            this.editorTabControl.SelectedIndex = 0;
            this.editorTabControl.ShowTabs = true;
            this.editorTabControl.ShowTabsDesignMode = true;
            this.editorTabControl.Size = new System.Drawing.Size(482, 189);
            this.editorTabControl.TabIndex = 0;
            this.editorTabControl.Selected += new System.Windows.Forms.TabControlEventHandler(this.editorTabControl_Selected);
            this.editorTabControl.MouseMove += new System.Windows.Forms.MouseEventHandler(this.editorTabControl_MouseMove);
            // 
            // timerCheckMouseLeave
            // 
            this.timerCheckMouseLeave.Tick += new System.EventHandler(this.timerCheckMouseLeave_Tick);
            // 
            // MultiViewEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.buttonClose);
            this.Controls.Add(this.editorTabControl);
            this.Name = "MultiViewEditor";
            this.Size = new System.Drawing.Size(617, 332);
            this.ResumeLayout(false);

        }

        #endregion

        private TablessTabControl editorTabControl;
        private System.Windows.Forms.Button buttonClose;
        private System.Windows.Forms.Timer timerCheckMouseLeave;
    }
}
