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
            this.mainTabControl = new Gosub.Zurfur.TablessTabControl();
            this.buttonClose = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // editorTabControl
            // 
            this.mainTabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainTabControl.ItemSize = new System.Drawing.Size(0, 24);
            this.mainTabControl.Location = new System.Drawing.Point(0, 0);
            this.mainTabControl.Name = "editorTabControl";
            this.mainTabControl.SelectedIndex = 0;
            this.mainTabControl.ShowTabs = true;
            this.mainTabControl.ShowTabsDesignMode = true;
            this.mainTabControl.Size = new System.Drawing.Size(617, 332);
            this.mainTabControl.TabIndex = 0;
            this.mainTabControl.Selected += new System.Windows.Forms.TabControlEventHandler(this.mainTabControl_Selected);
            this.mainTabControl.MouseLeave += new System.EventHandler(this.editorTabControl_MouseLeave);
            this.mainTabControl.MouseMove += new System.Windows.Forms.MouseEventHandler(this.editorTabControl_MouseMove);
            this.mainTabControl.MouseUp += new System.Windows.Forms.MouseEventHandler(this.editorTabControl_MouseUp);
            // 
            // buttonClose
            // 
            this.buttonClose.BackColor = System.Drawing.Color.White;
            this.buttonClose.Location = new System.Drawing.Point(82, 287);
            this.buttonClose.Name = "buttonClose";
            this.buttonClose.Size = new System.Drawing.Size(23, 23);
            this.buttonClose.TabIndex = 2;
            this.buttonClose.Text = "X";
            this.buttonClose.UseVisualStyleBackColor = false;
            this.buttonClose.Visible = false;
            // 
            // MultiViewEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.buttonClose);
            this.Controls.Add(this.mainTabControl);
            this.Name = "MultiViewEditor";
            this.Size = new System.Drawing.Size(617, 332);
            this.ResumeLayout(false);

        }

        #endregion

        private TablessTabControl mainTabControl;
        private System.Windows.Forms.Button buttonClose;
    }
}
