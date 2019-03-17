namespace Gosub.Zurfur
{
	partial class FormViewText
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
			this.editor1 = new Gosub.Zurfur.Editor();
			this.SuspendLayout();
			// 
			// editor1
			// 
			this.editor1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.editor1.BackColor = System.Drawing.SystemColors.Window;
			this.editor1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
			this.editor1.Cursor = System.Windows.Forms.Cursors.IBeam;
			this.editor1.Font = new System.Drawing.Font("Courier New", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.editor1.Location = new System.Drawing.Point(8, 8);
			this.editor1.Name = "editor1";
			this.editor1.ReadOnly = false;
			this.editor1.Size = new System.Drawing.Size(768, 448);
			this.editor1.TokenColorOverrides = null;
			this.editor1.TabIndex = 1;
			this.editor1.TabSize = 4;
			this.editor1.BlockedByReadOnly += new System.EventHandler(this.editor1_BlockedByReadOnly);
			// 
			// FormViewText
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(781, 464);
			this.Controls.Add(this.editor1);
			this.Name = "FormViewText";
			this.Text = "FormViewText";
			this.Load += new System.EventHandler(this.FormViewText_Load);
			this.Shown += new System.EventHandler(this.FormViewText_Shown);
			this.ResumeLayout(false);

		}

		#endregion

		private Editor editor1;

	}
}