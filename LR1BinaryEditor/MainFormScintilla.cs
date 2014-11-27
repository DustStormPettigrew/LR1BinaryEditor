using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using LibLR1.Utils;
using ScintillaNET;
using ScintillaNET.Configuration;

namespace LR1BinaryEditor
{
	public partial class MainFormScintilla : Form
	{
		private static string k_applicationName = "LR1 Binary Editor";

		Scintilla g_txtBox;

		private bool   m_unsavedChanges;
		private string m_fileName;

		public MainFormScintilla(string[] p_args)
		{
			InitializeComponent();

			Assembly assembly = Assembly.GetExecutingAssembly();
			Version ver = AssemblyName.GetAssemblyName(assembly.Location).Version;
			k_applicationName += string.Format(" [v{0}.{1}]", ver.Major, ver.Minor);
			g_LblBuild.Text = g_LblBuild.Text.Replace("X.Y", string.Format("{0}.{1}", ver.Build, ver.Revision));
			this.Text = k_applicationName;

			string executingDir = new FileInfo(assembly.Location).DirectoryName;

			Util.LoadKeywordInfo(executingDir);

			bool enableHighlighting = !p_args.Contains("-no-highlight");

			g_txtBox = new Scintilla();
			g_txtBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
			g_txtBox.Location = new Point(0, g_ToolStrip.Bottom);
			g_txtBox.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - g_ToolStrip.Height - g_StatusStrip.Height);
			g_txtBox.Font = new Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, (byte)0);
			g_txtBox.Location = new System.Drawing.Point(0, 25);

			g_txtBox.AllowDrop = true;
			g_txtBox.DragEnter += g_TxtBox_DragEnter;
			g_txtBox.DragDrop += g_TxtBox_DragDrop;
			g_txtBox.Whitespace.Mode = WhitespaceMode.Invisible;

			g_txtBox.Margins[0].Width = 32;
			g_txtBox.Margins[0].IsClickable = false;
			g_txtBox.Margins[0].IsFoldMargin = false;
			g_txtBox.Margins[1].Width = 16;
			g_txtBox.Margins[1].IsClickable = true;
			g_txtBox.Margins[1].IsFoldMargin = true;
			g_txtBox.Indentation.SmartIndentType = SmartIndent.CPP;

			if (enableHighlighting)
			{
				g_txtBox.ConfigurationManager.CustomLocation = Path.Combine(new string[] { executingDir, "styles.xml" });
				g_txtBox.ConfigurationManager.Language = "cpp";
				g_txtBox.Folding.MarkerScheme = FoldMarkerScheme.BoxPlusMinus;
				g_txtBox.Folding.UseCompactFolding = true;
				g_txtBox.Folding.IsEnabled = true;
			}

			this.Controls.Add(g_txtBox);


			string fileToOpen = "";
			for (int i = 0; i < p_args.Length; i++)
			{
				if (File.Exists(p_args[i]))
				{
					fileToOpen = p_args[i];
					break;
				}
			}
			if (fileToOpen != "")
			{
				Open(fileToOpen);
			}
			else
			{
				CreateNewFile();
			}
		}

		void g_TxtBox_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effect = DragDropEffects.Copy;
			}
		}

		void g_TxtBox_DragDrop(object sender, DragEventArgs e)
		{
			string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (files.Length > 0)
			{
				Open(files[0]);
			}
		}

		private void CreateNewFile()
		{
			if (m_unsavedChanges && !AreYouSure("create a new file"))
			{
				return;
			}
			g_txtBox.Text = "";
			m_fileName = "Untitled";
			UpdateFormTitle();
		}

		public void DisplayOpenDialog()
		{
			OpenFileDialog ofd = new OpenFileDialog()
			{
				FileName = "",
				Filter = Util.GetFileOpenFilter()
			};
			if (ofd.ShowDialog() == DialogResult.OK)
			{
				Open(ofd.FileName);
			}
		}

		private void Open(string p_filepath)
		{
			FileInfo fi = new FileInfo(p_filepath);
			using (BinaryReader br = new BinaryReader(BinaryFileHelper.Decompress(p_filepath)))
			{
				int indent = 0;
				int sqBracketStack = 0;
				int sqBracketCount = -1;
				StringBuilder buffer = new StringBuilder();
				string format = fi.Extension.Replace(".", "");
				while (br.BaseStream.Position < br.BaseStream.Length)
				{
					byte token = br.ReadByte();
					Util.RecursiveAppend(br, token, ref buffer, ref indent, ref sqBracketStack, ref sqBracketCount, format);
				}
				g_txtBox.Text = buffer.ToString().Trim();  // removes any trailing newlines :)
				m_fileName = fi.Name;
			}
			g_txtBox.UndoRedo.EmptyUndoBuffer();
			g_txtBox.Refresh();
			UpdateFormTitle();
		}

		public void DisplaySaveDialog()
		{
			SaveFileDialog sfd = new SaveFileDialog()
			{
				FileName = m_fileName,
				Filter = Util.GetFileOpenFilter()
			};
			if (sfd.ShowDialog() == DialogResult.OK)
			{
				Save(sfd.FileName);
			}
		}

		private void Save(string p_filepath)
		{
			g_txtBox.Enabled = false;
			MemoryStream ms = Util.Compile(g_txtBox.Text);
			using (FileStream fsOut = new FileStream(p_filepath, FileMode.Create, FileAccess.Write))
			{
				fsOut.Write(ms.ToArray(), 0, (int)ms.Length);
			}
			g_txtBox.Enabled = true;
		}

		private bool AreYouSure(string p_action)
		{
			return MessageBox.Show(
				string.Format("There are unsaved changes, are you sure you want to {0}?", p_action),
				"Are you sure?",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Warning
			) == DialogResult.Yes;
		}

		private void BtnNew_Click(object sender, EventArgs e)
		{
			CreateNewFile();
		}

		private void BtnOpen_Click(object sender, EventArgs e)
		{
			DisplayOpenDialog();
		}

		private void BtnSave_Click(object sender, EventArgs e)
		{
			DisplaySaveDialog();
		}

		private void Form1_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (m_unsavedChanges && !AreYouSure("quit"))
			{
				e.Cancel = true;
			}
		}

		private void UpdateFormTitle()
		{
			this.Text = string.Format("{0} | {1}", m_fileName, k_applicationName);
		}
	}
}