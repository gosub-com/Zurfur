namespace Gosub.Zurfur
{
	partial class FormHtml
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
            this.webBrowser1 = new System.Windows.Forms.WebBrowser();
            this.buttonCopyHtmlAsText = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // webBrowser1
            // 
            this.webBrowser1.Location = new System.Drawing.Point(12, 41);
            this.webBrowser1.MinimumSize = new System.Drawing.Size(20, 20);
            this.webBrowser1.Name = "webBrowser1";
            this.webBrowser1.Size = new System.Drawing.Size(886, 516);
            this.webBrowser1.TabIndex = 2;
            // 
            // buttonCopyHtmlAsText
            // 
            this.buttonCopyHtmlAsText.Location = new System.Drawing.Point(12, 12);
            this.buttonCopyHtmlAsText.Name = "buttonCopyHtmlAsText";
            this.buttonCopyHtmlAsText.Size = new System.Drawing.Size(145, 23);
            this.buttonCopyHtmlAsText.TabIndex = 3;
            this.buttonCopyHtmlAsText.Text = "Copy HTML as Text";
            this.buttonCopyHtmlAsText.UseVisualStyleBackColor = true;
            this.buttonCopyHtmlAsText.Click += new System.EventHandler(this.buttonCopyHtmlAsText_Click);
            // 
            // FormHtml
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(910, 583);
            this.Controls.Add(this.buttonCopyHtmlAsText);
            this.Controls.Add(this.webBrowser1);
            this.Name = "FormHtml";
            this.Text = "Text";
            this.Load += new System.EventHandler(this.FormHtml_Load);
            this.ResumeLayout(false);

		}

		#endregion
        private System.Windows.Forms.WebBrowser webBrowser1;
        private System.Windows.Forms.Button buttonCopyHtmlAsText;
    }
}