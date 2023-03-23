using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UABEAvalonia;

namespace TextureReplacerCLI
{
    internal class AssetsContext : IDisposable
    {
        private bool disposedValue;

        AssetsManager assetsManager;

        public AssetWorkspace assetWorkspace { get; private set; }

        public delegate void AssetModifiedDelegate(AssetsFileInstance fileInstance, byte[] serializedAsset);
        public event AssetModifiedDelegate OnAssetChanged;

        public IEnumerable<AssetContainer> AssetContainers
        {
            get
            {

                IEnumerable<AssetContainer> assetContainers = from kv in this.assetWorkspace.LoadedAssets
                                                              select kv.Value;
                return assetContainers;
            }
        }

        public AssetsContext( AssetWorkspace assetWorkspace) { 
            this.assetWorkspace = assetWorkspace;
            this.assetsManager = assetWorkspace.am;
        }

        private void SaveAssets(string? saveAsFilePath = null)
        {


            bool saveAs = saveAsFilePath is not null;
            var fileToReplacer = new Dictionary<AssetsFileInstance, List<AssetsReplacer>>();
            var changedFiles = this.assetWorkspace.GetChangedFiles();

            foreach (var newAsset in this.assetWorkspace.NewAssets)
            {
                AssetID assetId = newAsset.Key;
                AssetsReplacer replacer = newAsset.Value;
                string fileName = assetId.fileName;

                if (this.assetWorkspace.LoadedFileLookup.TryGetValue(fileName.ToLower(), out AssetsFileInstance? file))
                {
                    if (!fileToReplacer.ContainsKey(file))
                        fileToReplacer[file] = new List<AssetsReplacer>();

                    fileToReplacer[file].Add(replacer);
                }
            }

            if (this.assetWorkspace.fromBundle)
            {
                foreach (var file in changedFiles)
                {
                    List<AssetsReplacer> replacers;
                    if (fileToReplacer.ContainsKey(file))
                        replacers = fileToReplacer[file];
                    else
                        replacers = new List<AssetsReplacer>(0);


                    using (MemoryStream ms = new MemoryStream())
                    using (AssetsFileWriter w = new AssetsFileWriter(ms))
                    {
                        file.file.Write(w, 0, replacers);
                        this.OnAssetChanged(file, ms.ToArray());
                    }
                }

            }
            else
            {
                foreach (var file in changedFiles)
                {
                    List<AssetsReplacer> replacers;
                    if (fileToReplacer.ContainsKey(file))
                        replacers = fileToReplacer[file];
                    else
                        replacers = new List<AssetsReplacer>(0);

                    string filePath;
                    if (saveAs)
                    {
                        while (true)
                        {

                            filePath = saveAsFilePath!;

                            if (Path.GetFullPath(filePath) == Path.GetFullPath(file.path))
                            {
                                throw new Exception("You already have this file open. To overwrite, use Save instead of Save as.");
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        string newName = "~" + file.name;
                        string dir = Path.GetDirectoryName(file.path)!;
                        filePath = Path.Combine(dir, newName);
                    }

                    using (FileStream fs = File.OpenWrite(filePath))
                    using (AssetsFileWriter w = new AssetsFileWriter(fs))
                    {
                        file.file.Write(w, 0, replacers);
                    }

                    if (!saveAs)
                    {
                        string origFilePath = file.path;

                        // "overwrite" the original
                        file.file.Reader.Close();
                        File.Delete(file.path);
                        File.Move(filePath, origFilePath);
                        file.file = new AssetsFile();
                        file.file.Read(new AssetsFileReader(File.OpenRead(origFilePath)));
                    }
                }
            }
        }


        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    SaveAssets();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~AssetsContext()
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
    }
}
