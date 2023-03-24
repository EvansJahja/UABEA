// See https://aka.ms/new-console-template for more information

using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UABEAvalonia;
using TexturePlugin;
using AssetsTools.NET.Texture;
using Path = System.IO.Path;
using TextureReplacerCLI;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Collections.Immutable;
using SixLabors.ImageSharp;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Processing;
using Point = SixLabors.ImageSharp.Point;
using CommandLine;

class App: IDisposable
{
    [Option(
        Required = true,
        HelpText = "Output folder"
        )]
    public string outputFolder { get; set; }

    [Option(
        Required =true,
        HelpText = "Folder containing replacement files"
        )]
    public string replacementFolder { get {
            return _replacementFolder;
        } set {
            if (!Directory.Exists(value))
            {
                throw new Exception($"Directory {value} for replacementfolder does not exist");
            }
            _replacementFolder = value;
        } }
    private string _replacementFolder;

    [Option(
        Required = true,
        HelpText = "Data folder containing assets"
        )]
    public string dataFolder { 
        get {
            return _dataFolder; 
        } set {
            if (!Directory.Exists( value))
            {
                throw new Exception($"Directory {value} for datafolder does not exist");
            }
            _dataFolder = value;

            streamingAssets = Path.Join(_dataFolder, "StreamingAssets");
        }
    }
    private string _dataFolder;
    string streamingAssets;

    private Dictionary<string, Mutex> mutexes = new Dictionary<string, Mutex>();



