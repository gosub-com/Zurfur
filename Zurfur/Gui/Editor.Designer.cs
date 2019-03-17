namespace Gosub.Zurfur
{
	partial class Editor
	{
		/// <summary> 
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            this.hScrollBar = new System.Windows.Forms.HScrollBar();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.vScrollBar = new System.Windows.Forms.VScrollBar();
            this.vMarksLeft = new Gosub.Zurfur.VerticalMarks();
            this.SuspendLayout();
            // 
            // hScrollBar
            // 
            this.hScrollBar.Cursor = System.Windows.Forms.Cursors.Default;
            this.hScrollBar.Location = new System.Drawing.Point(0, 40);
            this.hScrollBar.Name = "hScrollBar";
            this.hScrollBar.Size = new System.Drawing.Size(106, 17);
            this.hScrollBar.TabIndex = 0;
            this.hScrollBar.Visible = false;
            this.hScrollBar.ValueChanged += new System.EventHandler(this.hScrollBar_ValueChanged);
            // 
            // timer1
            // 
            this.timer1.Enabled = true;
            this.timer1.Interval = 20;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // vScrollBar
            // 
            this.vScrollBar.Cursor = System.Windows.Forms.Cursors.Default;
            this.vScrollBar.LargeChange = 0;
            this.vScrollBar.Location = new System.Drawing.Point(174, 26);
            this.vScrollBar.Maximum = 0;
            this.vScrollBar.Name = "vScrollBar";
            this.vScrollBar.Size = new System.Drawing.Size(18, 144);
            this.vScrollBar.SmallChange = 0;
            this.vScrollBar.TabIndex = 1;
            this.vScrollBar.ValueChanged += new System.EventHandler(this.vScrollBar_ValueChanged);
            // 
            // verticalMarksLeft
            // 
            this.vMarksLeft.ArrowHight = 0;
            this.vMarksLeft.CursorMark = 0;
            this.vMarksLeft.Location = new System.Drawing.Point(139, 40);
            this.vMarksLeft.Maximum = 0;
            this.vMarksLeft.Name = "verticalMarksLeft";
            this.vMarksLeft.Size = new System.Drawing.Size(5, 149);
            this.vMarksLeft.TabIndex = 2;
            this.vMarksLeft.Text = "verticalMarks1";
            // 
            // Editor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.vMarksLeft);
            this.Controls.Add(this.vScrollBar);
            this.Controls.Add(this.hScrollBar);
            this.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.DoubleBuffered = true;
            this.Font = new System.Drawing.Font("Courier New", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "Editor";
            this.Size = new System.Drawing.Size(212, 211);
            this.Load += new System.EventHandler(this.Editor_Load);
            this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.HScrollBar hScrollBar;
		private System.Windows.Forms.VScrollBar vScrollBar;
		private System.Windows.Forms.Timer timer1;
        private VerticalMarks vMarksLeft;
    }
}
