// See https://aka.ms/new-console-template for more information

using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MessageBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UABEAvalonia;
using UABEAvalonia.Plugins;
using System.Linq;
using TexturePlugin;
using AssetsTools.NET.Texture;

App app = new App();
app.Start();

class App
{
    public AssetsManager am;
    BundleWorkspace bundleWorkspace;
    AssetWorkspace assetWorkspace;
    public App()
    {
        this.bundleWorkspace = new BundleWorkspace();
        this.am = bundleWorkspace.am;


        string classDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");
        if (File.Exists(classDataPath))
        {
            am.LoadClassPackage(classDataPath);
        } else
        {
            throw new Exception("Cannot find classdata.tpk");
        }
    }

    private void LoadOrAskTypeData(AssetsFileInstance fileInst)
    {
        string uVer = fileInst.file.Metadata.UnityVersion;
        am.LoadClassDatabaseFromPackage(uVer);
    }

    public async void LoadAssetBundle()
    {
        string selectedFile = "C:\\Users\\EvansGrace02\\Documents\\0100B0100E26C000\\romfs\\Data\\StreamingAssets\\command_icon.assetbundle";

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
            BundleFileInstance bundleInst = am.LoadBundleFile(selectedFile, false);

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

    void LoadAssetsFromBundle(BundleWorkspaceItem item)
    {
        if (this.bundleWorkspace.BundleInst == null)
        {
            return;
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
        AssetsFileInstance fileInst = am.LoadAssetsFile(assetStream, assetMemPath, true);

        LoadOrAskTypeData(fileInst);

        if (this.bundleWorkspace.BundleInst != null && fileInst.parentBundle == null)
            fileInst.parentBundle = this.bundleWorkspace.BundleInst;

        var files = new List<AssetsFileInstance> { fileInst };
        LoadAllAssetsWithDeps(files);

        return;
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
    private void LoadAllAssetsWithDeps(List<AssetsFileInstance> files)
    {
        this.assetWorkspace = new AssetWorkspace(this.am, true);
        foreach (AssetsFileInstance file in files)
        {
            this.assetWorkspace.LoadAssetsFile(file, true);
        }
    }

    public void Start()
    {
        LoadAssetBundle();
        BundleWorkspaceItem item = this.bundleWorkspace.Files[0];
        LoadAssetsFromBundle(item);


        //this.assetWorkspace.GetBaseField(this.assetWorkspace.LoadedAssets.Skip(1).First().Value)int classId = am.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;
        int classId = am.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

        var w = from x in this.assetWorkspace.LoadedAssets
                select x.Value;

        foreach (AssetContainer cont in w)
        {
            if (cont.ClassId != classId)
                continue;

            AssetTypeValueField texBaseField = TextureHelper.GetByteArrayTexture(this.assetWorkspace, cont);
            TextureFile texFile = TextureFile.ReadTextureFile(texBaseField);

            if (texFile.m_Width == 0 && texFile.m_Height == 0)
            {
                throw new Exception("Texture size is 0x0. Texture cannot be exported.");
            }
            string assetName = Extensions.ReplaceInvalidPathChars(texFile.m_Name);

            String file = "C:\\Users\\EvansGrace02\\Desktop\\scratch\\Mar23\\" + assetName + ".png";

            if (!GetResSTexture(texFile, cont))
            {
                throw new Exception("No resS texture");
            }
            byte[] data = TextureHelper.GetRawTextureBytes(texFile, cont.FileInstance);

            if (data == null)
            {
                throw new Exception("data is null");
            }

            byte[] platformBlob = TextureHelper.GetPlatformBlob(texBaseField);
            uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

            bool success = TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);


            continue;
        }
    }

    private bool GetResSTexture(TextureFile texFile, AssetContainer cont)
    {
        TextureFile.StreamingInfo streamInfo = texFile.m_StreamData;
        if (streamInfo.path != null && streamInfo.path != "" && cont.FileInstance.parentBundle != null)
        {
            //some versions apparently don't use archive:/
            string searchPath = streamInfo.path;
            if (searchPath.StartsWith("archive:/"))
                searchPath = searchPath.Substring(9);

            searchPath = Path.GetFileName(searchPath);

            AssetBundleFile bundle = cont.FileInstance.parentBundle.file;

            AssetsFileReader reader = bundle.DataReader;
            AssetBundleDirectoryInfo[] dirInf = bundle.BlockAndDirInfo.DirectoryInfos;
            for (int i = 0; i < dirInf.Length; i++)
            {
                AssetBundleDirectoryInfo info = dirInf[i];
                if (info.Name == searchPath)
                {
                    reader.Position = info.Offset + (long)streamInfo.offset;
                    texFile.pictureData = reader.ReadBytes((int)streamInfo.size);
                    texFile.m_StreamData.offset = 0;
                    texFile.m_StreamData.size = 0;
                    texFile.m_StreamData.path = "";
                    return true;
                }
            }
            return false;
        }
        else
        {
            return true;
        }
    }
}