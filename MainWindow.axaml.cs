using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
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
        private static readonly FilePickerFileType[] ms_binaryFileTypes = BuildBinaryFileTypes();

        private readonly string m_versionText;
        private readonly List<EditorDocument> m_documents = new List<EditorDocument>();
        private bool m_forceClose;
        private bool m_filePickerOpen;
        private IHighlightingDefinition m_highlightingDefinition;
        private Dictionary<string, string> m_defaultHighlightColors = new Dictionary<string, string>();

        public MainWindow()
            : this(Array.Empty<string>())
        {
        }

        public MainWindow(string[] args)
        {
            InitializeComponent();
            g_Tabs.SelectionChanged += Tabs_SelectionChanged;

            Assembly assembly = Assembly.GetExecutingAssembly();
            Version ver = assembly.GetName().Version;
            m_versionText = string.Format("Version {0}", ver);
            g_LblBuild.Text = m_versionText;

            Util.LoadKeywordInfo(AppContext.BaseDirectory);

            bool enableHighlighting = !args.Contains("-no-highlight");
            if (enableHighlighting)
            {
                IHighlightingDefinition highlighting = Lr1HighlightingDefinition.Create();
                m_highlightingDefinition = highlighting;
                m_defaultHighlightColors = GetHighlightingColors(highlighting);
                LoadSavedHighlightingColors();
            }

            this.KeyDown += OnWindowKeyDown;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);

            string[] filesToOpen = GetExistingFileArgs(args).ToArray();
            if (filesToOpen.Length > 0)
                OpenFiles(filesToOpen);
            else
                CreateNewDocument();
        }

        public void OpenFilesFromExternal(IEnumerable<string> args)
        {
            Activate();

            string[] filesToOpen = GetExistingFileArgs(args).ToArray();
            if (filesToOpen.Length > 0)
                OpenFiles(filesToOpen);
        }

        private static IEnumerable<string> GetExistingFileArgs(IEnumerable<string> args)
        {
            foreach (string arg in args ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("-"))
                    continue;

                string path = Path.GetFullPath(arg);
                if (File.Exists(path))
                    yield return path;
            }
        }

        private EditorDocument CurrentDocument
            => (g_Tabs?.SelectedItem as TabItem)?.Tag as EditorDocument;

        private TextEditor CurrentEditor
            => CurrentDocument?.Editor;

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            switch (e.Key)
            {
                case Key.N: CreateNewDocument(); e.Handled = true; break;
                case Key.O: _ = DisplayOpenDialog(); e.Handled = true; break;
                case Key.W: _ = CloseCurrentDocument(); e.Handled = true; break;
                case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift): _ = DisplaySaveAsDialog(); e.Handled = true; break;
                case Key.S: _ = SaveCurrentOrShowDialog(); e.Handled = true; break;
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
            if (!e.DataTransfer.Contains(DataFormat.File))
                return;

            var files = e.DataTransfer.TryGetFiles();
            if (files == null)
                return;

            string[] paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => path != null)
                .ToArray();

            OpenFiles(paths);
            e.Handled = true;
        }

        private EditorDocument CreateNewDocument()
        {
            EditorDocument document = CreateDocument("Untitled", null, null, false);
            SetEditorText(document, "");
            AddDocument(document);
            return document;
        }

        private EditorDocument CreateDocument(string fileName, string filePath, string format, bool unsavedChanges)
        {
            EditorDocument document = new EditorDocument
            {
                FileName = string.IsNullOrWhiteSpace(fileName) ? "Untitled" : fileName,
                FilePath = filePath,
                CurrentFormat = NormalizeFormat(format),
                UnsavedChanges = unsavedChanges
            };

            document.Editor = new TextEditor
            {
                FontFamily = new FontFamily("Consolas,Menlo,Courier New,monospace"),
                FontSize = 13,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                ShowLineNumbers = true,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                SyntaxHighlighting = m_highlightingDefinition
            };

            document.FoldingManager = FoldingManager.Install(document.Editor.TextArea);
            document.Editor.TextChanged += (s, e) =>
            {
                UpdateFoldings(document);
                if (document.LoadingEditorText) return;
                document.UnsavedChanges = true;
                UpdateDocumentHeader(document);
                UpdateFormTitle();
            };

            return document;
        }

        private void AddDocument(EditorDocument document)
        {
            document.Tab = new TabItem { Content = document.Editor, Tag = document };
            document.Tab.Header = CreateTabHeader(document);

            m_documents.Add(document);
            g_Tabs.Items.Add(document.Tab);
            g_Tabs.SelectedItem = document.Tab;
            UpdateDocumentHeader(document);
            UpdateFormTitle();
            UpdateStatusText(document);
        }

        private Control CreateTabHeader(EditorDocument document)
        {
            Border border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                Padding = new Thickness(8, 2, 4, 2),
                Margin = new Thickness(0),
                MinHeight = 24
            };

            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };

            document.HeaderBorder = border;
            document.HeaderText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                MaxWidth = 160,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Button closeButton = new Button
            {
                Content = "x",
                Padding = new Thickness(3, 0),
                MinWidth = 18,
                MinHeight = 18,
                FontSize = 12,
                Tag = document
            };
            closeButton.Click += CloseTab_Click;

            panel.Children.Add(document.HeaderText);
            panel.Children.Add(closeButton);
            border.Child = panel;
            return border;
        }

        private void UpdateDocumentHeader(EditorDocument document)
        {
            if (document?.HeaderText == null)
                return;

            document.HeaderText.Text = document.FileName + (document.UnsavedChanges ? "*" : "");
            UpdateTabHeaderVisual(document);
        }

        private void UpdateAllTabHeaderVisuals()
        {
            foreach (EditorDocument document in m_documents)
                UpdateTabHeaderVisual(document);
        }

        private void UpdateTabHeaderVisual(EditorDocument document)
        {
            if (document?.HeaderBorder == null || document.HeaderText == null)
                return;

            bool selected = g_Tabs?.SelectedItem == document.Tab;
            document.HeaderBorder.Background = selected
                ? new SolidColorBrush(Color.Parse("#DCEBFF"))
                : new SolidColorBrush(Color.Parse("#ECECEC"));
            document.HeaderBorder.BorderBrush = selected
                ? new SolidColorBrush(Color.Parse("#2D7DCE"))
                : new SolidColorBrush(Color.Parse("#B8B8B8"));
            document.HeaderBorder.BorderThickness = selected
                ? new Thickness(1, 1, 1, 2)
                : new Thickness(1);
            document.HeaderText.FontWeight = selected
                ? FontWeight.SemiBold
                : FontWeight.Normal;
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAllTabHeaderVisuals();
            UpdateFormTitle();
            UpdateStatusText(CurrentDocument);
        }

        private async void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button button && button.Tag is EditorDocument document)
                await CloseDocumentAsync(document);
        }

        private Task CloseCurrentDocument()
        {
            EditorDocument document = CurrentDocument;
            return document == null ? Task.CompletedTask : CloseDocumentAsync(document);
        }

        private async Task<bool> CloseDocumentAsync(EditorDocument document)
        {
            if (document == null)
                return true;

            if (document.UnsavedChanges)
            {
                SaveChangesChoice choice = await ShowSaveChangesDialogAsync(document);
                if (choice == SaveChangesChoice.Cancel)
                    return false;

                if (choice == SaveChangesChoice.Save)
                {
                    bool saved = await SaveDocumentOrShowDialog(document);
                    if (!saved)
                        return false;
                }
            }

            RemoveDocument(document);
            return true;
        }

        private void RemoveDocument(EditorDocument document)
        {
            int index = m_documents.IndexOf(document);
            if (index < 0)
                return;

            FoldingManager.Uninstall(document.FoldingManager);
            m_documents.RemoveAt(index);
            g_Tabs.Items.Remove(document.Tab);

            if (m_documents.Count > 0 && g_Tabs.SelectedItem == null)
                g_Tabs.SelectedItem = m_documents[Math.Min(index, m_documents.Count - 1)].Tab;

            UpdateFormTitle();
            UpdateStatusText(CurrentDocument);
        }

        private async Task DisplayOpenDialog()
        {
            if (m_filePickerOpen) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            m_filePickerOpen = true;
            try
            {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open Binary File",
                    FileTypeFilter = ms_binaryFileTypes,
                    AllowMultiple = true
                });

                string[] paths = files
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => path != null)
                    .ToArray();

                OpenFiles(paths);
            }
            finally
            {
                m_filePickerOpen = false;
            }
        }

        private void OpenFiles(IEnumerable<string> filePaths)
        {
            foreach (string filePath in filePaths)
                Open(filePath);
        }

        private void Open(string filePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                EditorDocument existing = m_documents.FirstOrDefault(document =>
                    !string.IsNullOrWhiteSpace(document.FilePath)
                    && string.Equals(Path.GetFullPath(document.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    g_Tabs.SelectedItem = existing.Tab;
                    return;
                }

                FileInfo fi = new FileInfo(fullPath);
                string format = fi.Extension.Replace(".", "");
                if (IsIndependentEncoding(fi))
                {
                    _ = ShowMessageAsync(
                        "Unsupported Raw Format",
                        IsLrsSaveFile(fi)
                            ? "LRS saves use a fixed-struct encoding and are edited by LR1RacerEditor."
                            : "This file uses an independent encoding and is not supported by LR1BinaryEditor's token-stream editor.");
                    return;
                }

                using (LRBinaryReader br = BinaryFileHelper.Decompress(fullPath))
                {
                    LoadEditorFromReader(br, format, fi.Name, fullPath, false);
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Open Binary File", ex.Message);
            }
        }

        private Task<bool> SaveCurrentOrShowDialog()
        {
            EditorDocument document = CurrentDocument;
            return document == null ? Task.FromResult(false) : SaveDocumentOrShowDialog(document);
        }

        private Task<bool> SaveDocumentOrShowDialog(EditorDocument document)
        {
            if (!CanSaveToCurrentFile(document))
                return DisplaySaveAsDialog(document);

            return Task.FromResult(Save(document, document.FilePath));
        }

        private bool CanSaveToCurrentFile(EditorDocument document)
        {
            if (document == null || string.IsNullOrWhiteSpace(document.FilePath) || !File.Exists(document.FilePath))
                return false;

            FileAttributes attributes = File.GetAttributes(document.FilePath);
            return (attributes & FileAttributes.ReadOnly) == 0;
        }

        private Task<bool> DisplaySaveAsDialog()
        {
            EditorDocument document = CurrentDocument;
            return document == null ? Task.FromResult(false) : DisplaySaveAsDialog(document);
        }

        private async Task<bool> DisplaySaveAsDialog(EditorDocument document)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return false;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Binary File As",
                SuggestedFileName = document.FileName,
                FileTypeChoices = ms_binaryFileTypes
            });

            if (file == null)
                return false;

            string path = file.TryGetLocalPath();
            return path != null && Save(document, path);
        }

        private bool Save(EditorDocument document, string filePath)
        {
            try
            {
                document.Editor.IsReadOnly = true;
                using (MemoryStream ms = Util.Compile(document.Editor.Text))
                using (FileStream fsOut = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    fsOut.Write(ms.ToArray(), 0, (int)ms.Length);
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Save Binary File", ex.Message);
                return false;
            }
            finally
            {
                document.Editor.IsReadOnly = false;
            }

            document.FileName = Path.GetFileName(filePath);
            document.FilePath = filePath;
            document.CurrentFormat = GetFormatFromFileName(document.FileName);
            document.UnsavedChanges = false;
            UpdateDocumentHeader(document);
            UpdateFormTitle();
            return true;
        }

        private void LoadEditorFromReader(LRBinaryReader reader, string format, string fileName, string filePath, bool markDirty)
        {
            int indent = 0;
            int sqBracketStack = 0;
            int sqBracketCount = -1;
            StringBuilder buffer = new StringBuilder();
            string normalizedFormat = NormalizeFormat(format);
            string pendingKeywordInfo = null;

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                Token token = reader.ReadToken();
                Util.RecursiveAppend(reader, token, ref buffer, ref indent, ref sqBracketStack, ref sqBracketCount, ref pendingKeywordInfo, normalizedFormat);
            }

            EditorDocument document = CreateDocument(fileName, filePath, normalizedFormat, markDirty);
            SetEditorText(document, buffer.ToString().Trim());
            AddDocument(document);
        }

        private void SetEditorText(EditorDocument document, string text)
        {
            document.LoadingEditorText = true;
            try
            {
                document.Editor.Text = text ?? "";
                document.Editor.Document.UndoStack.ClearAll();
                document.Editor.TextArea.Caret.Offset = 0;
                document.Editor.ScrollToHome();
                UpdateFoldings(document);
                UpdateStatusText(document);
            }
            finally
            {
                document.LoadingEditorText = false;
            }
        }

        private MemoryStream GetCompiledEditorBuffer(EditorDocument document)
        {
            return Util.Compile(document.Editor.Text);
        }

        private string GetFormatFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension)) return null;
            return NormalizeFormat(extension);
        }

        private static string NormalizeFormat(string format)
        {
            return string.IsNullOrWhiteSpace(format)
                ? null
                : format.Trim().TrimStart('.').ToUpperInvariant();
        }

        private static bool IsIndependentEncoding(FileInfo file)
        {
            return IsLrsSaveFile(file)
                || file.Extension.Equals(".BMP", StringComparison.OrdinalIgnoreCase)
                || file.Extension.Equals(".SRF", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLrsSaveFile(FileInfo file)
        {
            return file.Extension.Equals(".LRS", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrEmpty(file.Extension) && file.Name.StartsWith("LEGORac", StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateFoldings(EditorDocument document)
        {
            document.BraceFoldingStrategy.UpdateFoldings(document.FoldingManager, document.Editor.Document);
        }

        private async Task DisplayExportJsonDialog()
        {
            EditorDocument document = CurrentDocument;
            if (document == null)
                return;

            string format = document.CurrentFormat ?? GetFormatFromFileName(document.FileName);
            if (!LibLR1JsonBridge.CanExport(format, out string error))
            {
                await ShowMessageAsync("Export as JSON", error);
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export as JSON",
                SuggestedFileName = Path.ChangeExtension(document.FileName ?? "Untitled", ".json"),
                FileTypeChoices = new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } }
            });

            if (file != null)
            {
                string path = file.TryGetLocalPath();
                if (path != null)
                    ExportJson(document, path, format);
            }
        }

        private void ExportJson(EditorDocument document, string outputPath, string format)
        {
            try
            {
                using (MemoryStream binaryBuffer = GetCompiledEditorBuffer(document))
                {
                    if (!LibLR1JsonBridge.TryExportJson(format, document.FileName, binaryBuffer, out string json, out string error))
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
                    LoadEditorFromReader(reader, imported.Format, imported.FileName, null, true);
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Import as JSON", ex.Message);
            }
        }

        private void UpdateFormTitle()
        {
            EditorDocument document = CurrentDocument;
            if (document == null)
            {
                Title = k_applicationName;
                return;
            }

            string dirtyMarker = document.UnsavedChanges ? "*" : "";
            Title = string.Format("{0}{1} | {2}", document.FileName, dirtyMarker, k_applicationName);
        }

        private void UpdateStatusText(EditorDocument document)
        {
            if (document == null)
            {
                g_LblBuild.Text = m_versionText;
                return;
            }

            g_LblBuild.Text = string.Format("{0} | {1:N0} characters", m_versionText, document.Editor.Text?.Length ?? 0);
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

            foreach (EditorDocument document in m_documents)
                document.Editor.TextArea.TextView.InvalidateVisual();
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

        private static FilePickerFileType[] BuildBinaryFileTypes()
        {
            var types = new List<FilePickerFileType>();
            types.Add(CreateAllSupportedFileType());
            foreach (KeyValuePair<string, string> kvp in Util.FileFormats)
                types.Add(CreateFileType(kvp.Key, kvp.Value));
            types.Add(FilePickerFileTypes.All);
            return types.ToArray();
        }

        private static FilePickerFileType CreateAllSupportedFileType()
        {
            string[] patterns = Util.FileFormats.Keys
                .Select(format => "*." + format)
                .ToArray();
            return new FilePickerFileType("LR1 Binary Formats") { Patterns = patterns };
        }

        private static FilePickerFileType CreateFileType(string format, string description)
        {
            string name = string.IsNullOrWhiteSpace(description) ? "Unknown_" + format : description;
            return new FilePickerFileType(string.Format("{0} (*.{1})", name, format))
            {
                Patterns = new[] { "*." + format }
            };
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e) => CreateNewDocument();
        private void BtnOpen_Click(object sender, RoutedEventArgs e) => _ = DisplayOpenDialog();
        private void BtnSave_Click(object sender, RoutedEventArgs e) => _ = SaveCurrentOrShowDialog();
        private void BtnSaveAs_Click(object sender, RoutedEventArgs e) => _ = DisplaySaveAsDialog();
        private void BtnExportJson_Click(object sender, RoutedEventArgs e) => _ = DisplayExportJsonDialog();
        private void BtnImportJson_Click(object sender, RoutedEventArgs e) => _ = DisplayImportJsonDialog();
        private async void MenuAbout_Click(object sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);

        private async void Window_Closing(object sender, WindowClosingEventArgs e)
        {
            if (m_forceClose)
                return;

            e.Cancel = true;
            bool closed = await CloseAllDocumentsForExitAsync();
            if (closed)
            {
                m_forceClose = true;
                Close();
            }
        }

        private async Task<bool> CloseAllDocumentsForExitAsync()
        {
            while (m_documents.Count > 0)
            {
                EditorDocument document = m_documents[0];
                g_Tabs.SelectedItem = document.Tab;
                bool closed = await CloseDocumentAsync(document);
                if (!closed)
                    return false;
            }

            return true;
        }

        private Task ShowMessageAsync(string title, string message)
            => ShowDialogInternal(title, message, new[] { "OK" }).ContinueWith(_ => { });

        private async Task<SaveChangesChoice> ShowSaveChangesDialogAsync(EditorDocument document)
        {
            string message = string.Format("Save changes to {0} before closing?", document.FileName);
            int choice = await ShowDialogInternal("Save Changes", message, new[] { "Save", "Don't Save", "Cancel" });
            switch (choice)
            {
                case 0: return SaveChangesChoice.Save;
                case 1: return SaveChangesChoice.DontSave;
                default: return SaveChangesChoice.Cancel;
            }
        }

        private async Task<int> ShowDialogInternal(string title, string message, string[] buttons)
        {
            int result = buttons.Length - 1;

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

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            for (int i = 0; i < buttons.Length; i++)
            {
                int buttonIndex = i;
                var button = new Button { Content = buttons[i], MinWidth = 75, Margin = new Thickness(4) };
                button.Click += (s, e) =>
                {
                    result = buttonIndex;
                    dialog.Close();
                };
                buttonPanel.Children.Add(button);
            }

            var content = new StackPanel { Margin = new Thickness(20) };
            content.Children.Add(textBlock);
            content.Children.Add(buttonPanel);
            dialog.Content = content;

            await dialog.ShowDialog(this);
            return result;
        }

        private sealed class EditorDocument
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
            public string CurrentFormat { get; set; }
            public bool UnsavedChanges { get; set; }
            public bool LoadingEditorText { get; set; }
            public TextEditor Editor { get; set; }
            public TabItem Tab { get; set; }
            public Border HeaderBorder { get; set; }
            public TextBlock HeaderText { get; set; }
            public FoldingManager FoldingManager { get; set; }
            public BraceFoldingStrategy BraceFoldingStrategy { get; } = new BraceFoldingStrategy();
        }

        private enum SaveChangesChoice
        {
            Save,
            DontSave,
            Cancel
        }
    }
}
