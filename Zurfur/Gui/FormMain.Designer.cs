namespace Gosub.Zurfur
{
	partial class FormMain
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

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            Gosub.Zurfur.Lexer lexer1 = new Gosub.Zurfur.Lexer();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.menuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileOpen = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileSave = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditFind = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditFindNext = new System.Windows.Forms.ToolStripMenuItem();
            this.viewRTFToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelp = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelpAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelpLicense = new System.Windows.Forms.ToolStripMenuItem();
            this.editor1 = new Gosub.Zurfur.Editor();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // timer1
            // 
            this.timer1.Enabled = true;
            this.timer1.Interval = 20;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFile,
            this.menuEdit,
            this.menuHelp});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(942, 24);
            this.menuStrip1.TabIndex = 16;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // menuFile
            // 
            this.menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFileOpen,
            this.menuFileSave});
            this.menuFile.Name = "menuFile";
            this.menuFile.Size = new System.Drawing.Size(37, 20);
            this.menuFile.Text = "&File";
            // 
            // menuFileOpen
            // 
            this.menuFileOpen.Name = "menuFileOpen";
            this.menuFileOpen.Size = new System.Drawing.Size(103, 22);
            this.menuFileOpen.Text = "Open";
            this.menuFileOpen.Click += new System.EventHandler(this.menuFileOpen_Click);
            // 
            // menuFileSave
            // 
            this.menuFileSave.Name = "menuFileSave";
            this.menuFileSave.Size = new System.Drawing.Size(103, 22);
            this.menuFileSave.Text = "Save";
            this.menuFileSave.Click += new System.EventHandler(this.menuFileSave_Click);
            // 
            // menuEdit
            // 
            this.menuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuEditFind,
            this.menuEditFindNext,
            this.viewRTFToolStripMenuItem});
            this.menuEdit.Name = "menuEdit";
            this.menuEdit.Size = new System.Drawing.Size(39, 20);
            this.menuEdit.Text = "&Edit";
            // 
            // menuEditFind
            // 
            this.menuEditFind.Name = "menuEditFind";
            this.menuEditFind.Size = new System.Drawing.Size(156, 22);
            this.menuEditFind.Text = "Find... (CTRL-F)";
            this.menuEditFind.Click += new System.EventHandler(this.menuEditFind_Click);
            // 
            // menuEditFindNext
            // 
            this.menuEditFindNext.Name = "menuEditFindNext";
            this.menuEditFindNext.Size = new System.Drawing.Size(156, 22);
            this.menuEditFindNext.Text = "Find Next... (F3)";
            this.menuEditFindNext.Click += new System.EventHandler(this.menuEditFindNext_Click);
            // 
            // viewRTFToolStripMenuItem
            // 
            this.viewRTFToolStripMenuItem.Name = "viewRTFToolStripMenuItem";
            this.viewRTFToolStripMenuItem.Size = new System.Drawing.Size(156, 22);
            this.viewRTFToolStripMenuItem.Text = "View HTML...";
            this.viewRTFToolStripMenuItem.Click += new System.EventHandler(this.viewRTFToolStripMenuItem_Click);
            // 
            // menuHelp
            // 
            this.menuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuHelpAbout,
            this.menuHelpLicense});
            this.menuHelp.Name = "menuHelp";
            this.menuHelp.Size = new System.Drawing.Size(44, 20);
            this.menuHelp.Text = "&Help";
            // 
            // menuHelpAbout
            // 
            this.menuHelpAbout.Name = "menuHelpAbout";
            this.menuHelpAbout.Size = new System.Drawing.Size(152, 22);
            this.menuHelpAbout.Text = "About...";
            this.menuHelpAbout.Click += new System.EventHandler(this.menuHelpAbout_Click);
            // 
            // menuHelpLicense
            // 
            this.menuHelpLicense.Name = "menuHelpLicense";
            this.menuHelpLicense.Size = new System.Drawing.Size(152, 22);
            this.menuHelpLicense.Text = "License...";
            this.menuHelpLicense.Click += new System.EventHandler(this.menuHelpLicense_Click);
            // 
            // editor1
            // 
            this.editor1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.editor1.BackColor = System.Drawing.SystemColors.Window;
            this.editor1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.editor1.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.editor1.Font = new System.Drawing.Font("Courier New", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.editor1.Lexer = lexer1;
            this.editor1.Location = new System.Drawing.Point(8, 24);
            this.editor1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.editor1.Name = "editor1";
            this.editor1.OverwriteMode = false;
            this.editor1.ReadOnly = false;
            this.editor1.Size = new System.Drawing.Size(928, 600);
            this.editor1.TabIndex = 12;
            this.editor1.TabSize = 4;
            this.editor1.TokenColorOverrides = null;
            this.editor1.MouseTokenChanged += new Gosub.Zurfur.Editor.EditorTokenDelegate(this.editor1_MouseTokenChanged);
            this.editor1.BlockedByReadOnly += new System.EventHandler(this.editor1_BlockedByReadOnly);
            this.editor1.TextChanged2 += new System.EventHandler(this.editor1_TextChanged2);
            this.editor1.Load += new System.EventHandler(this.editor1_Load);
            this.editor1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.editor1_MouseDown);
            this.editor1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.editor1_MouseMove);
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(942, 631);
            this.Controls.Add(this.editor1);
            this.Controls.Add(this.menuStrip1);
            this.KeyPreview = true;
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "FormMain";
            this.Text = "Zurfur";
            this.Load += new System.EventHandler(this.FormBit_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormBit_KeyDown);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		private Editor editor1;
		private System.Windows.Forms.Timer timer1;
		private System.Windows.Forms.MenuStrip menuStrip1;
		private System.Windows.Forms.ToolStripMenuItem menuFile;
		private System.Windows.Forms.ToolStripMenuItem menuFileOpen;
		private System.Windows.Forms.ToolStripMenuItem menuFileSave;
		private System.Windows.Forms.ToolStripMenuItem menuHelp;
		private System.Windows.Forms.ToolStripMenuItem menuHelpAbout;
		private System.Windows.Forms.ToolStripMenuItem menuHelpLicense;
		private System.Windows.Forms.ToolStripMenuItem menuEdit;
		private System.Windows.Forms.ToolStripMenuItem menuEditFind;
		private System.Windows.Forms.ToolStripMenuItem menuEditFindNext;
		private System.Windows.Forms.ToolStripMenuItem viewRTFToolStripMenuItem;
	}
}