    private string _scratchDir = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
    string scratchDir
    {
        get
        {
            return _scratchDir;
        }
    }

    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<App>(args)
                   .WithParsed(app =>
                   {
                       using (app)
                       {
                           app.Start();
                       }
                   })               
                   ;
    }

    public void Dispose()
    {
        string relPath = Path.GetRelativePath(Path.GetTempPath(), scratchDir);
        bool canDelete = relPath == Path.GetFileName(scratchDir);
        if (canDelete)
        {
            Directory.Delete(scratchDir, true);
        }
    }
    
    record FileSet(string replacementFile, string container, string name, long pathId, long classID, Rectangle? spriteRect, long? tex2DPathID);
    record BundleReplacementRequest(string assetBundlePath, FileSet[] replacements);
    IEnumerable<BundleReplacementRequest> assetBundlesNeedModification() {
        Dictionary<string, string> filesForModifications = new Dictionary<string, string>();
        {
            Matcher matcher = new();
            matcher.AddIncludePatterns(new[] { "**/*.png" });

            IEnumerable<string> matchingFiles = matcher.GetResultsInFullPath(replacementFolder);

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
            
            Matcher matcher = new();
            matcher.AddIncludePatterns(new[] { "**/*.assetbundle" });
            matcher.AddExcludePatterns(new[] { "bg/", "character/", "effects/" , "fashion/", "scenario/", "sd/", "still/"});
            IEnumerable<string> matchingFiles = matcher.GetResultsInFullPath(streamingAssets);


            Queue<BundleReplacementRequest> qu = new Queue<BundleReplacementRequest>();
            bool finished = false;

            Task.Run(()=> {
                Parallel.ForEach<string>(matchingFiles, (assetBundlefile, ct) =>
                {
                    UnityContainer ucont = new UnityContainer();
                    using (AssetBundleContext bundleContext = new AssetBundleContext(assetBundlefile))
                    using (AssetsContext assetsContext = bundleContext.AssetsContext())
                    {
                        int spriteClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Sprite").ClassId;
                        int texture2dClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

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
                            AssetContainer cont = assetsContext.assetWorkspace.GetAssetContainer(fileInstance, 0, kvp.Key.asset.PathId);
                            var bf = assetsContext.assetWorkspace.GetBaseField(cont);
                            string name = bf["m_Name"].AsString;
                            if (filesForModifications.ContainsKey(name))
                            {
                                string f = filesForModifications[name];
                                string container = kvp.Value;
                                Rectangle? rectangle = null;
                                AssetContainer? tex2DContainer = null;
                                if (cont.ClassId == spriteClassId)
                                {
                                    rectangle = new Rectangle(
                                        bf["m_Rect"]["x"].AsInt,
                                        bf["m_Rect"]["y"].AsInt,
                                        bf["m_Rect"]["width"].AsInt,
                                        bf["m_Rect"]["height"].AsInt
                                        );
                                    // find container for texture. Probably won't work if the dependency is external.
                                    long texturePathID = bf["m_RD"]["texture"]["m_PathID"].AsLong;
                                    tex2DContainer = assetsContext.assetWorkspace.GetAssetContainer(fileInstance, 0, texturePathID);


                                }

                                fileSets.Add(new FileSet(f, container, name, kvp.Key.asset.PathId, cont.PathId, rectangle, tex2DContainer?.PathId));

                            }
                        }
                        if (fileSets.Count == 0)
                        {
                            return;
                        }

                        var texture2dFileSetContainers = (from x in fileSets where x.classID == texture2dClassId select x.container).ToImmutableHashSet();
                        var combinedFileSets = from x in fileSets
                                               where
                           (x.classID != texture2dClassId && !texture2dFileSetContainers.Contains(x.container)) ||
                           x.classID == texture2dClassId
                                               select x;


                        lock (qu)
                        {
                            qu.Enqueue(new BundleReplacementRequest(assetBundlefile, combinedFileSets.ToArray()));
                        }
                    }
                });
                finished = true;
            });
            while (!finished)
            {
                while (qu.Count > 0)
                {
                    yield return qu.Dequeue();
                }
            }

            
        }
    }

    private Mutex getMutexForFile(string textureFileName)
    {
        Mutex m;
        lock (mutexes)
        {
            if (mutexes.ContainsKey(textureFileName))
            {
                m = mutexes[textureFileName];
            }
            else
            {
                m = new Mutex();
                mutexes.Add(textureFileName, m);
            }
        }
        return m;
    }

    public void Start()
    {
        Directory.CreateDirectory(scratchDir);
        List<Task> tasks = new List<Task>();
        foreach (BundleReplacementRequest replacementRequest in assetBundlesNeedModification())
        {
            tasks.Add(Task.Run(()=>replaceAssetBundle(replacementRequest.assetBundlePath, replacementRequest.replacements)));
        }
        Task.WaitAll(tasks.ToArray());
        
    }
        
    void replaceAssetBundle(string originalAssetBundle, FileSet[] replacementFileInfos)
    {
        string saveAs = Path.Join(outputFolder, Path.GetRelativePath(this.streamingAssets, originalAssetBundle));

        Directory.CreateDirectory(Path.GetDirectoryName(saveAs));

        using (AssetBundleContext bundleContext = new AssetBundleContext(originalAssetBundle, saveAs))
        using (AssetsContext assetsContext = bundleContext.AssetsContext())
        {
            AssetWorkspace assetWorkspace = assetsContext.assetWorkspace;
            int spriteClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Sprite").ClassId;
            int texture2dClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;


            foreach(FileSet fileSet in replacementFileInfos)
            {
                AssetsFileInstance fileInstance = assetsContext.assetWorkspace.LoadedFiles[0];
                AssetContainer cont = assetsContext.assetWorkspace.GetAssetContainer(fileInstance, 0, fileSet.pathId); ;

                if (cont.ClassId == spriteClassId) {
                    string file = Path.GetFileNameWithoutExtension(fileSet.container);
                    string textureFileName = Path.Join(scratchDir, file + ".png");


                    Mutex m = getMutexForFile(textureFileName);
                    try
                    {
                        m.WaitOne();
                        AssetContainer tex2DCont = assetsContext.assetWorkspace.GetAssetContainer(fileInstance, 0, fileSet.tex2DPathID.Value); ;

                        AssetTypeValueField baseField;
                        TextureFile texFile;
                        byte[] platformBlob;
                        uint platform;

                        bool infoOnly = File.Exists(textureFileName);

                        ExportTexture(assetWorkspace, tex2DCont, infoOnly, out baseField, out texFile, textureFileName, out platformBlob, out platform);

                        string replacementFile = fileSet.replacementFile;
                        Rectangle rect = fileSet.spriteRect.Value;
                        Console.WriteLine("Opening {0} for {1}", textureFileName, originalAssetBundle);

                        using (Image<Rgba32> image = Image.Load<Rgba32>(textureFileName))
                        {
                            using (Image<Rgba32> replacementImage = Image.Load<Rgba32>(replacementFile))
                            {
                                replacementImage.Mutate(i => i
                                .Resize(rect.Width, rect.Height)
                                .Crop(rect.Width, rect.Height)
                                );

                                Point point = new Point(rect.X, image.Height - rect.Y - replacementImage.Height);

                                // We don't want alpha blending, we want to replace it with src, otherwise the original and translated overlap.
                                GraphicsOptions options = new GraphicsOptions();
                                options.AlphaCompositionMode = PixelAlphaCompositionMode.Src;

                                image.Mutate(i => i.DrawImage(replacementImage, point, options));
                                image.Save(textureFileName);

                                int width, height;
                                var b = TextureImportExport.Import(textureFileName, (TextureFormat)texFile.m_TextureFormat, out width, out height, platform, platformBlob);

                                AssetTypeValueField image_data = baseField["image data"];
                                image_data.AsByteArray = b;


                                byte[] savedAsset = baseField.WriteToByteArray();

                                var replacer = new AssetsReplacerFromMemory(
                                    tex2DCont.PathId, tex2DCont.ClassId, tex2DCont.MonoId, savedAsset);

                                assetWorkspace.AddReplacer(tex2DCont.FileInstance, replacer, new MemoryStream(savedAsset));

                            }
                        }
                    } finally
                    {
                        m.ReleaseMutex();
                    }
                    
                    
                } else if (cont.ClassId == texture2dClassId)
                {
                    string file = Path.GetFileNameWithoutExtension(fileSet.container);
                    string textureFileName = Path.Join(scratchDir, file + ".png");
                    if (File.Exists(textureFileName))
                    {
                        continue;
                    }

                    string replacementFile = fileSet.replacementFile;

                    AssetTypeValueField baseField;
                    TextureFile texFile;
                    byte[] platformBlob;
                    uint platform;

                    Mutex m = getMutexForFile(textureFileName);
                    try
                    {
                        m.WaitOne();

                        ExportTexture(assetWorkspace, cont, true, out baseField, out texFile, textureFileName, out platformBlob, out platform);


                        // TODO match resolution

                        File.Copy(replacementFile, textureFileName);
                    } finally
                    {
                        m.ReleaseMutex();
                    }
                    


                    int width, height;
                    var b = TextureImportExport.Import(textureFileName, (TextureFormat)texFile.m_TextureFormat, out width, out height, platform, platformBlob);

                    AssetTypeValueField image_data = baseField["image data"];
                    image_data.AsByteArray = b;

                    byte[] savedAsset = baseField.WriteToByteArray();

                    var replacer = new AssetsReplacerFromMemory(
                        cont.PathId, cont.ClassId, cont.MonoId, savedAsset);

                    assetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));
                }
            }

        }

        Console.WriteLine("Saved to {0}", saveAs);
    }

    private void ExportTexture(AssetWorkspace assetWorkspace, AssetContainer cont, bool infoOnly, out AssetTypeValueField baseField, out TextureFile texFile, string file, out byte[] platformBlob, out uint platform)
    {
        baseField = TextureHelper.GetByteArrayTexture(assetWorkspace, cont);
        texFile = TextureFile.ReadTextureFile(baseField);
        if (texFile.m_Width == 0 && texFile.m_Height == 0)
        {
            throw new Exception("Texture size is 0x0. Texture cannot be exported.");
        }
        
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
        if (!infoOnly)
        {
            TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);
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