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

App app = new App();
app.Start();

class App
{
    
    
    

    public App()
    {


    }

    

    




    public void Start()
    {
        string selectedFile = "C:\\Users\\EvansGrace02\\Documents\\0100B0100E26C000\\1.1.0\\romfs\\Data\\StreamingAssets\\command_icon.assetbundle";


        using (AssetBundleContext bundleContext = new AssetBundleContext(selectedFile))
        using (AssetsContext assetsContext = bundleContext.AssetsContext())
        {
            AssetWorkspace assetWorkspace = assetsContext.assetWorkspace;

            int texture2dClassId = bundleContext.assetsManager.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

            foreach (AssetContainer cont in assetsContext.AssetContainers)
            {
                if (cont.ClassId != texture2dClassId)
                    continue;

                AssetTypeValueField baseField = TextureHelper.GetByteArrayTexture(assetWorkspace, cont);
                TextureFile texFile = TextureFile.ReadTextureFile(baseField);

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

                byte[] platformBlob = TextureHelper.GetPlatformBlob(baseField);
                uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

                bool success = TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);

                int width, height;


                var b = TextureImportExport.Import(file, (TextureFormat)texFile.m_TextureFormat, out width, out height, platform, platformBlob);


                AssetTypeValueField image_data = baseField["image data"];
                image_data.AsByteArray = b;


                byte[] savedAsset = baseField.WriteToByteArray();

                var replacer = new AssetsReplacerFromMemory(
                    cont.PathId, cont.ClassId, cont.MonoId, savedAsset);

                assetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));

            }
        }

        
        

        return;
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