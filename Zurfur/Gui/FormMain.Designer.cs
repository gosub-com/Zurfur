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
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.mainMenu = new System.Windows.Forms.MenuStrip();
            this.menuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileOpen = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileSave = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileSaveAll = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileSaveAs = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditFind = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditFindNext = new System.Windows.Forms.ToolStripMenuItem();
            this.viewRTFToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelp = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelpAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelpLicense = new System.Windows.Forms.ToolStripMenuItem();
            this.mvEditors = new Gosub.Zurfur.MultiViewEditor();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.mainMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // timer1
            // 
            this.timer1.Enabled = true;
            this.timer1.Interval = 20;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // mainMenu
            // 
            this.mainMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFile,
            this.menuEdit,
            this.menuHelp});
            this.mainMenu.Location = new System.Drawing.Point(0, 0);
            this.mainMenu.Name = "mainMenu";
            this.mainMenu.Size = new System.Drawing.Size(942, 24);
            this.mainMenu.TabIndex = 16;
            this.mainMenu.Text = "menuStrip1";
            // 
            // menuFile
            // 
            this.menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFileOpen,
            this.menuFileSave,
            this.menuFileSaveAll,
            this.menuFileSaveAs});
            this.menuFile.Name = "menuFile";
            this.menuFile.Size = new System.Drawing.Size(37, 20);
            this.menuFile.Text = "&File";
            this.menuFile.DropDownOpening += new System.EventHandler(this.menuFile_DropDownOpening);
            // 
            // menuFileOpen
            // 
            this.menuFileOpen.Name = "menuFileOpen";
            this.menuFileOpen.Size = new System.Drawing.Size(152, 22);
            this.menuFileOpen.Text = "Open";
            this.menuFileOpen.Click += new System.EventHandler(this.menuFileOpen_Click);
            // 
            // menuFileSave
            // 
            this.menuFileSave.Name = "menuFileSave";
            this.menuFileSave.Size = new System.Drawing.Size(152, 22);
            this.menuFileSave.Text = "Save";
            this.menuFileSave.Click += new System.EventHandler(this.menuFileSave_Click);
            // 
            // menuFileSaveAll
            // 
            this.menuFileSaveAll.Name = "menuFileSaveAll";
            this.menuFileSaveAll.Size = new System.Drawing.Size(152, 22);
            this.menuFileSaveAll.Text = "Save All";
            this.menuFileSaveAll.Click += new System.EventHandler(this.menuFileSaveAll_Click);
            // 
            // menuFileSaveAs
            // 
            this.menuFileSaveAs.Name = "menuFileSaveAs";
            this.menuFileSaveAs.Size = new System.Drawing.Size(152, 22);
            this.menuFileSaveAs.Text = "Save As...";
            this.menuFileSaveAs.Click += new System.EventHandler(this.menuFileSaveAs_Click);
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
            this.menuEdit.DropDownOpening += new System.EventHandler(this.menuEdit_DropDownOpening);
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
            this.menuHelpAbout.Size = new System.Drawing.Size(122, 22);
            this.menuHelpAbout.Text = "About...";
            this.menuHelpAbout.Click += new System.EventHandler(this.menuHelpAbout_Click);
            // 
            // menuHelpLicense
            // 
            this.menuHelpLicense.Name = "menuHelpLicense";
            this.menuHelpLicense.Size = new System.Drawing.Size(122, 22);
            this.menuHelpLicense.Text = "License...";
            this.menuHelpLicense.Click += new System.EventHandler(this.menuHelpLicense_Click);
            // 
            // mvEditors
            // 
            this.mvEditors.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.mvEditors.EditorViewActive = null;
            this.mvEditors.Location = new System.Drawing.Point(12, 27);
            this.mvEditors.Name = "mvEditors";
            this.mvEditors.Size = new System.Drawing.Size(918, 592);
            this.mvEditors.TabIndex = 19;
            this.mvEditors.EditorAdded += new Gosub.Zurfur.MultiViewEditor.EditorDelegate(this.mvEditors_EditorAdded);
            this.mvEditors.EditorRemoved += new Gosub.Zurfur.MultiViewEditor.EditorDelegate(this.mvEditors_EditorRemoved);
            this.mvEditors.EditorViewActiveChanged += new Gosub.Zurfur.MultiViewEditor.EditorDelegate(this.mvEditors_EditorViewChanged);
            this.mvEditors.EditorCanClose += new Gosub.Zurfur.MultiViewEditor.EditorCanCloseDelegate(this.mvEditors_EditorCanClose);
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.FileName = "openFileDialog1";
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(942, 631);
            this.Controls.Add(this.mvEditors);
            this.Controls.Add(this.mainMenu);
            this.KeyPreview = true;
            this.MainMenuStrip = this.mainMenu;
            this.Name = "FormMain";
            this.Text = "Zurfur";
            this.Activated += new System.EventHandler(this.FormMain_Activated);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormMain_FormClosing);
            this.Load += new System.EventHandler(this.FormMain_Load);
            this.Shown += new System.EventHandler(this.FormMain_Shown);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormMain_KeyDown);
            this.mainMenu.ResumeLayout(false);
            this.mainMenu.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		private System.Windows.Forms.Timer timer1;
		private System.Windows.Forms.MenuStrip mainMenu;
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
        private MultiViewEditor mvEditors;
        private System.Windows.Forms.ToolStripMenuItem menuFileSaveAll;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.ToolStripMenuItem menuFileSaveAs;
    }
}

