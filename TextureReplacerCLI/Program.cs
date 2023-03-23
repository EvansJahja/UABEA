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
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using TextureReplacerCLI;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

App app = new App();
app.Start();

class App
{
    
    record FileSet(string replacementFile, string container, string name, long pathId,AssetContainer cont);
    record BundleReplacementRequest(string assetBundlePath, FileSet[] replacements);


    IEnumerable<BundleReplacementRequest> assetBundlesNeedModification()
    {

        Dictionary<string, string> filesForModifications = new Dictionary<string, string>();
        {
            string translatedFolder = "C:/Users/EvansGrace02/Documents/TMGS4 fan translation-20230316T121114Z-001/Reorganized/Translated/Sprite";

            Matcher matcher = new();
            matcher.AddIncludePatterns(new[] { "**/*.png" });

            IEnumerable<string> matchingFiles = matcher.GetResultsInFullPath(translatedFolder);

            foreach (string file in matchingFiles)
            {
                string filename = Path.GetFileNameWithoutExtension(file);
                if (!filesForModifications.ContainsKey(filename))
                {
                    filesForModifications.Add(filename, file);
                }
            }
        }

        {
            string streamingAssets = "C:/Users/EvansGrace02/Documents/0100B0100E26C000/romfs/Data/StreamingAssets";
            Matcher matcher = new();
            matcher.AddIncludePatterns(new[] { "**/*.assetbundle" });
            matcher.AddExcludePatterns(new[] { "bg/", "character/", "effects/" , "fashion/", "scenario/", "sd/", "still/"});
            IEnumerable<string> matchingFiles = matcher.GetResultsInFullPath(streamingAssets);

            

            foreach (string assetBundlefIle in matchingFiles)
            {
                //Console.WriteLine(assetBundlefIle);
                UnityContainer ucont = new UnityContainer();
                using (AssetBundleContext bundleContext = new AssetBundleContext(assetBundlefIle))
                using (AssetsContext assetsContext = bundleContext.AssetsContext())
                {
                    AssetsManager am = bundleContext.assetsManager;
                    
                    foreach (AssetsFileInstance file in assetsContext.assetWorkspace.LoadedFiles)
                    {
                        AssetsFileInstance? actualFile;
                        AssetTypeValueField? ucontBaseField;
                        if (UnityContainer.TryGetBundleContainerBaseField(assetsContext.assetWorkspace, file, out actualFile, out ucontBaseField))
                        {
                            ucont.FromAssetBundle(am, actualFile, ucontBaseField);
                        }
                        else if (UnityContainer.TryGetRsrcManContainerBaseField(assetsContext.assetWorkspace, file, out actualFile, out ucontBaseField))
                        {
                            ucont.FromResourceManager(am, actualFile, ucontBaseField);
                        }
                    }

                    List<FileSet> fileSets = new List<FileSet>();
                    AssetsFileInstance fileInstance = assetsContext.assetWorkspace.LoadedFiles[0];
                    foreach (KeyValuePair<UnityContainerAssetInfo, string> kvp in ucont.AssetMap)
                    {
                        //string line = kvp.Value;
                        //string p = Path.GetFileNameWithoutExtension(line);

                        AssetContainer cont = assetsContext.assetWorkspace.GetAssetContainer(fileInstance, 0, kvp.Key.asset.PathId);
                        var bf = assetsContext.assetWorkspace.GetBaseField(cont);
                        string name = bf["m_Name"].AsString;
                        Console.WriteLine(name);
                        if (filesForModifications.ContainsKey(name)) {
                            string f = filesForModifications[name];
                            string container = kvp.Value;
                            fileSets.Add(new FileSet(f, container, name, kvp.Key.asset.PathId, cont));
                        }
                        /*
                        if (filesForModifications.ContainsKey(p))
                        {
                            string f = filesForModifications[p];
                            
                            Console.WriteLine(f);
                        }
                        */
                    }
                    if (fileSets.Count > 0)
                    {
                        int spriteClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Sprite").ClassId;
                        int texture2dClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

                        var texture2dFileSetContainers = (from x in fileSets where x.cont.ClassId == texture2dClassId select x.container).ToImmutableHashSet();

                        var combinedFileSets = from x in fileSets where
                          (x.cont.ClassId != texture2dClassId && !texture2dFileSetContainers.Contains(x.container)) ||
                          x.cont.ClassId == texture2dClassId select x;
                          

                        //AssetsFileInstance fileInstance = assetsContext.assetWorkspace.LoadedFiles[0];
                        
                        foreach (var fileSet  in combinedFileSets) {

                            AssetContainer cont = assetsContext.assetWorkspace.GetAssetContainer(fileInstance, 0, fileSet.pathId);
                            if (cont.ClassId == spriteClassId)
                            {
                                // TODO extract rect
                                continue;
                            }
                        }


                        yield return new BundleReplacementRequest(assetBundlefIle, fileSets.ToArray());
                    }
                }

                
            }
        }
    }


