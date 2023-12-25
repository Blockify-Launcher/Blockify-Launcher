using CmlLib.Core;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Setting
{
    class MyMinecraftPath : MinecraftPath
    {
        public MyMinecraftPath(string p)
        {
            BasePath = NormalizePath(p);

            Library = NormalizePath(BasePath + "/libs");
            Versions = NormalizePath(BasePath + "/vers");
            Resource = NormalizePath(BasePath + "/resources");

            Runtime = NormalizePath(BasePath + "/java");
            Assets = NormalizePath(BasePath + "/assets");
            CreateDirs();
        }

        public override string GetVersionJarPath(string id)
            => NormalizePath($"{Versions}/{id}/client.jar");

        public override string GetVersionJsonPath(string id)
            => NormalizePath($"{Versions}/{id}/client.json");

        public override string GetAssetObjectPath(string assetId)
            => NormalizePath($"{Assets}/files");
    }
}
