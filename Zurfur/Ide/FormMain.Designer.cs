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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
            this.mainMenu = new System.Windows.Forms.MenuStrip();
            this.menuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileNewProject = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileNewFile = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripSeparator();
            this.menuFileOpenProject = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileOpenFile = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            this.menuFileSave = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileSaveAll = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileSaveAs = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditFind = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditFindNext = new System.Windows.Forms.ToolStripMenuItem();
            this.viewRTFToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuDebug = new System.Windows.Forms.ToolStripMenuItem();
            this.menuDebugViewOutput = new System.Windows.Forms.ToolStripMenuItem();
            this.menuDebugRun = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelp = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelpAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelpLicense = new System.Windows.Forms.ToolStripMenuItem();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.projectTree = new Gosub.Zurfur.ProjectTree();
            this.mvEditors = new Gosub.Zurfur.MultiViewEditor();
            this.buttonMax = new System.Windows.Forms.Button();
            this.buttonMin = new System.Windows.Forms.Button();
            this.buttonClose = new System.Windows.Forms.Button();
            this.pictureMenuIcon = new System.Windows.Forms.PictureBox();
            this.labelStatus = new System.Windows.Forms.Label();
            this.mainMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureMenuIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // mainMenu
            // 
            this.mainMenu.Dock = System.Windows.Forms.DockStyle.None;
            this.mainMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFile,
            this.menuEdit,
            this.menuDebug,
            this.menuHelp});
            this.mainMenu.Location = new System.Drawing.Point(22, 1);
            this.mainMenu.Name = "mainMenu";
            this.mainMenu.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.mainMenu.Size = new System.Drawing.Size(182, 24);
            this.mainMenu.TabIndex = 16;
            this.mainMenu.Text = "menuStrip1";
            // 
            // menuFile
            // 
            this.menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFileNewProject,
            this.menuFileNewFile,
            this.toolStripMenuItem2,
            this.menuFileOpenProject,
            this.menuFileOpenFile,
            this.toolStripMenuItem1,
            this.menuFileSave,
            this.menuFileSaveAll,
            this.menuFileSaveAs});
            this.menuFile.Name = "menuFile";
            this.menuFile.Size = new System.Drawing.Size(37, 20);
            this.menuFile.Text = "&File";
            this.menuFile.DropDownOpening += new System.EventHandler(this.menuFile_DropDownOpening);
            // 
            // menuFileNewProject
            // 
            this.menuFileNewProject.Name = "menuFileNewProject";
            this.menuFileNewProject.Size = new System.Drawing.Size(152, 22);
            this.menuFileNewProject.Text = "New Project...";
            this.menuFileNewProject.Click += new System.EventHandler(this.menuFileNewProject_Click);
            // 
            // menuFileNewFile
            // 
            this.menuFileNewFile.Name = "menuFileNewFile";
            this.menuFileNewFile.Size = new System.Drawing.Size(152, 22);
            this.menuFileNewFile.Text = "New File";
            this.menuFileNewFile.Click += new System.EventHandler(this.menuFileNewFile_Click);
            // 
            // toolStripMenuItem2
            // 
            this.toolStripMenuItem2.Name = "toolStripMenuItem2";
            this.toolStripMenuItem2.Size = new System.Drawing.Size(149, 6);
            // 
            // menuFileOpenProject
            // 
            this.menuFileOpenProject.Name = "menuFileOpenProject";
            this.menuFileOpenProject.Size = new System.Drawing.Size(152, 22);
            this.menuFileOpenProject.Text = "Open Project...";
            this.menuFileOpenProject.Click += new System.EventHandler(this.menuFileOpenProject_Click);
            // 
            // menuFileOpenFile
            // 
            this.menuFileOpenFile.Name = "menuFileOpenFile";
            this.menuFileOpenFile.Size = new System.Drawing.Size(152, 22);
            this.menuFileOpenFile.Text = "Open File...";
            this.menuFileOpenFile.Click += new System.EventHandler(this.menuFileOpenFile_Click);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new System.Drawing.Size(149, 6);
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
            this.menuEditFind.Size = new System.Drawing.Size(157, 22);
            this.menuEditFind.Text = "Find... (CTRL-F)";
            this.menuEditFind.Click += new System.EventHandler(this.menuEditFind_Click);
            // 
            // menuEditFindNext
            // 
            this.menuEditFindNext.Name = "menuEditFindNext";
            this.menuEditFindNext.Size = new System.Drawing.Size(157, 22);
            this.menuEditFindNext.Text = "Find Next... (F3)";
            this.menuEditFindNext.Click += new System.EventHandler(this.menuEditFindNext_Click);
            // 
            // viewRTFToolStripMenuItem
            // 
            this.viewRTFToolStripMenuItem.Name = "viewRTFToolStripMenuItem";
            this.viewRTFToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.viewRTFToolStripMenuItem.Text = "View HTML...";
            this.viewRTFToolStripMenuItem.Click += new System.EventHandler(this.viewRTFToolStripMenuItem_Click);
            // 
            // menuDebug
            // 
            this.menuDebug.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuDebugViewOutput,
            this.menuDebugRun});
            this.menuDebug.Name = "menuDebug";
            this.menuDebug.Size = new System.Drawing.Size(54, 20);
            this.menuDebug.Text = "Debug";
            this.menuDebug.DropDownOpening += new System.EventHandler(this.menuDebug_DropDownOpening);
            // 
            // menuDebugViewOutput
            // 
            this.menuDebugViewOutput.Name = "menuDebugViewOutput";
            this.menuDebugViewOutput.Size = new System.Drawing.Size(163, 22);
            this.menuDebugViewOutput.Text = "View Output (F4)";
            this.menuDebugViewOutput.Click += new System.EventHandler(this.menuDebugViewOutput_Click);
            // 
            // menuDebugRun
            // 
            this.menuDebugRun.Name = "menuDebugRun";
            this.menuDebugRun.Size = new System.Drawing.Size(163, 22);
            this.menuDebugRun.Text = "Run (F5)";
            this.menuDebugRun.Click += new System.EventHandler(this.menuDebugRun_Click);
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
            // openFileDialog1
            // 
            this.openFileDialog1.FileName = "openFileDialog1";
            // 
            // splitContainer1
            // 
            this.splitContainer1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.splitContainer1.Location = new System.Drawing.Point(0, 24);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.projectTree);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.mvEditors);
            this.splitContainer1.Size = new System.Drawing.Size(1323, 615);
            this.splitContainer1.SplitterDistance = 165;
            this.splitContainer1.TabIndex = 20;
            // 
            // projectTree
            // 
            this.projectTree.Dock = System.Windows.Forms.DockStyle.Fill;
            this.projectTree.Location = new System.Drawing.Point(0, 0);
            this.projectTree.Name = "projectTree";
            this.projectTree.RootDir = "";
            this.projectTree.Size = new System.Drawing.Size(165, 615);
            this.projectTree.TabIndex = 20;
            this.projectTree.FileDoubleClicked += new Gosub.Zurfur.ProjectTree.FileInfoDelegate(this.projectTree_FileDoubleClicked);
            this.projectTree.FileMoved += new Gosub.Zurfur.ProjectTree.FileMovedDelegate(this.projectTree_FileMoved);
            // 
            // mvEditors
            // 
            this.mvEditors.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mvEditors.EditorViewActive = null;
            this.mvEditors.Location = new System.Drawing.Point(0, 0);
            this.mvEditors.Name = "mvEditors";
            this.mvEditors.Size = new System.Drawing.Size(1154, 615);
            this.mvEditors.TabIndex = 19;
            this.mvEditors.EditorAdded += new Gosub.Zurfur.MultiViewEditor.EditorDelegate(this.mvEditors_EditorAdded);
            this.mvEditors.EditorRemoved += new Gosub.Zurfur.MultiViewEditor.EditorDelegate(this.mvEditors_EditorRemoved);
            this.mvEditors.EditorActiveViewChanged += new Gosub.Zurfur.MultiViewEditor.EditorDelegate(this.mvEditors_EditorActiveViewChanged);
            this.mvEditors.EditorCanClose += new Gosub.Zurfur.MultiViewEditor.EditorCanCloseDelegate(this.mvEditors_EditorCanClose);
            // 
            // buttonMax
            // 
            this.buttonMax.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonMax.Font = new System.Drawing.Font("Wingdings", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(2)));
            this.buttonMax.Location = new System.Drawing.Point(1254, 2);
            this.buttonMax.Name = "buttonMax";
            this.buttonMax.Size = new System.Drawing.Size(32, 23);
            this.buttonMax.TabIndex = 21;
            this.buttonMax.TabStop = false;
            this.buttonMax.Text = "o";
            this.buttonMax.UseVisualStyleBackColor = true;
            this.buttonMax.Click += new System.EventHandler(this.buttonMax_Click);
            // 
            // buttonMin
            // 
            this.buttonMin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonMin.Font = new System.Drawing.Font("Arial Narrow", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.buttonMin.Location = new System.Drawing.Point(1220, 2);
            this.buttonMin.Name = "buttonMin";
            this.buttonMin.Size = new System.Drawing.Size(32, 23);
            this.buttonMin.TabIndex = 22;
            this.buttonMin.TabStop = false;
            this.buttonMin.Text = "─";
            this.buttonMin.UseVisualStyleBackColor = true;
            this.buttonMin.Click += new System.EventHandler(this.buttonMin_Click);
            // 
            // buttonClose
            // 
            this.buttonClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonClose.Location = new System.Drawing.Point(1288, 2);
            this.buttonClose.Name = "buttonClose";
            this.buttonClose.Size = new System.Drawing.Size(32, 23);
            this.buttonClose.TabIndex = 24;
            this.buttonClose.TabStop = false;
            this.buttonClose.Text = "X";
            this.buttonClose.UseVisualStyleBackColor = true;
            this.buttonClose.Click += new System.EventHandler(this.buttonClose_Click);
            // 
            // pictureMenuIcon
            // 
            this.pictureMenuIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureMenuIcon.Image = ((System.Drawing.Image)(resources.GetObject("pictureMenuIcon.Image")));
            this.pictureMenuIcon.Location = new System.Drawing.Point(2, 0);
            this.pictureMenuIcon.Name = "pictureMenuIcon";
            this.pictureMenuIcon.Size = new System.Drawing.Size(24, 24);
            this.pictureMenuIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureMenuIcon.TabIndex = 25;
            this.pictureMenuIcon.TabStop = false;
            this.pictureMenuIcon.Click += new System.EventHandler(this.pictureMenuIcon_Click);
            // 
            // labelStatus
            // 
            this.labelStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelStatus.AutoSize = true;
            this.labelStatus.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labelStatus.Location = new System.Drawing.Point(-1, 642);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Size = new System.Drawing.Size(51, 20);
            this.labelStatus.TabIndex = 26;
            this.labelStatus.Text = "label1";
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1323, 664);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.pictureMenuIcon);
            this.Controls.Add(this.buttonClose);
            this.Controls.Add(this.buttonMin);
            this.Controls.Add(this.buttonMax);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.mainMenu);
            this.Cursor = System.Windows.Forms.Cursors.Default;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MainMenuStrip = this.mainMenu;
            this.Name = "FormMain";
            this.Text = "Zurfur";
            this.Activated += new System.EventHandler(this.FormMain_Activated);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormMain_FormClosing);
            this.Load += new System.EventHandler(this.FormMain_Load);
            this.Shown += new System.EventHandler(this.FormMain_Shown);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormMain_KeyDown);
            this.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.FormMain_MouseDoubleClick);
            this.MouseCaptureChanged += new System.EventHandler(this.FormMain_MouseCaptureChanged);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.FormMain_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.FormMain_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.FormMain_MouseUp);
            this.mainMenu.ResumeLayout(false);
            this.mainMenu.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureMenuIcon)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		private System.Windows.Forms.MenuStrip mainMenu;
		private System.Windows.Forms.ToolStripMenuItem menuFile;
		private System.Windows.Forms.ToolStripMenuItem menuFileOpenFile;
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
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.ToolStripMenuItem menuFileOpenProject;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem menuFileNewFile;
        private System.Windows.Forms.ToolStripMenuItem menuFileNewProject;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem2;
        private ProjectTree projectTree;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.ToolStripMenuItem menuDebug;
        private System.Windows.Forms.ToolStripMenuItem menuDebugRun;
        private System.Windows.Forms.Button buttonMax;
        private System.Windows.Forms.Button buttonMin;
        private System.Windows.Forms.Button buttonClose;
        private System.Windows.Forms.PictureBox pictureMenuIcon;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.ToolStripMenuItem menuDebugViewOutput;
    }
}