    public void Start()
    {
        foreach (BundleReplacementRequest replacementRequest in assetBundlesNeedModification())
        {
            doStuffs(replacementRequest.assetBundlePath, replacementRequest.replacements);
            /*
            foreach (var replacement in replacementRequest.replacements)
            {
                Console.WriteLine("{0}\n--> {1}\n--> {2}\n\n", replacementRequest.assetBundlePath, replacement.assetFile, replacement.replacementFile);

                
            }
            */
        }

    }

    void doStuffs(string originalAssetBundle, FileSet[] replacementFileInfos)
    {


        using (AssetBundleContext bundleContext = new AssetBundleContext(originalAssetBundle))
        using (AssetsContext assetsContext = bundleContext.AssetsContext())
        {
            AssetWorkspace assetWorkspace = assetsContext.assetWorkspace;

            int texture2dClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

            // TODO: we can optimize this by looking at replacementFileInfos
            foreach (AssetContainer cont in assetsContext.AssetContainers)
            {
                // If it's a texture, we can just replace
                if (cont.ClassId == texture2dClassId)
                {


                    var filesetIEnum = from x in replacementFileInfos where x.pathId == cont.PathId select x;
                    if (!filesetIEnum.Any())
                    {
                        continue;
                    }

                    FileSet fileset = filesetIEnum.First();

                    AssetTypeValueField baseField;
                    TextureFile texFile;
                    string file;
                    byte[] platformBlob;
                    uint platform;
                    ExportTexture(assetWorkspace, cont, out baseField, out texFile, out file, out platformBlob, out platform);

                    int width, height;
                    var b = TextureImportExport.Import(file, (TextureFormat)texFile.m_TextureFormat, out width, out height, platform, platformBlob);


                    AssetTypeValueField image_data = baseField["image data"];
                    image_data.AsByteArray = b;


                    byte[] savedAsset = baseField.WriteToByteArray();

                    var replacer = new AssetsReplacerFromMemory(
                        cont.PathId, cont.ClassId, cont.MonoId, savedAsset);

                    assetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));
                } else
                {
                    // If it's sprite: 
                    // 1. export 
                    // 2. paste sprite to texture

                    continue;
                }

            }
        }




        return;
    }

    private void ExportTexture(AssetWorkspace assetWorkspace, AssetContainer cont, out AssetTypeValueField baseField, out TextureFile texFile, out string file, out byte[] platformBlob, out uint platform)
    {
        baseField = TextureHelper.GetByteArrayTexture(assetWorkspace, cont);
        texFile = TextureFile.ReadTextureFile(baseField);
        if (texFile.m_Width == 0 && texFile.m_Height == 0)
        {
            throw new Exception("Texture size is 0x0. Texture cannot be exported.");
        }
        string assetName = Extensions.ReplaceInvalidPathChars(texFile.m_Name);

        file = "C:/Users/EvansGrace02/Desktop/scratch/Mar23/" + assetName + ".png";
        if (!GetResSTexture(texFile, cont))
        {
            throw new Exception("No resS texture");
        }
        byte[] data = TextureHelper.GetRawTextureBytes(texFile, cont.FileInstance);

        if (data == null)
        {
            throw new Exception("data is null");
        }

        platformBlob = TextureHelper.GetPlatformBlob(baseField);
        platform = cont.FileInstance.file.Metadata.TargetPlatform;
        bool success = TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);
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