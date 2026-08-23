using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using LibLR1;
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
        private readonly BinaryEditorDocumentService m_documentService = new BinaryEditorDocumentService();
        private readonly Dictionary<string, JamArchiveSession> m_jamSessions = new Dictionary<string, JamArchiveSession>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Bitmap> m_iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private bool m_forceClose;
        private bool m_filePickerOpen;
        private bool m_navigationPaneVisible = true;
        private bool m_navigationPaneRight;
        private string m_navigationRoot;
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
            g_NavTree.SelectionChanged += NavTree_SelectionChanged;
            LoadNavigationSettings();
            UpdateNavigationStatus();
            UpdateNavigationPaneLayout();
            RefreshNavigationTree();

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
                case Key.R: ValidateCurrentDocument(true); e.Handled = true; break;
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
            document.ListGroupSeparatorRenderer = new ListGroupSeparatorRenderer();
            document.Editor.TextArea.TextView.BackgroundRenderers.Add(document.ListGroupSeparatorRenderer);
            document.Editor.TextChanged += (s, e) =>
            {
                UpdateFoldings(document);
                UpdateListGroupSeparators(document);
                if (document.LoadingEditorText) return;
                document.UnsavedChanges = true;
                if (document.Session?.CanEditText == true)
                {
                    document.NeedsValidation = true;
                    document.Session.State = BinaryEditorDocumentState.InspectionOnly;
                    UpdateDocumentState(document, false);
                }
                UpdateDocumentHeader(document);
                UpdateFormTitle();
            };

            return document;
        }

        private void AddDocument(EditorDocument document)
        {
            var content = new DockPanel();
            document.InspectionText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 5) };
            document.InspectionBanner = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FFF2CC")),
                BorderBrush = new SolidColorBrush(Color.Parse("#D6B656")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = document.InspectionText
            };
            DockPanel.SetDock(document.InspectionBanner, Dock.Top);
            content.Children.Add(document.InspectionBanner);
            content.Children.Add(document.Editor);
            document.Tab = new TabItem { Content = content, Tag = document };
            document.Tab.Header = CreateTabHeader(document);

            m_documents.Add(document);
            g_Tabs.Items.Add(document.Tab);
            g_Tabs.SelectedItem = document.Tab;
            UpdateDocumentHeader(document);
            UpdateFormTitle();
            UpdateStatusText(document);
            UpdateDocumentState(document, false);
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
            UpdateCommandState();
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
            document.Editor.TextArea.TextView.BackgroundRenderers.Remove(document.ListGroupSeparatorRenderer);
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

        private async Task DisplayOpenFolderDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open Navigation Folder",
                AllowMultiple = false
            });

            if (folders.Count == 0)
                return;

            string path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                SetNavigationRoot(path);
        }

        private void SetNavigationRoot(string path)
        {
            m_navigationRoot = Path.GetFullPath(path);
            RefreshNavigationTree();
            SaveNavigationSettings();
        }

        private void RefreshNavigationTree()
        {
            g_NavTree.Items.Clear();

            if (string.IsNullOrWhiteSpace(m_navigationRoot) || !Directory.Exists(m_navigationRoot))
            {
                UpdateNavigationStatus();
                return;
            }

            try
            {
                DirectoryInfo root = new DirectoryInfo(m_navigationRoot);
                TreeViewItem rootItem = CreateNavigationItem(
                    root.Name,
                    GetIconUri("icons8-home-48.png"),
                    NavigationEntry.ForDirectory(root.FullName));
                rootItem.IsExpanded = true;
                PopulateDirectoryItem(rootItem, root);
                g_NavTree.Items.Add(rootItem);
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Refresh Navigation", ex.Message);
            }

            UpdateNavigationStatus();
        }

        private void PopulateDirectoryItem(TreeViewItem parentItem, DirectoryInfo directory)
        {
            DirectoryInfo[] directories;
            FileInfo[] files;
            try
            {
                directories = directory.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                files = directory.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch
            {
                return;
            }

            foreach (DirectoryInfo childDirectory in directories)
            {
                TreeViewItem childItem = CreateNavigationItem(
                    childDirectory.Name,
                    GetIconUri("icons8-folder-48.png"),
                    NavigationEntry.ForDirectory(childDirectory.FullName));
                PopulateDirectoryItem(childItem, childDirectory);
                parentItem.Items.Add(childItem);
            }

            foreach (FileInfo file in files)
            {
                if (IsJamArchive(file))
                {
                    TreeViewItem archiveItem = CreateNavigationItem(
                        file.Name,
                        GetFileTypeIconUri("JAM"),
                        NavigationEntry.ForJamArchive(file.FullName));
                    PopulateJamArchiveItem(archiveItem, file.FullName);
                    parentItem.Items.Add(archiveItem);
                }
                else if (IsSupportedEditableFileName(file.Name))
                {
                    parentItem.Items.Add(CreateNavigationItem(
                        file.Name,
                        GetFileTypeIconUri(GetFormatFromFileName(file.Name)),
                        NavigationEntry.ForFile(file.FullName)));
                }
            }
        }

        private void PopulateJamArchiveItem(TreeViewItem archiveItem, string archivePath)
        {
            try
            {
                JAM archive = new JAM(archivePath);
                Dictionary<string, TreeViewItem> directories = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);

                foreach (string directory in archive.Directories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    EnsureJamDirectoryItem(archiveItem, directories, archivePath, directory);

                foreach (JAMFile file in archive.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
                {
                    if (!IsSupportedEditableFileName(file.Name))
                        continue;

                    string parentPath = GetArchiveParentPath(file.Path);
                    TreeViewItem parentItem = string.IsNullOrEmpty(parentPath)
                        ? archiveItem
                        : EnsureJamDirectoryItem(archiveItem, directories, archivePath, parentPath);

                    parentItem.Items.Add(CreateNavigationItem(
                        file.Name,
                        GetFileTypeIconUri(GetFormatFromFileName(file.Name)),
                        NavigationEntry.ForJamFile(archivePath, file.Path)));
                }
            }
            catch (Exception ex)
            {
                archiveItem.Items.Add(CreateNavigationItem(
                    "Unable to read archive: " + ex.Message,
                    GetIconUri("icons8-file-48.png"),
                    NavigationEntry.ForMessage(archivePath)));
            }
        }

        private TreeViewItem EnsureJamDirectoryItem(TreeViewItem archiveItem, Dictionary<string, TreeViewItem> directories, string archivePath, string directoryPath)
        {
            if (directories.TryGetValue(directoryPath, out TreeViewItem existing))
                return existing;

            string parentPath = GetArchiveParentPath(directoryPath);
            TreeViewItem parentItem = string.IsNullOrEmpty(parentPath)
                ? archiveItem
                : EnsureJamDirectoryItem(archiveItem, directories, archivePath, parentPath);

            string name = Path.GetFileName(directoryPath.Replace('/', Path.DirectorySeparatorChar));
            TreeViewItem item = CreateNavigationItem(
                name,
                GetIconUri("icons8-folder-48.png"),
                NavigationEntry.ForJamDirectory(archivePath, directoryPath));
            parentItem.Items.Add(item);
            directories[directoryPath] = item;
            return item;
        }

        private TreeViewItem CreateNavigationItem(string text, string iconUri, NavigationEntry entry)
        {
            Image icon = new Image
            {
                Source = GetIcon(iconUri),
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            StackPanel header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };
            header.Children.Add(icon);
            header.Children.Add(label);

            TreeViewItem item = new TreeViewItem { Header = header, Tag = entry };
            item.DoubleTapped += NavItem_DoubleTapped;
            return item;
        }

        private Bitmap GetIcon(string uri)
        {
            if (!m_iconCache.TryGetValue(uri, out Bitmap bitmap))
            {
                bitmap = new Bitmap(AssetLoader.Open(new Uri(uri)));
                m_iconCache[uri] = bitmap;
            }
            return bitmap;
        }

        private static string GetIconUri(string fileName)
            => "avares://LR1BinaryEditor/Assets/AppIcons/" + fileName;

        private static string GetFileTypeIconUri(string format)
        {
            string normalized = NormalizeFormat(format);
            if (string.IsNullOrWhiteSpace(normalized))
                return GetIconUri("icons8-file-48.png");
            return "avares://LR1BinaryEditor/Assets/FileTypes/filetype-" + normalized.ToLowerInvariant() + ".png";
        }

        private static string GetArchiveParentPath(string archivePath)
        {
            int slash = archivePath.Replace('\\', '/').LastIndexOf('/');
            return slash < 0 ? string.Empty : archivePath.Substring(0, slash).Replace('\\', '/');
        }

        private void NavTree_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateNavigationStatus();
        }

        private void NavItem_DoubleTapped(object sender, TappedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Tag is NavigationEntry entry)
            {
                if (entry.Kind == NavigationEntryKind.File)
                    Open(entry.Path);
                else if (entry.Kind == NavigationEntryKind.JamFile)
                    OpenJamEntry(entry.JamArchivePath, entry.JamEntryPath);
                e.Handled = true;
            }
        }

        private void UpdateNavigationStatus()
        {
            if (g_NavStatus == null)
                return;

            NavigationEntry selected = GetSelectedNavigationEntry();
            if (selected != null)
            {
                g_NavStatus.Text = selected.GetStatusText();
                return;
            }

            g_NavStatus.Text = string.IsNullOrWhiteSpace(m_navigationRoot)
                ? "No folder selected"
                : m_navigationRoot;
        }

        private NavigationEntry GetSelectedNavigationEntry()
            => (g_NavTree?.SelectedItem as TreeViewItem)?.Tag as NavigationEntry;

        private void UpdateNavigationPaneLayout()
        {
            if (g_NavigationPane == null)
                return;

            g_MenuNavigationPane.Header = m_navigationPaneVisible
                ? "[x] Show _Navigation Pane"
                : "Show _Navigation Pane";
            g_MenuNavigationPaneRight.Header = m_navigationPaneRight
                ? "[x] Navigation Pane on _Right"
                : "Navigation Pane on _Right";
            g_NavigationPane.IsVisible = m_navigationPaneVisible;
            g_NavigationSplitter.IsVisible = m_navigationPaneVisible;

            if (!m_navigationPaneVisible)
            {
                g_WorkArea.ColumnDefinitions[0].Width = new GridLength(0);
                g_WorkArea.ColumnDefinitions[1].Width = new GridLength(0);
                g_WorkArea.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(g_Tabs, 0);
                Grid.SetColumnSpan(g_Tabs, 3);
                return;
            }

            g_WorkArea.ColumnDefinitions[1].Width = new GridLength(5);
            Grid.SetColumnSpan(g_Tabs, 1);

            if (m_navigationPaneRight)
            {
                g_WorkArea.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                g_WorkArea.ColumnDefinitions[2].Width = new GridLength(260);
                Grid.SetColumn(g_Tabs, 0);
                Grid.SetColumn(g_NavigationSplitter, 1);
                Grid.SetColumn(g_NavigationPane, 2);
                g_NavigationPane.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            else
            {
                g_WorkArea.ColumnDefinitions[0].Width = new GridLength(260);
                g_WorkArea.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(g_NavigationPane, 0);
                Grid.SetColumn(g_NavigationSplitter, 1);
                Grid.SetColumn(g_Tabs, 2);
                g_NavigationPane.BorderThickness = new Thickness(0, 0, 1, 0);
            }
        }

        private async Task SaveAllChangedDocumentsAsync()
        {
            EditorDocument[] changedDocuments = m_documents.Where(document => document.UnsavedChanges).ToArray();
            if (changedDocuments.Length == 0)
            {
                await ShowMessageAsync("Save All", "There are no pending changes to save.");
                return;
            }

            int choice = await ShowDialogInternal(
                "Save All",
                string.Format("Save changes to {0} open file(s)?", changedDocuments.Length),
                new[] { "Save All", "Cancel" });

            if (choice != 0)
                return;

            foreach (EditorDocument document in changedDocuments)
            {
                g_Tabs.SelectedItem = document.Tab;
                bool saved = await SaveDocumentOrShowDialog(document);
                if (!saved)
                    break;
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
                if (IsJamArchive(fi))
                {
                    if (!string.IsNullOrWhiteSpace(fi.DirectoryName))
                        SetNavigationRoot(fi.DirectoryName);
                    return;
                }

                BinaryEditorDocumentSession session = m_documentService.Open(fullPath);
                LoadEditorFromSession(session, fullPath, false);
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Open Binary File", ex.Message);
            }
        }

        private void OpenJamEntry(string archivePath, string entryPath)
        {
            try
            {
                JamArchiveSession session = GetJamSession(archivePath);
                string extractedPath = session.GetExtractedPath(entryPath);
                Open(extractedPath, new JamEntrySource(archivePath, entryPath, session.TempRoot));
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Open JAM Entry", ex.Message);
            }
        }

        private void Open(string filePath, JamEntrySource jamSource)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                EditorDocument existing = m_documents.FirstOrDefault(document =>
                    document.JamSource != null
                    && string.Equals(document.JamSource.ArchivePath, jamSource.ArchivePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(document.JamSource.EntryPath, jamSource.EntryPath, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    g_Tabs.SelectedItem = existing.Tab;
                    return;
                }

                FileInfo fi = new FileInfo(fullPath);
                BinaryEditorDocumentSession session = m_documentService.Open(fullPath, fi.Name);
                EditorDocument document = LoadEditorFromSession(session, fullPath, false);
                document.JamSource = jamSource;
                UpdateStatusText(document);
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Open JAM Entry", ex.Message);
            }
        }

        private JamArchiveSession GetJamSession(string archivePath)
        {
            string fullPath = Path.GetFullPath(archivePath);
            if (m_jamSessions.TryGetValue(fullPath, out JamArchiveSession session))
                return session;

            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "LR1BinaryEditor",
                "JAM",
                Path.GetFileNameWithoutExtension(fullPath) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            JAM archive = new JAM(fullPath);
            archive.Extract(tempRoot, true);
            session = new JamArchiveSession(fullPath, tempRoot);
            m_jamSessions[fullPath] = session;
            return session;
        }

        private Task<bool> SaveCurrentOrShowDialog()
        {
            EditorDocument document = CurrentDocument;
            return document == null ? Task.FromResult(false) : SaveDocumentOrShowDialog(document);
        }

        private Task<bool> SaveDocumentOrShowDialog(EditorDocument document)
        {
            if (document.NeedsValidation && !ValidateDocument(document, true))
                return Task.FromResult(false);
            if (document.Session != null && !document.Session.CanWrite)
            {
                _ = ShowMessageAsync("Write Disabled", document.Session.Diagnostic ?? "Validate the document successfully before writing.");
                return Task.FromResult(false);
            }
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
            if (document.NeedsValidation && !ValidateDocument(document, true))
                return false;
            if (document.Session != null && !document.Session.CanWrite)
            {
                await ShowMessageAsync("Write Disabled", document.Session.Diagnostic ?? "Validate the document successfully before writing.");
                return false;
            }
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
                string format = GetFormatFromFileName(Path.GetFileName(filePath));
                if (document.Session == null || !string.Equals(document.Session.Format, format, StringComparison.OrdinalIgnoreCase))
                    document.Session = m_documentService.CreateCandidate(format, Path.GetFileName(filePath), document.Editor.Text);
                m_documentService.Write(document.Session, document.Editor.Text, filePath);

                if (document.JamSource != null
                    && string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(document.FilePath), StringComparison.OrdinalIgnoreCase))
                {
                    JAM archive = JAM.FromDirectory(document.JamSource.TempRoot);
                    archive.Write(document.JamSource.ArchivePath);
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Save Binary File", ex.Message);
                return false;
            }
            finally
            {
                document.Editor.IsReadOnly = document.Session != null && !document.Session.CanEditText;
            }

            document.FileName = Path.GetFileName(filePath);
            document.FilePath = filePath;
            document.CurrentFormat = GetFormatFromFileName(document.FileName);
            if (document.JamSource != null
                && !string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(document.JamSource.GetExtractedPath()), StringComparison.OrdinalIgnoreCase))
            {
                document.JamSource = null;
            }
            document.UnsavedChanges = false;
            UpdateDocumentHeader(document);
            UpdateFormTitle();
            UpdateStatusText(document);
            UpdateDocumentState(document, false);
            return true;
        }

        private EditorDocument LoadEditorFromSession(BinaryEditorDocumentSession session, string filePath, bool markDirty)
        {
            EditorDocument document = CreateDocument(session.FileName, filePath, session.Format, markDirty);
            document.Session = session;
            document.Editor.IsReadOnly = !session.CanEditText;
            SetEditorText(document, session.Text);
            AddDocument(document);
            return document;
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
                UpdateListGroupSeparators(document);
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

        private void ValidateCurrentDocument(bool showResult)
        {
            EditorDocument document = CurrentDocument;
            if (document != null) ValidateDocument(document, showResult);
        }

        private bool ValidateDocument(EditorDocument document, bool showResult)
        {
            if (document?.Session == null || !document.Session.CanEditText)
            {
                if (showResult) _ = ShowMessageAsync("Validate / Reparse", "This document is an exact raw/opaque byte view and is not routed through the token compiler.");
                return document?.Session?.CanWrite == true;
            }

            m_documentService.Validate(document.Session, document.Editor.Text);
            document.NeedsValidation = false;
            UpdateDocumentState(document, true);
            if (showResult)
            {
                string message = document.Session.CanWrite
                    ? "LibLR1 reparsed the candidate completely. Writer-backed commands are enabled.\n\n" + document.Session.DecompressedDiff
                    : document.Session.Diagnostic ?? "The candidate is still not writable.";
                _ = ShowMessageAsync("Validate / Reparse", message);
            }
            return document.Session.CanWrite;
        }

        private void UpdateDocumentState(EditorDocument document, bool navigateIssue)
        {
            if (document?.InspectionBanner == null) return;
            BinaryEditorDocumentSession session = document.Session;
            bool show = session != null && (session.State != BinaryEditorDocumentState.ValidSemantic || document.NeedsValidation);
            document.InspectionBanner.IsVisible = show;
            if (show)
            {
                string state = document.NeedsValidation ? "Candidate needs validation" : session.State.ToString();
                string encoding = session.Encoding.ToString();
                string evidence = string.IsNullOrWhiteSpace(session.EvidenceStatus) ? "UNRESOLVED" : session.EvidenceStatus;
                document.InspectionText.Text = $"{state} | {encoding} | evidence: {evidence}\n{session.Diagnostic ?? "Exact source bytes are retained; writer-backed commands remain disabled."}";
            }
            document.Editor.IsReadOnly = session != null && !session.CanEditText;
            if (navigateIssue && session?.Issue?.DecompressedOffset is long offset)
            {
                int textOffset = Math.Min(document.Editor.Document.TextLength, session.OffsetMap.FindTextOffset(offset));
                document.Editor.TextArea.Caret.Offset = textOffset;
                document.Editor.ScrollToLine(document.Editor.Document.GetLineByOffset(textOffset).LineNumber);
                document.Editor.Focus();
            }
            UpdateStatusText(document);
            UpdateCommandState();
        }

        private void UpdateCommandState()
        {
            EditorDocument document = CurrentDocument;
            bool writable = document != null && !document.NeedsValidation && (document.Session == null || document.Session.CanWrite);
            bool exportable = document?.Session?.CanExportJson == true && !document.NeedsValidation;
            bool tokenized = document?.Session?.CanEditText == true;
            g_MenuSave.IsEnabled = writable;
            g_MenuSaveAs.IsEnabled = writable;
            g_ButtonSave.IsEnabled = writable;
            g_MenuExportJson.IsEnabled = exportable;
            g_ButtonExportJson.IsEnabled = exportable;
            g_MenuValidate.IsEnabled = tokenized;
            g_ButtonValidate.IsEnabled = tokenized;
        }

        private void ShowBinaryDiff()
        {
            EditorDocument document = CurrentDocument;
            if (document == null) return;
            if (document.Session?.CanEditText == true) ValidateDocument(document, false);
            BinaryDiffSummary source = document.Session?.SourceDiff ?? BinaryEditorDocumentService.Compare(document.Session?.SourceData, document.Session?.SourceData);
            BinaryDiffSummary expanded = document.Session?.DecompressedDiff;
            string message = "Source versus canonical candidate\n" + source + (expanded == null ? string.Empty : "\n\nExpanded/token bytes\n" + expanded);
            _ = ShowMessageAsync("Binary Diff", message);
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

        private static bool IsJamArchive(FileInfo file)
            => file != null && file.Extension.Equals(".JAM", StringComparison.OrdinalIgnoreCase);

        private static bool IsSupportedEditableFileName(string fileName)
        {
            string format = NormalizeFormat(Path.GetExtension(fileName));
            if (string.IsNullOrWhiteSpace(format) || format == "JAM")
                return false;

            if (!LibLR1.Schema.SchemaStructureProvider.Formats.Contains(format, StringComparer.OrdinalIgnoreCase))
                return false;

            return true;
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

        private void UpdateListGroupSeparators(EditorDocument document)
        {
            if (document?.ListGroupSeparatorRenderer == null)
                return;

            document.ListGroupSeparatorRenderer.UpdateText(document.Editor.Text);
            document.Editor.TextArea.TextView.InvalidateVisual();
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
                if (document.NeedsValidation && !ValidateDocument(document, true)) return;
                if (document.Session?.CanExportJson != true) throw new InvalidOperationException(document.Session?.Diagnostic ?? "JSON export requires a validated semantic LibLR1 document.");
                if (!LibLR1JsonBridge.TryExportJson(format, document.FileName, document.Session.Inspection.Document, document.Session.CandidateData ?? document.Session.SourceData, out string json, out string error))
                    throw new InvalidOperationException(error);
                File.WriteAllText(outputPath, json, Encoding.UTF8);
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

                BinaryEditorDocumentSession session = m_documentService.ImportJson(imported);
                LoadEditorFromSession(session, null, true);
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

            BinaryEditorDocumentSession session = document.Session;
            if (session == null)
            {
                g_LblBuild.Text = string.Format("{0} | {1:N0} characters", m_versionText, document.Editor.Text?.Length ?? 0);
                return;
            }
            g_LblBuild.Text = string.Format(
                "{0} | {1} | {2} | {3} | source {4:N0} bytes | expanded {5:N0} bytes",
                m_versionText,
                session.State,
                session.Encoding,
                session.EvidenceStatus ?? "UNRESOLVED",
                session.SourceData?.Length ?? 0,
                session.DecompressedData?.Length ?? 0);
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
            return Path.Combine(GetSettingsDirectory(), "syntax-colors.json");
        }

        private void LoadNavigationSettings()
        {
            string path = GetNavigationSettingsPath();
            if (!File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                NavigationSettings settings = JsonSerializer.Deserialize<NavigationSettings>(json);
                if (settings == null)
                    return;

                m_navigationPaneVisible = settings.NavigationPaneVisible;
                m_navigationPaneRight = settings.NavigationPaneRight;
                if (!string.IsNullOrWhiteSpace(settings.NavigationRoot))
                    m_navigationRoot = settings.NavigationRoot;
            }
            catch
            {
                // Ignore invalid user navigation config and keep defaults.
            }
        }

        private void SaveNavigationSettings()
        {
            try
            {
                string path = GetNavigationSettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var settings = new NavigationSettings
                {
                    NavigationPaneVisible = m_navigationPaneVisible,
                    NavigationPaneRight = m_navigationPaneRight,
                    NavigationRoot = m_navigationRoot
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Navigation preferences are convenience state; ignore persistence failures.
            }
        }

        private static string GetNavigationSettingsPath()
        {
            return Path.Combine(GetSettingsDirectory(), "navigation.json");
        }

        private static string GetSettingsDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "LR1BinaryEditor");
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
            foreach (string format in LibLR1.Schema.SchemaStructureProvider.Formats)
            {
                Util.FormatDescriptions.TryGetValue(format, out string description);
                types.Add(CreateFileType(format, description));
            }
            types.Add(FilePickerFileTypes.All);
            return types.ToArray();
        }

        private static FilePickerFileType CreateAllSupportedFileType()
        {
            string[] patterns = LibLR1.Schema.SchemaStructureProvider.Formats
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
        private void BtnValidate_Click(object sender, RoutedEventArgs e) => ValidateCurrentDocument(true);
        private void BtnBinaryDiff_Click(object sender, RoutedEventArgs e) => ShowBinaryDiff();
        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e) => _ = DisplayOpenFolderDialog();
        private void BtnSaveAll_Click(object sender, RoutedEventArgs e) => _ = SaveAllChangedDocumentsAsync();
        private void BtnRefreshNavigation_Click(object sender, RoutedEventArgs e) => RefreshNavigationTree();
        private void MenuNavigationPane_Click(object sender, RoutedEventArgs e)
        {
            m_navigationPaneVisible = !m_navigationPaneVisible;
            UpdateNavigationPaneLayout();
            SaveNavigationSettings();
        }
        private void MenuNavigationPaneRight_Click(object sender, RoutedEventArgs e)
        {
            m_navigationPaneRight = !m_navigationPaneRight;
            UpdateNavigationPaneLayout();
            SaveNavigationSettings();
        }
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
            public bool NeedsValidation { get; set; }
            public JamEntrySource JamSource { get; set; }
            public BinaryEditorDocumentSession Session { get; set; }
            public TextEditor Editor { get; set; }
            public TabItem Tab { get; set; }
            public Border HeaderBorder { get; set; }
            public TextBlock HeaderText { get; set; }
            public Border InspectionBanner { get; set; }
            public TextBlock InspectionText { get; set; }
            public FoldingManager FoldingManager { get; set; }
            public ListGroupSeparatorRenderer ListGroupSeparatorRenderer { get; set; }
            public BraceFoldingStrategy BraceFoldingStrategy { get; } = new BraceFoldingStrategy();
        }

        private enum SaveChangesChoice
        {
            Save,
            DontSave,
            Cancel
        }

        private sealed class NavigationEntry
        {
            public NavigationEntryKind Kind { get; private set; }
            public string Path { get; private set; }
            public string JamArchivePath { get; private set; }
            public string JamEntryPath { get; private set; }

            public static NavigationEntry ForDirectory(string path)
                => new NavigationEntry { Kind = NavigationEntryKind.Directory, Path = path };

            public static NavigationEntry ForFile(string path)
                => new NavigationEntry { Kind = NavigationEntryKind.File, Path = path };

            public static NavigationEntry ForJamArchive(string archivePath)
                => new NavigationEntry { Kind = NavigationEntryKind.JamArchive, Path = archivePath, JamArchivePath = archivePath };

            public static NavigationEntry ForJamDirectory(string archivePath, string entryPath)
                => new NavigationEntry { Kind = NavigationEntryKind.JamDirectory, Path = archivePath, JamArchivePath = archivePath, JamEntryPath = entryPath };

            public static NavigationEntry ForJamFile(string archivePath, string entryPath)
                => new NavigationEntry { Kind = NavigationEntryKind.JamFile, Path = archivePath, JamArchivePath = archivePath, JamEntryPath = entryPath };

            public static NavigationEntry ForMessage(string path)
                => new NavigationEntry { Kind = NavigationEntryKind.Message, Path = path };

            public string GetStatusText()
            {
                switch (Kind)
                {
                    case NavigationEntryKind.Directory:
                    case NavigationEntryKind.File:
                    case NavigationEntryKind.JamArchive:
                        return Path ?? "";
                    case NavigationEntryKind.JamDirectory:
                    case NavigationEntryKind.JamFile:
                        return string.Format("{0} | {1}", JamArchivePath, JamEntryPath);
                    default:
                        return Path ?? "";
                }
            }
        }

        private enum NavigationEntryKind
        {
            Directory,
            File,
            JamArchive,
            JamDirectory,
            JamFile,
            Message
        }

        private sealed class JamEntrySource
        {
            public JamEntrySource(string archivePath, string entryPath, string tempRoot)
            {
                ArchivePath = Path.GetFullPath(archivePath);
                EntryPath = entryPath.Replace('\\', '/');
                TempRoot = Path.GetFullPath(tempRoot);
            }

            public string ArchivePath { get; }
            public string EntryPath { get; }
            public string TempRoot { get; }

            public string GetExtractedPath()
            {
                string path = TempRoot;
                foreach (string component in EntryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    path = Path.Combine(path, component);
                return Path.GetFullPath(path);
            }
        }

        private sealed class JamArchiveSession
        {
            public JamArchiveSession(string archivePath, string tempRoot)
            {
                ArchivePath = Path.GetFullPath(archivePath);
                TempRoot = Path.GetFullPath(tempRoot);
            }

            public string ArchivePath { get; }
            public string TempRoot { get; }

            public string GetExtractedPath(string entryPath)
            {
                string normalized = entryPath.Replace('\\', '/');
                string path = TempRoot;
                foreach (string component in normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    path = Path.Combine(path, component);

                string fullPath = Path.GetFullPath(path);
                string rootPrefix = TempRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? TempRoot
                    : TempRoot + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("JAM extraction path escapes the temporary archive directory.");
                return fullPath;
            }
        }

        private sealed class NavigationSettings
        {
            public bool NavigationPaneVisible { get; set; } = true;
            public bool NavigationPaneRight { get; set; }
            public string NavigationRoot { get; set; }
        }
    }
}
