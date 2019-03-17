namespace Gosub.Zurfur
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
			this.checkMatchWholeWord.Location = new System.Drawing.Point(8, 48);
			this.checkMatchWholeWord.Name = "checkMatchWholeWord";
			this.checkMatchWholeWord.Size = new System.Drawing.Size(113, 17);
			this.checkMatchWholeWord.TabIndex = 2;
			this.checkMatchWholeWord.Text = "Match whole word";
			this.checkMatchWholeWord.UseVisualStyleBackColor = true;
			// 
			// checkMatchCase
			// 
			this.checkMatchCase.AutoSize = true;
			this.checkMatchCase.Location = new System.Drawing.Point(8, 64);
			this.checkMatchCase.Name = "checkMatchCase";
			this.checkMatchCase.Size = new System.Drawing.Size(82, 17);
			this.checkMatchCase.TabIndex = 3;
			this.checkMatchCase.Text = "Match case";
			this.checkMatchCase.UseVisualStyleBackColor = true;
			// 
			// buttonFindNext
			// 
			this.buttonFindNext.Location = new System.Drawing.Point(272, 56);
			this.buttonFindNext.Name = "buttonFindNext";
			this.buttonFindNext.Size = new System.Drawing.Size(80, 23);
			this.buttonFindNext.TabIndex = 4;
			this.buttonFindNext.Text = "Find Next";
			this.buttonFindNext.UseVisualStyleBackColor = true;
			this.buttonFindNext.Click += new System.EventHandler(this.buttonFindNext_Click);
			// 
			// FormSearch
			// 
			this.AcceptButton = this.buttonFindNext;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(360, 86);
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
			this.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.FormSearch_KeyPress);
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormSearch_FormClosing);
			this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormSearch_KeyDown);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox textSearch;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.CheckBox checkMatchWholeWord;
		private System.Windows.Forms.CheckBox checkMatchCase;
		private System.Windows.Forms.Button buttonFindNext;
	}
}