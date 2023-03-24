using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia.Win32.Interop.Automation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UABEAvalonia;

namespace TextureReplacerCLI
{
    internal class AssetBundleContext : IDisposable
    {
        private bool disposedValue;
        public AssetsManager assetsManager { get; private set; }
        String bundleFile;
        public BundleWorkspace bundleWorkspace { get; private set; } = new BundleWorkspace();

        public List<Tuple<AssetsFileInstance, byte[]>> ChangedAssetsDatas = new List<Tuple<AssetsFileInstance, byte[]>>();

        String? saveAs;

        public AssetBundleContext(String bundleFile, String? saveAs = null)
        {
            this.saveAs = saveAs;
            this.assetsManager = bundleWorkspace.am;
            string classDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");
            if (File.Exists(classDataPath))
            {
                this.assetsManager.LoadClassPackage(classDataPath);
            }
            else
            {
                throw new Exception("Cannot find classdata.tpk");
            }

            this.bundleFile = bundleFile;

            LoadAssetBundle(this.bundleFile);
        }
        private void LoadOrAskTypeData(AssetsFileInstance fileInst)
        {
            string uVer = fileInst.file.Metadata.UnityVersion;
            this.assetsManager.LoadClassDatabaseFromPackage(uVer);
        }
        AssetWorkspace LoadAssetsFromBundle(BundleWorkspaceItem item)
        {
            if (this.bundleWorkspace.BundleInst == null)
            {
                throw new Exception("no bundleInst");
            }

            string name = item.Name;

            AssetBundleFile bundleFile = this.bundleWorkspace.BundleInst.file;

            Stream assetStream = item.Stream;

            DetectedFileType fileType = AssetBundleDetector.DetectFileType(new AssetsFileReader(assetStream), 0);
            assetStream.Position = 0;

            if (fileType != DetectedFileType.AssetsFile)
            {
                throw new Exception("Not an asset file");
            }

            string assetMemPath = Path.Combine(this.bundleWorkspace.BundleInst.path, name);
            AssetsFileInstance fileInst = this.assetsManager.LoadAssetsFile(assetStream, assetMemPath, true);

            LoadOrAskTypeData(fileInst);

            if (this.bundleWorkspace.BundleInst != null && fileInst.parentBundle == null)
                fileInst.parentBundle = this.bundleWorkspace.BundleInst;

            var files = new List<AssetsFileInstance> { fileInst };
            return LoadAllAssetsWithDeps(files);

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
        private AssetWorkspace LoadAllAssetsWithDeps(List<AssetsFileInstance> files)
        {
            AssetWorkspace assetWorkspace = new AssetWorkspace(this.assetsManager, true);
            foreach (AssetsFileInstance file in files)
            {
                assetWorkspace.LoadAssetsFile(file, true);
            }
            assetWorkspace.GenerateAssetsFileLookup();
            return assetWorkspace;
        }
        public async void LoadAssetBundle(string selectedFile)
        {


            DetectedFileType fileType = AssetBundleDetector.DetectFileType(selectedFile);

            //CloseAllFiles();

            // can you even have split bundles?
            if (fileType != DetectedFileType.Unknown)
            {
                /*
                if (selectedFile.EndsWith(".split0"))
                {
                    string? splitFilePath = await AskLoadSplitFile(selectedFile);
                    if (splitFilePath == null)
                        return;
                    else
                        selectedFile = splitFilePath;
                }
                */
            }

            /*
            if (fileType == DetectedFileType.AssetsFile)
            {
                AssetsFileInstance fileInst = am.LoadAssetsFile(selectedFile, true);

                if (!await LoadOrAskTypeData(fileInst))
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

                InfoWindow info = new InfoWindow(am, fileInstances, false);
                info.Show();
            }

            else */
            if (fileType == DetectedFileType.BundleFile)
            {
                BundleFileInstance bundleInst = this.assetsManager.LoadBundleFile(selectedFile, false);

                if (AssetBundleUtil.IsBundleDataCompressed(bundleInst.file))
                {
                    if (bundleInst.file.DataIsCompressed)
                    {
                        DecompressToMemory(bundleInst);
                    }
                }

                this.bundleWorkspace.Reset(bundleInst);
            }
            else
            {
                throw new Exception("This doesn't seem to be an assets file or bundle.");
            }
        }
        private bool SaveBundle(string path)
        {
            if (bundleWorkspace.BundleInst is null)
            {
                throw new Exception("BundleInst is null");
            }

            if (ChangedAssetsDatas.Count == 0)
            {
                return false;
            }

            List<Tuple<AssetsFileInstance, byte[]>> assetDatas = ChangedAssetsDatas;

            foreach (var tup in assetDatas)
            {
                AssetsFileInstance fileInstance = tup.Item1;
                byte[] assetData = tup.Item2;

                // remember selected index, when we replace the file it unselects the combobox item

                string assetName = Path.GetFileName(fileInstance.path);
                this.bundleWorkspace.AddOrReplaceFile(new MemoryStream(assetData), assetName, true);
                // unload it so the new version is reloaded when we reopen it
                this.assetsManager.UnloadAssetsFile(fileInstance.path);

            }

            if (assetDatas.Count > 0)
            {
                List<BundleReplacer> replacers = this.bundleWorkspace.GetReplacers();
                using (FileStream fs = File.OpenWrite(path))
                {
                    using (AssetsFileWriter w = new AssetsFileWriter(fs))
                    {
                        var bundleInst = this.bundleWorkspace.BundleInst;
                        this.bundleWorkspace.BundleInst.file.Write(w, replacers.ToList());
                    }
                }
            }
            return true;
        }

        private void Compress(string decompressedFilename, string compressedFilename)
        {
            var am = this.bundleWorkspace.am;
            var bun = am.LoadBundleFile(decompressedFilename);
            using (var stream = File.OpenWrite(compressedFilename))
            {
                using (var writer = new AssetsFileWriter(stream))
                {
                    bun.file.Pack(bun.file.Reader, writer, AssetBundleCompressionType.LZMA);
                }
            }
            am.UnloadBundleFile(decompressedFilename);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    if (this.saveAs != null)
                    {
                        string decompressedSaveAs = this.saveAs + "_decompressed";
                        bool hasChanges = SaveBundle(decompressedSaveAs);
                        if (hasChanges)
                        {
                            Compress(decompressedSaveAs, this.saveAs);
                            File.Delete(decompressedSaveAs);
                        }
                    }
                    
                    this.assetsManager.UnloadBundleFile(this.bundleFile);
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~AssetBundleContext()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        internal AssetsContext AssetsContext()
        {

            ChangedAssetsDatas.Clear();
            BundleWorkspaceItem item = this.bundleWorkspace.Files[0];
            AssetWorkspace assetWorkspace =  LoadAssetsFromBundle(item);
            AssetsContext assetsContext = new AssetsContext(assetWorkspace);

            assetsContext.OnAssetChanged += AssetsContext_OnAssetChanged;

            return assetsContext;
        }

        private void AssetsContext_OnAssetChanged(AssetsFileInstance fileInstance, byte[] serializedAsset)
        {
            ChangedAssetsDatas.Add(new Tuple<AssetsFileInstance, byte[]>(fileInstance, serializedAsset));
        }
    }
}
