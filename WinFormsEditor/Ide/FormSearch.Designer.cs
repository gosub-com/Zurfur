namespace Zurfur.Ide
{
	partial class FormSearch
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
            this.textSearch = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.checkMatchWholeWord = new System.Windows.Forms.CheckBox();
            this.checkMatchCase = new System.Windows.Forms.CheckBox();
            this.buttonFindNext = new System.Windows.Forms.Button();
            this.textReplace = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.buttonReplaceNext = new System.Windows.Forms.Button();
            this.buttonReplaceAll = new System.Windows.Forms.Button();
            this.labelMatches = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // textSearch
            // 
            this.textSearch.Location = new System.Drawing.Point(8, 24);
            this.textSearch.Name = "textSearch";
            this.textSearch.Size = new System.Drawing.Size(344, 20);
            this.textSearch.TabIndex = 0;
            this.textSearch.TextChanged += new System.EventHandler(this.textSearch_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 8);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(59, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Search for:";
            // 
            // checkMatchWholeWord
            // 
            this.checkMatchWholeWord.AutoSize = true;
            this.checkMatchWholeWord.Location = new System.Drawing.Point(12, 130);
            this.checkMatchWholeWord.Name = "checkMatchWholeWord";
            this.checkMatchWholeWord.Size = new System.Drawing.Size(113, 17);
            this.checkMatchWholeWord.TabIndex = 5;
            this.checkMatchWholeWord.Text = "Match whole word";
            this.checkMatchWholeWord.UseVisualStyleBackColor = true;
            this.checkMatchWholeWord.CheckedChanged += new System.EventHandler(this.checkMatchWholeWord_CheckedChanged);
            // 
            // checkMatchCase
            // 
            this.checkMatchCase.AutoSize = true;
            this.checkMatchCase.Location = new System.Drawing.Point(12, 153);
            this.checkMatchCase.Name = "checkMatchCase";
            this.checkMatchCase.Size = new System.Drawing.Size(82, 17);
            this.checkMatchCase.TabIndex = 6;
            this.checkMatchCase.Text = "Match case";
            this.checkMatchCase.UseVisualStyleBackColor = true;
            this.checkMatchCase.CheckedChanged += new System.EventHandler(this.checkMatchCase_CheckedChanged);
            // 
            // buttonFindNext
            // 
            this.buttonFindNext.Location = new System.Drawing.Point(248, 50);
            this.buttonFindNext.Name = "buttonFindNext";
            this.buttonFindNext.Size = new System.Drawing.Size(104, 23);
            this.buttonFindNext.TabIndex = 1;
            this.buttonFindNext.Text = "Find Next";
            this.buttonFindNext.UseVisualStyleBackColor = true;
            this.buttonFindNext.Click += new System.EventHandler(this.buttonFindNext_Click);
            // 
            // textReplace
            // 
            this.textReplace.Location = new System.Drawing.Point(8, 79);
            this.textReplace.Name = "textReplace";
            this.textReplace.Size = new System.Drawing.Size(344, 20);
            this.textReplace.TabIndex = 2;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 63);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(72, 13);
            this.label2.TabIndex = 6;
            this.label2.Text = "Replace with:";
            // 
            // buttonReplaceNext
            // 
            this.buttonReplaceNext.Location = new System.Drawing.Point(248, 103);
            this.buttonReplaceNext.Name = "buttonReplaceNext";
            this.buttonReplaceNext.Size = new System.Drawing.Size(104, 23);
            this.buttonReplaceNext.TabIndex = 4;
            this.buttonReplaceNext.Text = "Repalce Next";
            this.buttonReplaceNext.UseVisualStyleBackColor = true;
            this.buttonReplaceNext.Click += new System.EventHandler(this.buttonReplaceNext_Click);
            // 
            // buttonReplaceAll
            // 
            this.buttonReplaceAll.Location = new System.Drawing.Point(138, 103);
            this.buttonReplaceAll.Name = "buttonReplaceAll";
            this.buttonReplaceAll.Size = new System.Drawing.Size(104, 23);
            this.buttonReplaceAll.TabIndex = 3;
            this.buttonReplaceAll.Text = "Replace All";
            this.buttonReplaceAll.UseVisualStyleBackColor = true;
            this.buttonReplaceAll.Click += new System.EventHandler(this.buttonReplaceAll_Click);
            // 
            // labelMatches
            // 
            this.labelMatches.Location = new System.Drawing.Point(200, 8);
            this.labelMatches.Name = "labelMatches";
            this.labelMatches.Size = new System.Drawing.Size(152, 13);
            this.labelMatches.TabIndex = 9;
            this.labelMatches.Text = "labelMatches";
            this.labelMatches.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // FormSearch
            // 
            this.AcceptButton = this.buttonFindNext;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(360, 182);
            this.Controls.Add(this.labelMatches);
            this.Controls.Add(this.buttonReplaceAll);
            this.Controls.Add(this.buttonReplaceNext);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.textReplace);
            this.Controls.Add(this.buttonFindNext);
            this.Controls.Add(this.checkMatchCase);
            this.Controls.Add(this.checkMatchWholeWord);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textSearch);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormSearch";
            this.Text = " Search";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormSearch_FormClosing);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormSearch_KeyDown);
            this.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.FormSearch_KeyPress);
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.CheckBox checkMatchWholeWord;
		private System.Windows.Forms.CheckBox checkMatchCase;
		private System.Windows.Forms.Button buttonFindNext;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button buttonReplaceNext;
        private System.Windows.Forms.Button buttonReplaceAll;
        private System.Windows.Forms.Label labelMatches;
        public System.Windows.Forms.TextBox textSearch;
        public System.Windows.Forms.TextBox textReplace;
    }
}