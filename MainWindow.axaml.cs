using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    public partial class MainWindow : Window
    {
        public BundleWorkspace Workspace { get; }
        public AssetsManager am { get => Workspace.am; }
        public BundleFileInstance BundleInst { get => Workspace.BundleInst; }

        //private Dictionary<string, BundleReplacer> newFiles;
        private bool changesUnsaved; // sets false after saving
        private bool changesMade; // stays true even after saving
        private bool ignoreCloseEvent;
        private List<InfoWindow> openInfoWindows;

        //public ObservableCollection<ComboBoxItem> comboItems;

        public MainWindow()
        {
            // has to happen BEFORE initcomponent
            Workspace = new BundleWorkspace();
            Initialized += MainWindow_Initialized;

            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            //generated events
            menuOpen.Click += MenuOpen_Click;
            menuLoadPackageFile.Click += MenuLoadPackageFile_Click;
            menuClose.Click += MenuClose_Click;
            menuSave.Click += MenuSave_Click;
            menuSaveAs.Click += MenuSaveAs_Click;
            menuCompress.Click += MenuCompress_Click;
            menuExit.Click += MenuExit_Click;
            menuToggleDarkTheme.Click += MenuToggleDarkTheme_Click;
            menuToggleCpp2Il.Click += MenuToggleCpp2Il_Click;
            menuAbout.Click += MenuAbout_Click;
            //btnExport.Click += BtnExport_Click;
            //btnImport.Click += BtnImport_Click;
            //btnRemove.Click += BtnRemove_Click;
            btnInfo.Click += BtnInfo_Click;
            //btnExportAll.Click += BtnExportAll_Click;
            //btnImportAll.Click += BtnImportAll_Click;
            //btnRename.Click += BtnRename_Click;
            Closing += MainWindow_Closing;
            btnOpen.Click += BtnOpen_Click;
            btnPatch.Click += BtnPatch_Click;

            changesUnsaved = false;
            changesMade = false;
            ignoreCloseEvent = false;
            openInfoWindows = new List<InfoWindow>();

            AddHandler(DragDrop.DropEvent, Drop);

            ThemeHandler.UseDarkTheme = ConfigurationManager.Settings.UseDarkTheme;
        }

        private async void BtnPatch_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                Title = "Open Catalog JSON file",
                FileTypeFilter = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("JSON") { Patterns = new List<string>() { "catalog*.json" } }
                },
                AllowMultiple = false
            });

            string[] selectedFilePaths = Extensions.GetOpenFileDialogFiles(selectedFiles);
            if (selectedFilePaths.Length == 0)
                return;

            try { 
                //1. Run the .bat command to Patch JSON
                string filename = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\patcher\\PatchCRC.bat";
                string parameters = "\"" + selectedFilePaths[0].ToString() + "\"";
                //Process.Start(filename, parameters);
                var process = Process.Start(filename, parameters);
                process.WaitForExit();
                //Loop to see if .patched file exists, check every 5 seconds(?)
                //3. Rename and move patched file
                //File.Delete(patcherFilePath);
                string patchedfile = selectedFilePaths[0].ToString() + ".patched";
                while (true)
                {
                    if (File.Exists(patchedfile))
                    {
                        break;
                    }
                    // code here
                    Thread.Sleep(5000);
                }

                string appDataPath = @"%APPDATA%\..\LocalLow\Second Dinner\Snap\com.unity.addressables\";
                File.Move(patchedfile, Environment.ExpandEnvironmentVariables(appDataPath) + selectedFiles[0].Name.ToString(), true); 
            }
            catch (Exception ex)
            {
                await MessageBoxUtil.ShowDialog(this,
                    "Write exception", "There was a problem while writing the file:\n" + ex.ToString());
            }
        }

        private async void BtnOpen_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                Title = "Open assets or bundle file",
                FileTypeFilter = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("All files") { Patterns = new List<string>() { "*.*" } }
                },
                AllowMultiple = true
            });

            string[] selectedFilePaths = Extensions.GetOpenFileDialogFiles(selectedFiles);
            if (selectedFilePaths.Length == 0)
                return;

            OpenFiles(selectedFilePaths);
        }

        private async void MainWindow_Initialized(object? sender, EventArgs e)
        {
            string classDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");
            if (File.Exists(classDataPath))
            {
                am.LoadClassPackage(classDataPath);
            }
            else
            {
                await MessageBoxUtil.ShowDialog(this, "Error", "Missing classdata.tpk by exe.\nPlease make sure it exists.");
                Close();
                Environment.Exit(1);
            }
        }

        async void OpenFiles(string[] files)
        {
            string selectedFile = files[0];

            DetectedFileType fileType = AssetBundleDetector.DetectFileType(selectedFile);

            await CloseAllFiles();

            // can you even have split bundles?
            if (fileType != DetectedFileType.Unknown)
            {
                if (selectedFile.EndsWith(".split0"))
                {
                    string? splitFilePath = await AskLoadSplitFile(selectedFile);
                    if (splitFilePath == null)
                        return;
                    else
                        selectedFile = splitFilePath;
                }
            }

            if (fileType == DetectedFileType.AssetsFile)
            {
                AssetsFileInstance fileInst = am.LoadAssetsFile(selectedFile, true);

                if (!LoadOrAskTypeData(fileInst))
                    return;

                List<AssetsFileInstance> fileInstances = new List<AssetsFileInstance>();
                fileInstances.Add(fileInst);

                if (files.Length > 1)
                {
                    for (int i = 1; i < files.Length; i++)
                    {
                        string otherSelectedFile = files[i];
                        DetectedFileType otherFileType = AssetBundleDetector.DetectFileType(otherSelectedFile);
                        if (otherFileType == DetectedFileType.AssetsFile)
                        {
                            try
                            {
                                fileInstances.Add(am.LoadAssetsFile(otherSelectedFile, true));
                            }
                            catch
                            {
                                // no warning if the file didn't load but was detected as an assets file
                                // this is so you can select the entire _Data folder and any false positives
                                // don't message the user since it's basically a given
                            }
                        }
                    }
                }

                // shouldn't be possible but just in case
                if (openInfoWindows.Count > 0)
                {
                    await MessageBoxUtil.ShowDialog(this,
                        "Warning", "You cannot open two info windows at the same time. " +
                                   "Consider opening two separate UABEA windows if you " +
                                   "want two different games' files open at once.");

                    return;
                }

                InfoWindow info = new InfoWindow(am, fileInstances, false);
                info.Show();
                info.Closing += (sender, _) => {
                    if (sender == null)
                        return;

                    InfoWindow window = (InfoWindow)sender;
                    openInfoWindows.Remove(window);
                };
                openInfoWindows.Add(info);
            }
            else if (fileType == DetectedFileType.BundleFile)
            {
                BundleFileInstance bundleInst = am.LoadBundleFile(selectedFile, false);

                if (AssetBundleUtil.IsBundleDataCompressed(bundleInst.file))
                {
                    AskLoadCompressedBundle(bundleInst);
                }
                else
                {
                    LoadBundle(bundleInst);
                }
                //OPEN INFO RIGHT AWAY
                btnInfo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                await MessageBoxUtil.ShowDialog(this, "Error", "This doesn't seem to be an assets file or bundle.");
            }
        }

        void Drop(object? sender, DragEventArgs e)
        {
            string[] files = e.Data.GetFileNames().ToArray();

            if (files == null || files.Length == 0)
                return;

            OpenFiles(files);
        }

        private async void MenuOpen_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                Title = "Open assets or bundle file",
                FileTypeFilter = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("All files") { Patterns = new List<string>() { "*.*" } }
                },
                AllowMultiple = true
            });

            string[] selectedFilePaths = Extensions.GetOpenFileDialogFiles(selectedFiles);
            if (selectedFilePaths.Length == 0)
                return;

            OpenFiles(selectedFilePaths);
        }

        private async void MenuLoadPackageFile_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                FileTypeFilter = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("UABE Mod Installer Package") { Patterns = new List<string>() { "*.emip" } }
                }
            });

            string[] selectedFilePaths = Extensions.GetOpenFileDialogFiles(selectedFiles);
            if (selectedFilePaths.Length == 0)
                return;

            string emipPath = selectedFilePaths[0];

            if (emipPath != null && emipPath != string.Empty)
            {
                AssetsFileReader r = new AssetsFileReader(File.OpenRead(emipPath)); //todo close this
                InstallerPackageFile emip = new InstallerPackageFile();
                emip.Read(r);

                LoadModPackageDialog dialog = new LoadModPackageDialog(emip, am);
                await dialog.ShowDialog(this);
            }
        }

        private void MenuAbout_Click(object? sender, RoutedEventArgs e)
        {
            About about = new About();
            about.ShowDialog(this);
        }

        private async void MenuSave_Click(object? sender, RoutedEventArgs e)
        {
            await AskForLocationAndSave(false);
        }

        private async void MenuSaveAs_Click(object? sender, RoutedEventArgs e)
        {
            await AskForLocationAndSave(true);
        }

        private async void MenuCompress_Click(object? sender, RoutedEventArgs e)
        {
            await AskForLocationAndCompress();
        }

        private async void MenuClose_Click(object? sender, RoutedEventArgs e)
        {
            await AskForSave();
            await CloseAllFiles();
        }

        private async void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            if (BundleInst == null)
                return;

            BundleWorkspaceItem? item = (BundleWorkspaceItem?)comboBox.SelectedItem;
            if (item == null)
                return;

            var selectedFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                Title = "Save as...",
                SuggestedFileName = item.Name
            });

            string? selectedFilePath = Extensions.GetSaveFileDialogFile(selectedFile);
            if (selectedFilePath == null)
                return;

            using FileStream fileStream = File.Open(selectedFilePath, FileMode.Create);

            Stream stream = item.Stream;
            stream.Position = 0;
            stream.CopyToCompat(fileStream, stream.Length);
        }

        private async void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            if (BundleInst != null)
            {
                var selectedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "Open",
                    FileTypeFilter = new List<FilePickerFileType>()
                    {
                        new FilePickerFileType("All files") { Patterns = new List<string>() { "*.*" } }
                    }
                });

                string[] selectedFilePaths = Extensions.GetOpenFileDialogFiles(selectedFiles);
                if (selectedFilePaths.Length == 0)
                    return;

                string file = selectedFilePaths[0];

                ImportSerializedDialog dialog = new ImportSerializedDialog();
                bool isSerialized = await dialog.ShowDialog<bool>(this);

                byte[] fileBytes = File.ReadAllBytes(file);
                string fileName = Path.GetFileName(file);

                MemoryStream stream = new MemoryStream(fileBytes);
                Workspace.AddOrReplaceFile(stream, fileName, isSerialized);

                SetBundleControlsEnabled(true, true);
                changesUnsaved = true;
                changesMade = true;
            }
        }

        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (BundleInst != null && comboBox.SelectedItem != null)
            {
                BundleWorkspaceItem? item = (BundleWorkspaceItem?)comboBox.SelectedItem;
                if (item == null)
                    return;

                string origName = item.OriginalName;
                string name = item.Name;
                item.IsRemoved = true;
                Workspace.RemovedFiles.Add(origName);
                Workspace.Files.Remove(item);
                Workspace.FileLookup.Remove(name);

                SetBundleControlsEnabled(true, Workspace.Files.Count > 0);

                changesUnsaved = true;
                changesMade = true;
            }
        }

        private async void BtnInfo_Click(object? sender, RoutedEventArgs e)
        {
            if (BundleInst == null)
                return;

            BundleWorkspaceItem? item = (BundleWorkspaceItem?)comboBox.SelectedItem;
            if (item == null)
                return;

            string name = item.Name;

            AssetBundleFile bundleFile = BundleInst.file;

            Stream assetStream = item.Stream;

            DetectedFileType fileType = AssetBundleDetector.DetectFileType(new AssetsFileReader(assetStream), 0);
            assetStream.Position = 0;

            if (fileType == DetectedFileType.AssetsFile)
            {
                string assetMemPath = Path.Combine(BundleInst.path, name);
                AssetsFileInstance fileInst = am.LoadAssetsFile(assetStream, assetMemPath, true);

                if (BundleInst != null && fileInst.parentBundle == null)
                    fileInst.parentBundle = BundleInst;

                if (!LoadOrAskTypeData(fileInst))
                    return;

                // don't check for info open here
                // we're assuming it's fine since two infos can
                // be opened from a bundle without problems

                InfoWindow info = new InfoWindow(am, new List<AssetsFileInstance> { fileInst }, true);
                info.Closing += InfoWindow_Closing;
                info.Show();
                openInfoWindows.Add(info);
            }
            else
            {
                if (item.IsSerialized)
                {
                    await MessageBoxUtil.ShowDialog(this,
                        "Error", "This doesn't seem to be a valid assets file, " +
                                 "although the asset is serialized. Maybe the " +
                                 "file got corrupted or is too new of a version?");
                }
                else
                {
                    await MessageBoxUtil.ShowDialog(this,
                        "Error", "This doesn't seem to be a valid assets file. " +
                                 "If you want to export a non-assets file, " +
                                 "use Export.");
                }
            }
        }

        private async void BtnExportAll_Click(object? sender, RoutedEventArgs e)
        {
            if (BundleInst == null)
                return;
            
            var selectedFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Select export directory"
            });

            string[]? selectedFolderPaths = Extensions.GetOpenFolderDialogFiles(selectedFolders);
            if (selectedFolderPaths.Length == 0)
                return;

            string dir = selectedFolderPaths[0];

            for (int i = 0; i < BundleInst.file.BlockAndDirInfo.DirectoryInfos.Length; i++)
            {
                AssetBundleDirectoryInfo dirInf = BundleInst.file.BlockAndDirInfo.DirectoryInfos[i];

                string bunAssetName = dirInf.Name;
                string bunAssetPath = Path.Combine(dir, bunAssetName);

                // create dirs if bundle contains / in path
                if (bunAssetName.Contains("\\") || bunAssetName.Contains("/"))
                {
                    string bunAssetDir = Path.GetDirectoryName(bunAssetPath);
                    if (!Directory.Exists(bunAssetDir))
                    {
                        Directory.CreateDirectory(bunAssetDir);
                    }    
                }

                using FileStream fileStream = File.Open(bunAssetPath, FileMode.Create);

                AssetsFileReader bundleReader = BundleInst.file.DataReader;
                bundleReader.Position = dirInf.Offset;
                bundleReader.BaseStream.CopyToCompat(fileStream, dirInf.DecompressedSize);
            }
        }

        private async void BtnImportAll_Click(object? sender, RoutedEventArgs e)
        {
            if (BundleInst == null)
                return;

            var selectedFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Select import directory"
            });

            string[]? selectedFolderPaths = Extensions.GetOpenFolderDialogFiles(selectedFolders);
            if (selectedFolderPaths.Length == 0)
                return;

            string dir = selectedFolderPaths[0];

            foreach (string filePath in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(dir, filePath);
                relPath = relPath.Replace("\\", "/").TrimEnd('/');

                BundleWorkspaceItem? itemToReplace = Workspace.Files.FirstOrDefault(f => f.Name == relPath);
                if (itemToReplace != null)
                {
                    Workspace.AddOrReplaceFile(File.OpenRead(filePath), itemToReplace.Name, itemToReplace.IsSerialized);
                }
                else
                {
                    DetectedFileType type = AssetBundleDetector.DetectFileType(filePath);
                    bool isSerialized = type == DetectedFileType.AssetsFile;
                    Workspace.AddOrReplaceFile(File.OpenRead(filePath), relPath, isSerialized);
                }
            }

            changesUnsaved = true;
            changesMade = true;
        }

        private async void BtnRename_Click(object? sender, RoutedEventArgs e)
        {
            if (BundleInst == null)
                return;

            BundleWorkspaceItem? item = (BundleWorkspaceItem?)comboBox.SelectedItem;
            if (item == null)
                return;

            // if we rename twice, the "original name" is the current name
            RenameWindow window = new RenameWindow(item.Name);
            string newName = await window.ShowDialog<string>(this);
            if (newName == string.Empty)
                return;

            Workspace.RenameFile(item.Name, newName);

            // reload the text in the selected item preview
            // why not just use propertychangeevent? it's because getting
            // events working and the fact that displaymemberpath isn't
            // supported means more trouble than it's worth. this hack is
            // good enough, despite being jank af.
            Workspace.Files.Add(null);
            comboBox.SelectedItem = null;
            comboBox.SelectedItem = item;
            Workspace.Files.Remove(null);

            changesUnsaved = true;
            changesMade = true;
        }

        private void MenuExit_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MenuToggleDarkTheme_Click(object? sender, RoutedEventArgs e)
        {
            ConfigurationManager.Settings.UseDarkTheme = !ConfigurationManager.Settings.UseDarkTheme;
            ThemeHandler.UseDarkTheme = ConfigurationManager.Settings.UseDarkTheme;
        }

        private async void MenuToggleCpp2Il_Click(object? sender, RoutedEventArgs e)
        {
            bool useCpp2Il = !ConfigurationManager.Settings.UseCpp2Il;
            ConfigurationManager.Settings.UseCpp2Il = useCpp2Il;

            await MessageBoxUtil.ShowDialog(this, "Note",
                $"Use Cpp2Il is set to: {useCpp2Il.ToString().ToLower()}");
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!changesUnsaved || ignoreCloseEvent)
            {
                e.Cancel = false;
                ignoreCloseEvent = false;
            }
            else
            {
                e.Cancel = true;
                ignoreCloseEvent = true;

                await AskForSave();
                Close(); // calling Close() triggers Closing() again
            }
        }

        private void InfoWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (sender == null)
                return;

            InfoWindow window = (InfoWindow)sender;
            openInfoWindows.Remove(window);

            if (window.Workspace.fromBundle && window.ChangedAssetsDatas != null)
            {
                List<Tuple<AssetsFileInstance, byte[]>> assetDatas = window.ChangedAssetsDatas;

                foreach (var tup in assetDatas)
                {
                    AssetsFileInstance fileInstance = tup.Item1;
                    byte[] assetData = tup.Item2;

                    // remember selected index, when we replace the file it unselects the combobox item
                    int comboBoxSelectedIndex = comboBox.SelectedIndex;

                    string assetName = Path.GetFileName(fileInstance.path);
                    Workspace.AddOrReplaceFile(new MemoryStream(assetData), assetName, true);
                    // unload it so the new version is reloaded when we reopen it
                    am.UnloadAssetsFile(fileInstance.path);

                    // reselect the combobox item
                    comboBox.SelectedIndex = comboBoxSelectedIndex;
                }

                if (assetDatas.Count > 0)
                {
                    changesUnsaved = true;
                    changesMade = true;
                }
            }
        }

        private bool LoadOrAskTypeData(AssetsFileInstance fileInst)
        {
            string uVer = fileInst.file.Metadata.UnityVersion;
            if (uVer == "0.0.0" && fileInst.parentBundle != null)
            {
                uVer = fileInst.parentBundle.file.Header.EngineVersion;
            }

            am.LoadClassDatabaseFromPackage(uVer);
            return true;
        }

        private async Task AskForLocationAndSave(bool saveAs)
        {
            if (changesUnsaved && BundleInst != null)
            {
                if (saveAs)
                {
                    var selectedFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
                    {
                        Title = "Save as..."
                    });

                    string? selectedFilePath = Extensions.GetSaveFileDialogFile(selectedFile);
                    if (selectedFilePath == null)
                        return;

                    if (Path.GetFullPath(selectedFilePath) == Path.GetFullPath(BundleInst.path))
                    {
                        await MessageBoxUtil.ShowDialog(this,
                            "File in use", "Please use File > Save to save over the currently open bundle.");
                        return;
                    }

                    try
                    {
                        SaveBundle(BundleInst, selectedFilePath);
                    }
                    catch (Exception ex)
                    {
                        await MessageBoxUtil.ShowDialog(this,
                            "Write exception", "There was a problem while writing the file:\n" + ex.ToString());
                    }
                }
                else
                {
                    try
                    {
                        SaveBundleOver(BundleInst);
                    }
                    catch (Exception ex)
                    {
                        await MessageBoxUtil.ShowDialog(this,
                            "Write exception", "There was a problem while writing the file:\n" + ex.ToString());
                    }
                }
            }
        }

        private async Task AskForSave()
        {
            if (changesUnsaved && BundleInst != null)
            {
                MessageBoxResult choice = await MessageBoxUtil.ShowDialog(this,
                    "Changes made", "You've modified this file. Would you like to save?",
                    MessageBoxType.YesNo);
                if (choice == MessageBoxResult.Yes)
                {
                    await AskForLocationAndSave(true);
                }
            }
        }

        private async Task AskForLocationAndCompress()
        {
            if (BundleInst != null)
            {
                // temporary, maybe I should just write to a memory stream or smth
                // edit: looks like uabe just asks you to open a file instead of
                // using your currently opened one, so that may be the workaround
                if (changesMade)
                {
                    string messageBoxTest;
                    if (changesUnsaved)
                    {
                        messageBoxTest =
                            "You've modified this file, but you still haven't saved this bundle file to disk yet. If you want \n" +
                            "to compress the file with changes, please save this bundle now and open that file instead. \n" +
                            "Click Ok to compress the file without changes.";
                    }
                    else
                    {
                        messageBoxTest =
                            "You've modified this file, but only the old file before you made changes is open. If you want to compress the file with \n" +
                            "changes, please close this bundle and open the file you saved. Click Ok to compress the file without changes.";
                    }

                    MessageBoxResult continueWithChanges = await MessageBoxUtil.ShowDialog(
                        this, "Note", messageBoxTest,
                        MessageBoxType.OKCancel);

                    if (continueWithChanges == MessageBoxResult.Cancel)
                    {
                        return;
                    }
                }

                var selectedFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
                {
                    Title = "Save as..."
                });

                string? selectedFilePath = Extensions.GetSaveFileDialogFile(selectedFile);
                if (selectedFilePath == null)
                    return;

                if (Path.GetFullPath(selectedFilePath) == Path.GetFullPath(BundleInst.path))
                {
                    await MessageBoxUtil.ShowDialog(this,
                        "File in use", "Since this file is already open in UABEA, you must pick a new file name (sorry!)");
                    return;
                }

                const string lz4Option = "LZ4";
                const string lzmaOption = "LZMA";
                const string cancelOption = "Cancel";
                string result = await MessageBoxUtil.ShowDialogCustom(
                    this, "Note", "What compression method do you want to use?\nLZ4: Faster but larger size\nLZMA: Slower but smaller size",
                    lz4Option, lzmaOption, cancelOption);

                AssetBundleCompressionType compType = result switch
                {
                    lz4Option => AssetBundleCompressionType.LZ4,
                    lzmaOption => AssetBundleCompressionType.LZMA,
                    _ => AssetBundleCompressionType.None
                };

                if (compType != AssetBundleCompressionType.None)
                {
                    ProgressWindow progressWindow = new ProgressWindow("Compressing...");

                    Thread thread = new Thread(new ParameterizedThreadStart(CompressBundle));
                    object[] threadArgs =
                    {
                        BundleInst,
                        selectedFilePath,
                        compType,
                        progressWindow.Progress
                    };
                    thread.Start(threadArgs);

                    await progressWindow.ShowDialog(this);
                }
            }
            else
            {
                await MessageBoxUtil.ShowDialog(this, "Note", "Please open a bundle file before using compress.");
            }
        }

        private async Task<string?> AskLoadSplitFile(string fileToSplit)
        {
            MessageBoxResult splitRes = await MessageBoxUtil.ShowDialog(this,
                "Split file detected", "This file ends with .split0. Create merged file?\n",
                MessageBoxType.YesNoCancel);

            if (splitRes == MessageBoxResult.Yes)
            {
                var selectedFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
                {
                    Title = "Select location for merged file",
                    SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(fileToSplit)!),
                    SuggestedFileName = Path.GetFileName(fileToSplit[.. ^".split0".Length])
                });
                
                string? selectedFilePath = Extensions.GetSaveFileDialogFile(selectedFile);
                if (selectedFilePath == null)
                    return null;

                using (FileStream mergeFile = File.Open(selectedFilePath, FileMode.Create))
                {
                    int idx = 0;
                    string thisSplitFileNoNum = fileToSplit.Substring(0, fileToSplit.Length - 1);
                    string thisSplitFileNum = fileToSplit;
                    while (File.Exists(thisSplitFileNum))
                    {
                        using (FileStream thisSplitFile = File.OpenRead(thisSplitFileNum))
                        {
                            thisSplitFile.CopyTo(mergeFile);
                        }

                        idx++;
                        thisSplitFileNum = $"{thisSplitFileNoNum}{idx}";
                    };
                }
                return selectedFilePath;
            }
            else if (splitRes == MessageBoxResult.No)
            {
                return fileToSplit;
            }
            else //if (splitRes == MessageBoxResult.Cancel)
            {
                return null;
            }
        }

        private async void AskLoadCompressedBundle(BundleFileInstance bundleInst)
        {
            string decompSize = Extensions.GetFormattedByteSize(GetBundleDataDecompressedSize(bundleInst.file));

            /*const string fileOption = "File";
            const string memoryOption = "Memory";
            const string cancelOption = "Cancel";
            string result = await MessageBoxUtil.ShowDialogCustom(
                this, "Note", "This bundle is compressed. Decompress to file or memory?\nSize: " + decompSize,
                fileOption, memoryOption, cancelOption);

            if (result == fileOption)
            {
                string? selectedFilePath;
                while (true)
                {
                    var selectedFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
                    {
                        Title = "Save as...",
                        FileTypeChoices = new List<FilePickerFileType>()
                        {
                            new FilePickerFileType("All files") { Patterns = new List<string>() { "*.*" } }
                        }
                    });

                    selectedFilePath = Extensions.GetSaveFileDialogFile(selectedFile);
                    if (selectedFilePath == null)
                        return;

                    if (Path.GetFullPath(selectedFilePath) == Path.GetFullPath(bundleInst.path))
                    {
                        await MessageBoxUtil.ShowDialog(this,
                            "File in use", "Since this file is already open in UABEA, you must pick a new file name (sorry!)");
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                DecompressToFile(bundleInst, selectedFilePath);
            }
            else if (result == memoryOption)
            {
                // for lz4 block reading
                if (bundleInst.file.DataIsCompressed)
                {
                    DecompressToMemory(bundleInst);
                }
            }
            else //if (result == cancelOption || result == closeOption)
            {
                return;
            }
            */
            DecompressToMemory(bundleInst);
            LoadBundle(bundleInst);
        }

        private void DecompressToFile(BundleFileInstance bundleInst, string savePath)
        {
            AssetBundleFile bundle = bundleInst.file;

            FileStream bundleStream = File.Open(savePath, FileMode.Create);
            bundle.Unpack(new AssetsFileWriter(bundleStream));

            bundleStream.Position = 0;

            AssetBundleFile newBundle = new AssetBundleFile();
            newBundle.Read(new AssetsFileReader(bundleStream));

            bundle.Close();
            bundleInst.file = newBundle;
        }

        private void DecompressToMemory(BundleFileInstance bundleInst)
        {
            AssetBundleFile bundle = bundleInst.file;

            MemoryStream bundleStream = new MemoryStream();
            bundle.Unpack(new AssetsFileWriter(bundleStream));

            bundleStream.Position = 0;

            AssetBundleFile newBundle = new AssetBundleFile();
            newBundle.Read(new AssetsFileReader(bundleStream));

            bundle.Close();
            bundleInst.file = newBundle;
        }

        private void LoadBundle(BundleFileInstance bundleInst)
        {
            Workspace.Reset(bundleInst);

            comboBox.Items = Workspace.Files;
            comboBox.SelectedIndex = 0;

            lblFileName.Text = bundleInst.name;

            SetBundleControlsEnabled(true, Workspace.Files.Count > 0);
        }

        private void SaveBundle(BundleFileInstance bundleInst, string path)
        {
            List<BundleReplacer> replacers = Workspace.GetReplacers();
            using (FileStream fs = File.Open(path, FileMode.Create))
            using (AssetsFileWriter w = new AssetsFileWriter(fs))
            {
                bundleInst.file.Write(w, replacers.ToList());
            }
            changesUnsaved = false;
        }

        private void SaveBundleOver(BundleFileInstance bundleInst)
        {
            string newName = "~" + bundleInst.name;
            string dir = Path.GetDirectoryName(bundleInst.path)!;
            string filePath = Path.Combine(dir, newName);
            string origFilePath = bundleInst.path;

            SaveBundle(bundleInst, filePath);

            // "overwrite" the original
            bundleInst.file.Reader.Close();
            File.Delete(origFilePath);
            File.Move(filePath, origFilePath);
            bundleInst.file = new AssetBundleFile();
            bundleInst.file.Read(new AssetsFileReader(File.OpenRead(origFilePath)));

            BundleWorkspaceItem? selectedItem = (BundleWorkspaceItem?)comboBox.SelectedItem;
            string? selectedName = null;
            if (selectedItem != null)
            {
                selectedName = selectedItem.Name;
            }

            Workspace.Reset(bundleInst);

            BundleWorkspaceItem? newItem = Workspace.Files.FirstOrDefault(f => f.Name == selectedName);
            if (newItem != null)
            {
                comboBox.SelectedItem = newItem;
            }
        }

        private void CompressBundle(object? args)
        {
            object[] argsArr = (object[])args!;

            var bundleInst = (BundleFileInstance)argsArr[0];
            var path = (string)argsArr[1];
            var compType = (AssetBundleCompressionType)argsArr[2];
            var progress = (IAssetBundleCompressProgress)argsArr[3];

            using (FileStream fs = File.Open(path, FileMode.Create))
            using (AssetsFileWriter w = new AssetsFileWriter(fs))
            {
                bundleInst.file.Pack(bundleInst.file.Reader, w, compType, true, progress);
            }
        }

        private async Task CloseAllFiles()
        {
            List<InfoWindow> openInfoWindowsCopy = new List<InfoWindow>(openInfoWindows);
            foreach (InfoWindow window in openInfoWindowsCopy)
            {
                await window.AskForSaveAndClose();
            }

            //newFiles.Clear();
            changesUnsaved = false;
            changesMade = false;

            am.UnloadAllAssetsFiles(true);
            am.UnloadAllBundleFiles();

            SetBundleControlsEnabled(false, true);

            Workspace.Reset(null);

            lblFileName.Text = "No file opened.";
        }

        private void SetBundleControlsEnabled(bool enabled, bool hasAssets = false)
        {
            // buttons that should be enabled only if there are assets they can interact with
            if (hasAssets)
            {
                //btnExport.IsEnabled = enabled;
                //btnRemove.IsEnabled = enabled;
                //btnRename.IsEnabled = enabled;
                btnInfo.IsEnabled = enabled;
                //btnExportAll.IsEnabled = enabled;
            }

            // always enable / disable no matter if there's assets or not
            comboBox.IsEnabled = enabled;
            //btnImport.IsEnabled = enabled;
            //btnImportAll.IsEnabled = enabled;
        }

        private long GetBundleDataDecompressedSize(AssetBundleFile bundleFile)
        {
            long totalSize = 0;
            foreach (AssetBundleDirectoryInfo dirInf in bundleFile.BlockAndDirInfo.DirectoryInfos)
            {
                totalSize += dirInf.DecompressedSize;
            }
            return totalSize;
        }
    }
}
