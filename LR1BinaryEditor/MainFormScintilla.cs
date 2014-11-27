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

namespace LR1BinaryEditor {
    public partial class MainFormScintilla : Form {

        private static string APPLICATION_NAME = "LR1 Binary Editor";

        Scintilla g_TxtBox;

        private bool m_UnsavedChanges;
        private string m_FileName;

        public MainFormScintilla(string[] args) {
            InitializeComponent();

            Assembly assembly = Assembly.GetExecutingAssembly();
            Version ver = AssemblyName.GetAssemblyName(assembly.Location).Version;
            APPLICATION_NAME += " [v" + ver.Major + "." + ver.Minor + "]";
            g_LblBuild.Text = g_LblBuild.Text.Replace("X.Y", ver.Build + "." + ver.Revision);
            this.Text = APPLICATION_NAME;

            string executing_dir = new FileInfo(assembly.Location).DirectoryName;

            Util.LoadKeywordInfo(executing_dir);

            bool enable_highlighting = !args.Contains("-no-highlight");

            g_TxtBox = new Scintilla();
            g_TxtBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            g_TxtBox.Location = new Point(0, g_ToolStrip.Bottom);
            g_TxtBox.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - g_ToolStrip.Height - g_StatusStrip.Height);
            g_TxtBox.Font = new Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, (byte)0);
            g_TxtBox.Location = new System.Drawing.Point(0, 25);

            g_TxtBox.AllowDrop = true;
            g_TxtBox.DragEnter += g_TxtBox_DragEnter;
            g_TxtBox.DragDrop += g_TxtBox_DragDrop;
            g_TxtBox.Whitespace.Mode = WhitespaceMode.Invisible;

            g_TxtBox.Margins[0].Width = 32;
            g_TxtBox.Margins[0].IsClickable = false;
            g_TxtBox.Margins[0].IsFoldMargin = false;
            g_TxtBox.Margins[1].Width = 16;
            g_TxtBox.Margins[1].IsClickable = true;
            g_TxtBox.Margins[1].IsFoldMargin = true;
            g_TxtBox.Indentation.SmartIndentType = SmartIndent.CPP;

            if (enable_highlighting) {
                g_TxtBox.ConfigurationManager.CustomLocation = Path.Combine(new string[] { executing_dir, "styles.xml" });
                g_TxtBox.ConfigurationManager.Language = "cpp";
                g_TxtBox.Folding.MarkerScheme = FoldMarkerScheme.BoxPlusMinus;
                g_TxtBox.Folding.UseCompactFolding = true;
                g_TxtBox.Folding.IsEnabled = true;
            }

            this.Controls.Add(g_TxtBox);


            string file_to_open = "";
            for (int i = 0; i < args.Length; i++) {
                if (File.Exists(args[i])) {
                    file_to_open = args[i];
                    break;
                }
            }
            if (file_to_open != "")
                Open(file_to_open);
            else
                CreateNewFile();
        }

        void g_TxtBox_DragEnter(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        void g_TxtBox_DragDrop(object sender, DragEventArgs e) {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0) {
                Open(files[0]);
            }
        }

        private void CreateNewFile() {
            if (m_UnsavedChanges) {
                if (!AreYouSure("create a new file")) {
                    return;
                }
            }
            g_TxtBox.Text = "";
            m_FileName = "Untitled";
            UpdateFormTitle();
        }

        public void DisplayOpenDialog() {
            OpenFileDialog ofd = new OpenFileDialog() {
                FileName = "",
                Filter = Util.GetFileOpenFilter()
            };
            if (ofd.ShowDialog() == DialogResult.OK) {
                Open(ofd.FileName);
            }
        }
        
        private void Open(string path) {
            FileInfo fi = new FileInfo(path);
            using (BinaryReader br = new BinaryReader(BinaryFileHelper.Decompress(path))) {
                int indent = 0;
                int sq_bracket_stack = 0;
                int sq_bracket_count = -1;
                string buffer = "";
                string format = fi.Extension.Replace(".", "");
                while (br.BaseStream.Position < br.BaseStream.Length) {
                    byte token = br.ReadByte();
                    Util.RecursiveAppend(br, token, ref buffer, ref indent, ref sq_bracket_stack, ref sq_bracket_count, format);
                }
                g_TxtBox.Text = buffer.Trim();  // removes any trailing newlines :)
                m_FileName = fi.Name;
            }
            g_TxtBox.UndoRedo.EmptyUndoBuffer();
            g_TxtBox.Refresh();
            UpdateFormTitle();
        }

        public void DisplaySaveDialog() {
            SaveFileDialog sfd = new SaveFileDialog() {
                FileName = m_FileName,
                Filter = Util.GetFileOpenFilter()
            };
            if (sfd.ShowDialog() == DialogResult.OK) {
                Save(sfd.FileName);
            }
        }

        private void Save(string path) {
            g_TxtBox.Enabled = false;
            MemoryStream ms = Util.Compile(g_TxtBox.Text);
            using (FileStream fsOut = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                fsOut.Write(ms.ToArray(), 0, (int)ms.Length);
            }
            g_TxtBox.Enabled = true;
        }

        private bool AreYouSure(string action) {
            return MessageBox.Show("There are unsaved changes, are you sure you want to " + action + "?", "Are you sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void BtnNew_Click(object sender, System.EventArgs e) {
            CreateNewFile();
        }

        private void BtnOpen_Click(object sender, System.EventArgs e) {
            DisplayOpenDialog();
        }

        private void BtnSave_Click(object sender, System.EventArgs e) {
            DisplaySaveDialog();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e) {
            if (m_UnsavedChanges) {
                if (!AreYouSure("quit"))
                    e.Cancel = true;
            }
        }

        private void UpdateFormTitle() {
            this.Text = m_FileName + " | " + APPLICATION_NAME;
        }
    }
}