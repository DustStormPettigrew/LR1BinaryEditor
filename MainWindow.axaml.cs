using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using LibLR1.IO;
using LibLR1.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LR1BinaryEditor
{
    public partial class MainWindow : Window
    {
        private const string k_applicationName = "LR1 Binary Editor";

        private readonly string m_versionText;
        private bool m_unsavedChanges;
        private string m_fileName;
        private string m_currentFormat;
        private bool m_forceClose;
        private bool m_loadingEditorText;
        private readonly BraceFoldingStrategy m_braceFoldingStrategy = new BraceFoldingStrategy();
        private readonly FoldingManager m_foldingManager;
        private IHighlightingDefinition m_highlightingDefinition;
        private Dictionary<string, string> m_defaultHighlightColors = new Dictionary<string, string>();

        public MainWindow()
            : this(Array.Empty<string>())
        {
        }

        public MainWindow(string[] args)
        {
            InitializeComponent();

            m_foldingManager = FoldingManager.Install(g_Editor.TextArea);

            Assembly assembly = Assembly.GetExecutingAssembly();
            Version ver = assembly.GetName().Version;
            m_versionText = string.Format("Version {0}", ver);
            g_LblBuild.Text = m_versionText;

            Util.LoadKeywordInfo(AppContext.BaseDirectory);

            bool enableHighlighting = !args.Contains("-no-highlight");
            if (enableHighlighting)
            {
                IHighlightingDefinition highlighting = HighlightingManager.Instance.GetDefinition("C++");
                if (highlighting != null)
                {
                    m_highlightingDefinition = highlighting;
                    g_Editor.SyntaxHighlighting = highlighting;
                    m_defaultHighlightColors = GetHighlightingColors(highlighting);
                    LoadSavedHighlightingColors();
                }
            }

            g_Editor.TextChanged += (s, e) =>
            {
                UpdateFoldings();
                if (m_loadingEditorText) return;
                m_unsavedChanges = true;
                UpdateFormTitle();
            };

            this.KeyDown += OnWindowKeyDown;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);

            string fileToOpen = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (File.Exists(args[i]))
                {
                    fileToOpen = args[i];
                    break;
                }
            }

            if (fileToOpen != "")
                Open(fileToOpen);
            else
                ResetToNewFile();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            switch (e.Key)
            {
                case Key.N: _ = CreateNewFileWithConfirm(); e.Handled = true; break;
                case Key.O: _ = DisplayOpenDialog(); e.Handled = true; break;
                case Key.S: _ = DisplaySaveDialog(); e.Handled = true; break;
                case Key.E: _ = DisplayExportJsonDialog(); e.Handled = true; break;
                case Key.I: _ = DisplayImportJsonDialog(); e.Handled = true; break;
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(DataFormat.File))
                e.DragEffects = DragDropEffects.Copy;
            else
                e.DragEffects = DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(DataFormat.File))
            {
                var files = e.DataTransfer.TryGetFiles();
                var first = files?.FirstOrDefault();
                if (first != null)
                {
                    string path = first.TryGetLocalPath();
                    if (path != null)
                        Open(path);
                    e.Handled = true;
                }
            }
        }

        private void ResetToNewFile()
        {
            SetEditorText("");
            m_fileName = "Untitled";
            m_currentFormat = null;
            m_unsavedChanges = false;
            UpdateFormTitle();
        }

        private async Task CreateNewFileWithConfirm()
        {
            if (m_unsavedChanges)
            {
                bool confirmed = await ShowConfirmAsync(
                    "Are you sure?",
                    "There are unsaved changes, are you sure you want to create a new file?");
                if (!confirmed) return;
            }
            ResetToNewFile();
        }

        private async Task DisplayOpenDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Binary File",
                FileTypeFilter = BuildFileTypeFilter(),
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                string path = files[0].TryGetLocalPath();
                if (path != null)
                    Open(path);
            }
        }

        private void Open(string filePath)
        {
            try
            {
                FileInfo fi = new FileInfo(filePath);
                string format = fi.Extension.Replace(".", "");
                if (IsIndependentEncoding(fi))
                {
                    _ = ShowMessageAsync(
                        "Unsupported Raw Format",
                        fi.Extension.Equals(".LRS", StringComparison.OrdinalIgnoreCase) || fi.Name.StartsWith("LEGORac", StringComparison.OrdinalIgnoreCase)
                            ? "LRS saves use a fixed-struct encoding and are edited by LR1RacerEditor."
                            : "This file uses an independent encoding and is not supported by LR1BinaryEditor's token-stream editor.");
                    return;
                }

                using (LRBinaryReader br = BinaryFileHelper.Decompress(filePath))
                {
                    LoadEditorFromReader(br, format, fi.Name, false);
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Open Binary File", ex.Message);
            }
        }

        private async Task DisplaySaveDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Binary File",
                SuggestedFileName = m_fileName,
                FileTypeChoices = BuildFileTypeFilter()
            });

            if (file != null)
            {
                string path = file.TryGetLocalPath();
                if (path != null)
                    Save(path);
            }
        }

        private void Save(string filePath)
        {
            g_Editor.IsReadOnly = true;
            MemoryStream ms = Util.Compile(g_Editor.Text);
            using (FileStream fsOut = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                fsOut.Write(ms.ToArray(), 0, (int)ms.Length);
            g_Editor.IsReadOnly = false;
            m_fileName = Path.GetFileName(filePath);
            m_currentFormat = GetFormatFromFileName(m_fileName);
            m_unsavedChanges = false;
            UpdateFormTitle();
        }

        private void LoadEditorFromReader(LRBinaryReader reader, string format, string fileName, bool markDirty)
        {
            int indent = 0;
            int sqBracketStack = 0;
            int sqBracketCount = -1;
            StringBuilder buffer = new StringBuilder();
            string normalizedFormat = (format ?? "").Trim().TrimStart('.').ToUpperInvariant();

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                Token token = reader.ReadToken();
                Util.RecursiveAppend(reader, token, ref buffer, ref indent, ref sqBracketStack, ref sqBracketCount, normalizedFormat);
            }

            SetEditorText(buffer.ToString().Trim());
            m_fileName = fileName;
            m_currentFormat = normalizedFormat;
            m_unsavedChanges = markDirty;
            UpdateFormTitle();
        }

        private void SetEditorText(string text)
        {
            m_loadingEditorText = true;
            try
            {
                g_Editor.Text = text ?? "";
                g_Editor.Document.UndoStack.ClearAll();
                g_Editor.TextArea.Caret.Offset = 0;
                g_Editor.ScrollToHome();
                UpdateFoldings();
                g_LblBuild.Text = string.Format("{0} | {1:N0} characters", m_versionText, g_Editor.Text?.Length ?? 0);
            }
            finally
            {
                m_loadingEditorText = false;
            }
        }

        private MemoryStream GetCompiledEditorBuffer()
        {
            return Util.Compile(g_Editor.Text);
        }

        private string GetFormatFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension)) return null;
            return extension.TrimStart('.').ToUpperInvariant();
        }

        private static bool IsIndependentEncoding(FileInfo file)
        {
            return file.Extension.Equals(".LRS", StringComparison.OrdinalIgnoreCase)
                || file.Extension.Equals(".BMP", StringComparison.OrdinalIgnoreCase)
                || file.Extension.Equals(".SRF", StringComparison.OrdinalIgnoreCase)
                || file.Name.StartsWith("LEGORac", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateFoldings()
        {
            m_braceFoldingStrategy.UpdateFoldings(m_foldingManager, g_Editor.Document);
        }

        private async Task DisplayExportJsonDialog()
        {
            string format = m_currentFormat ?? GetFormatFromFileName(m_fileName);
            if (!LibLR1JsonBridge.CanExport(format, out string error))
            {
                await ShowMessageAsync("Export as JSON", error);
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export as JSON",
                SuggestedFileName = Path.ChangeExtension(m_fileName ?? "Untitled", ".json"),
                FileTypeChoices = new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } }
            });

            if (file != null)
            {
                string path = file.TryGetLocalPath();
                if (path != null)
                    ExportJson(path, format);
            }
        }

        private void ExportJson(string outputPath, string format)
        {
            try
            {
                using (MemoryStream binaryBuffer = GetCompiledEditorBuffer())
                {
                    if (!LibLR1JsonBridge.TryExportJson(format, m_fileName, binaryBuffer, out string json, out string error))
                        throw new InvalidOperationException(error);
                    File.WriteAllText(outputPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Export as JSON", ex.Message);
            }
        }

        private async Task DisplayImportJsonDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import as JSON",
                FileTypeFilter = new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } },
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                string path = files[0].TryGetLocalPath();
                if (path != null)
                    ImportJson(path);
            }
        }

        private void ImportJson(string inputPath)
        {
            try
            {
                string jsonText = File.ReadAllText(inputPath);
                if (!LibLR1JsonBridge.TryImportJson(jsonText, out ImportedJsonDocument imported, out string error))
                    throw new InvalidOperationException(error);

                if (!LibLR1JsonBridge.TryWriteBinary(imported.Format, imported.Model, out MemoryStream binaryBuffer, out error))
                    throw new InvalidOperationException(error);
                using (binaryBuffer)
                using (LRBinaryReader reader = new LRBinaryReader(binaryBuffer, false))
                {
                    LoadEditorFromReader(reader, imported.Format, imported.FileName, true);
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Import as JSON", ex.Message);
            }
        }

        private void UpdateFormTitle()
        {
            string dirtyMarker = m_unsavedChanges ? "*" : "";
            Title = string.Format("{0}{1} | {2}", m_fileName, dirtyMarker, k_applicationName);
        }

        private async void MenuSyntaxColors_Click(object sender, RoutedEventArgs e)
        {
            if (m_highlightingDefinition == null)
            {
                await ShowMessageAsync("Syntax Highlighting Colors", "Syntax highlighting is disabled or unavailable.");
                return;
            }

            var dialog = new SyntaxHighlightingColorsWindow(
                GetHighlightingColors(m_highlightingDefinition),
                m_defaultHighlightColors);

            Dictionary<string, string> result = await dialog.ShowDialog<Dictionary<string, string>>(this);
            if (result == null)
                return;

            ApplyHighlightingColors(result);
            SaveHighlightingColors(result);
        }

        private Dictionary<string, string> GetHighlightingColors(IHighlightingDefinition highlighting)
        {
            var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (HighlightingColor color in highlighting.NamedHighlightingColors.OrderBy(c => c.Name))
            {
                Avalonia.Media.Color? foreground = color.Foreground?.GetColor(null);
                if (foreground.HasValue)
                    colors[color.Name] = ColorToHex(foreground.Value);
            }
            return colors;
        }

        private void ApplyHighlightingColors(Dictionary<string, string> colors)
        {
            if (m_highlightingDefinition == null)
                return;

            foreach (KeyValuePair<string, string> kvp in colors)
            {
                HighlightingColor color = m_highlightingDefinition.GetNamedColor(kvp.Key);
                if (color == null)
                    continue;

                if (TryParseColor(kvp.Value, out Avalonia.Media.Color parsed))
                    color.Foreground = new SimpleHighlightingBrush(parsed);
            }

            g_Editor.TextArea.TextView.InvalidateVisual();
        }

        private void LoadSavedHighlightingColors()
        {
            string path = GetHighlightingColorsPath();
            if (!File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<string, string> colors = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (colors != null)
                    ApplyHighlightingColors(colors);
            }
            catch
            {
                // Ignore invalid user color config and keep defaults.
            }
        }

        private void SaveHighlightingColors(Dictionary<string, string> colors)
        {
            string path = GetHighlightingColorsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonSerializer.Serialize(colors, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private static string GetHighlightingColorsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "LR1BinaryEditor", "syntax-colors.json");
        }

        internal static bool TryParseColor(string text, out Avalonia.Media.Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();
            if (value.Length == 6 && value.All(IsHex))
                value = "#" + value;

            try
            {
                color = Avalonia.Media.Color.Parse(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string ColorToHex(Avalonia.Media.Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static bool IsHex(char ch)
        {
            return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
        }

        private static FilePickerFileType[] BuildFileTypeFilter()
        {
            // Parse the legacy pipe-delimited filter string: "Description|*.ext1;*.ext2|Name1 (*.ext1)|*.ext1|..."
            string filter = Util.GetFileOpenFilter();
            string[] parts = filter.Split('|');
            var types = new List<FilePickerFileType>();

            if (parts.Length >= 2)
            {
                string[] allPatterns = parts[1].Split(';').Where(p => p.Length > 0).ToArray();
                types.Add(new FilePickerFileType(parts[0]) { Patterns = allPatterns });
            }
            for (int i = 2; i + 1 < parts.Length; i += 2)
                types.Add(new FilePickerFileType(parts[i]) { Patterns = new[] { parts[i + 1] } });

            return types.ToArray();
        }

        // Button/menu click handlers
        private void BtnNew_Click(object sender, RoutedEventArgs e) => _ = CreateNewFileWithConfirm();
        private void BtnOpen_Click(object sender, RoutedEventArgs e) => _ = DisplayOpenDialog();
        private void BtnSave_Click(object sender, RoutedEventArgs e) => _ = DisplaySaveDialog();
        private void BtnExportJson_Click(object sender, RoutedEventArgs e) => _ = DisplayExportJsonDialog();
        private void BtnImportJson_Click(object sender, RoutedEventArgs e) => _ = DisplayImportJsonDialog();

        private async void Window_Closing(object sender, WindowClosingEventArgs e)
        {
            if (m_unsavedChanges && !m_forceClose)
            {
                e.Cancel = true;
                bool confirmed = await ShowConfirmAsync(
                    "Are you sure?",
                    "There are unsaved changes, are you sure you want to quit?");
                if (confirmed)
                {
                    m_forceClose = true;
                    Close();
                }
            }
        }

        private Task ShowMessageAsync(string title, string message)
            => ShowDialogInternal(title, message, isConfirm: false);

        private Task<bool> ShowConfirmAsync(string title, string message)
            => ShowDialogInternal(title, message, isConfirm: true);

        private async Task<bool> ShowDialogInternal(string title, string message, bool isConfirm)
        {
            bool result = false;

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var btnOk = new Button { Content = isConfirm ? "Yes" : "OK", MinWidth = 75, Margin = new Thickness(4) };
            btnOk.Click += (s, e) => { result = true; dialog.Close(); };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            buttonPanel.Children.Add(btnOk);

            if (isConfirm)
            {
                var btnNo = new Button { Content = "No", MinWidth = 75, Margin = new Thickness(4) };
                btnNo.Click += (s, e) => { result = false; dialog.Close(); };
                buttonPanel.Children.Add(btnNo);
            }

            var content = new StackPanel { Margin = new Thickness(20) };
            content.Children.Add(textBlock);
            content.Children.Add(buttonPanel);
            dialog.Content = content;

            await dialog.ShowDialog(this);
            return result;
        }
    }
}
