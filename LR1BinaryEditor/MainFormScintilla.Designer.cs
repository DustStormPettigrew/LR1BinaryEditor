namespace LR1BinaryEditor
{
	partial class MainFormScintilla
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainFormScintilla));
			this.g_ToolStrip = new System.Windows.Forms.ToolStrip();
			this.g_BtnNew = new System.Windows.Forms.ToolStripButton();
			this.g_BtnOpen = new System.Windows.Forms.ToolStripButton();
			this.g_BtnSave = new System.Windows.Forms.ToolStripButton();
			this.g_StatusStrip = new System.Windows.Forms.StatusStrip();
			this.g_LblBuild = new System.Windows.Forms.ToolStripStatusLabel();
			this.g_MenuStrip = new System.Windows.Forms.MenuStrip();
			this.g_MenuBtnNew = new System.Windows.Forms.ToolStripMenuItem();
			this.g_MenuBtnOpen = new System.Windows.Forms.ToolStripMenuItem();
			this.g_MenuBtnSave = new System.Windows.Forms.ToolStripMenuItem();
			this.g_ToolStrip.SuspendLayout();
			this.g_StatusStrip.SuspendLayout();
			this.g_MenuStrip.SuspendLayout();
			this.SuspendLayout();
			// 
			// g_ToolStrip
			// 
			this.g_ToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this.g_ToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.g_BtnNew,
			this.g_BtnOpen,
			this.g_BtnSave});
			this.g_ToolStrip.Location = new System.Drawing.Point(0, 0);
			this.g_ToolStrip.Name = "g_ToolStrip";
			this.g_ToolStrip.Size = new System.Drawing.Size(552, 25);
			this.g_ToolStrip.TabIndex = 0;
			this.g_ToolStrip.Text = "toolStrip1";
			// 
			// g_BtnNew
			// 
			this.g_BtnNew.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.g_BtnNew.Image = ((System.Drawing.Image)(resources.GetObject("g_BtnNew.Image")));
			this.g_BtnNew.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.g_BtnNew.Name = "g_BtnNew";
			this.g_BtnNew.Size = new System.Drawing.Size(23, 22);
			this.g_BtnNew.Text = "&New";
			this.g_BtnNew.Click += new System.EventHandler(this.BtnNew_Click);
			// 
			// g_BtnOpen
			// 
			this.g_BtnOpen.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.g_BtnOpen.Image = ((System.Drawing.Image)(resources.GetObject("g_BtnOpen.Image")));
			this.g_BtnOpen.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.g_BtnOpen.Name = "g_BtnOpen";
			this.g_BtnOpen.Size = new System.Drawing.Size(23, 22);
			this.g_BtnOpen.Text = "&Open";
			this.g_BtnOpen.Click += new System.EventHandler(this.BtnOpen_Click);
			// 
			// g_BtnSave
			// 
			this.g_BtnSave.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.g_BtnSave.Image = ((System.Drawing.Image)(resources.GetObject("g_BtnSave.Image")));
			this.g_BtnSave.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.g_BtnSave.Name = "g_BtnSave";
			this.g_BtnSave.Size = new System.Drawing.Size(23, 22);
			this.g_BtnSave.Text = "&Save";
			this.g_BtnSave.Click += new System.EventHandler(this.BtnSave_Click);
			// 
			// g_StatusStrip
			// 
			this.g_StatusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.g_LblBuild});
			this.g_StatusStrip.Location = new System.Drawing.Point(0, 418);
			this.g_StatusStrip.Name = "g_StatusStrip";
			this.g_StatusStrip.Size = new System.Drawing.Size(552, 22);
			this.g_StatusStrip.TabIndex = 1;
			this.g_StatusStrip.Text = "statusStrip1";
			// 
			// g_LblBuild
			// 
			this.g_LblBuild.Name = "g_LblBuild";
			this.g_LblBuild.Size = new System.Drawing.Size(160, 17);
			this.g_LblBuild.Text = "© Will Kirkby 2013   Build X.Y";
			// 
			// g_MenuStrip
			// 
			this.g_MenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.g_MenuBtnNew,
			this.g_MenuBtnOpen,
			this.g_MenuBtnSave});
			this.g_MenuStrip.Location = new System.Drawing.Point(0, 0);
			this.g_MenuStrip.Name = "g_MenuStrip";
			this.g_MenuStrip.Size = new System.Drawing.Size(552, 24);
			this.g_MenuStrip.TabIndex = 2;
			this.g_MenuStrip.Text = "menuStrip1";
			this.g_MenuStrip.Visible = false;
			// 
			// g_MenuBtnNew
			// 
			this.g_MenuBtnNew.Name = "g_MenuBtnNew";
			this.g_MenuBtnNew.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.N)));
			this.g_MenuBtnNew.Size = new System.Drawing.Size(43, 20);
			this.g_MenuBtnNew.Text = "New";
			this.g_MenuBtnNew.Click += new System.EventHandler(this.BtnNew_Click);
			// 
			// g_MenuBtnOpen
			// 
			this.g_MenuBtnOpen.Name = "g_MenuBtnOpen";
			this.g_MenuBtnOpen.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
			this.g_MenuBtnOpen.Size = new System.Drawing.Size(48, 20);
			this.g_MenuBtnOpen.Text = "Open";
			this.g_MenuBtnOpen.Click += new System.EventHandler(this.BtnOpen_Click);
			// 
			// g_MenuBtnSave
			// 
			this.g_MenuBtnSave.Name = "g_MenuBtnSave";
			this.g_MenuBtnSave.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)));
			this.g_MenuBtnSave.Size = new System.Drawing.Size(43, 20);
			this.g_MenuBtnSave.Text = "Save";
			this.g_MenuBtnSave.Click += new System.EventHandler(this.BtnSave_Click);
			// 
			// MainFormScintilla
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(552, 440);
			this.Controls.Add(this.g_StatusStrip);
			this.Controls.Add(this.g_ToolStrip);
			this.Controls.Add(this.g_MenuStrip);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MainMenuStrip = this.g_MenuStrip;
			this.Name = "MainFormScintilla";
			this.Text = "MainFormScintilla";
			this.g_ToolStrip.ResumeLayout(false);
			this.g_ToolStrip.PerformLayout();
			this.g_StatusStrip.ResumeLayout(false);
			this.g_StatusStrip.PerformLayout();
			this.g_MenuStrip.ResumeLayout(false);
			this.g_MenuStrip.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip g_ToolStrip;
		private System.Windows.Forms.ToolStripButton g_BtnNew;
		private System.Windows.Forms.ToolStripButton g_BtnOpen;
		private System.Windows.Forms.ToolStripButton g_BtnSave;
		private System.Windows.Forms.StatusStrip g_StatusStrip;
		private System.Windows.Forms.ToolStripStatusLabel g_LblBuild;
		private System.Windows.Forms.MenuStrip g_MenuStrip;
		private System.Windows.Forms.ToolStripMenuItem g_MenuBtnNew;
		private System.Windows.Forms.ToolStripMenuItem g_MenuBtnOpen;
		private System.Windows.Forms.ToolStripMenuItem g_MenuBtnSave;
	}
}