using BlockifyLauncher.Core.Modrinth;
using System.Diagnostics;
using System.IO;

namespace BlockifyLauncher
{
    /// <summary>
    /// Forge / NeoForge loader install via the official installer run headlessly
    /// (`java -jar installer.jar --installClient &lt;dir&gt;`). Returns the produced
    /// version-profile id, which is then launched like any other local version.
    /// </summary>
    public partial class MainWindow
    {
        private void UiPostJob(string phase)
        {
            var job = _activeJob;
            string id = job?.id ?? "pack", title = job?.title ?? "Сборка";
            if (Dispatcher.CheckAccess()) PostJob(id, title, phase, 0, 0);
            else Dispatcher.Invoke(() => PostJob(id, title, phase, 0, 0));
        }

        // (mc, loader, loaderVer) → produced version-profile id
        private async Task<string> RunLoaderInstallerAsync(string mc, string loader, string loaderVer)
        {
            string versionsDir = setting.launcher.MinecraftPath.Versions;
            string basePath = setting.launcher.MinecraftPath.BasePath;

            // a JRE must exist to run the installer — pull the vanilla version (brings the runtime)
            UiPostJob($"Готовлю {loader}: файлы {mc}…");
            var vanilla = await setting.launcher.GetVersionAsync(mc);
            await setting.launcher.CheckAndDownloadAsync(vanilla);
            string java = ResolveInstallerJava(vanilla);

            // the Forge/NeoForge installer expects launcher_profiles.json in the target dir
            string lp = Path.Combine(basePath, "launcher_profiles.json");
            if (!File.Exists(lp))
                await File.WriteAllTextAsync(lp, "{\"profiles\":{},\"selectedProfile\":\"\"}");

            string url = loader == "forge"
                ? $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mc}-{loaderVer}/forge-{mc}-{loaderVer}-installer.jar"
                : $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVer}/neoforge-{loaderVer}-installer.jar";

            UiPostJob($"Скачиваю установщик {loader}…");
            var jarBytes = await ModrinthService.DownloadAsync(url);
            string installerJar = Path.Combine(Path.GetTempPath(), $"blockify-{loader}-{loaderVer}-installer.jar");
            await File.WriteAllBytesAsync(installerJar, jarBytes);

            var before = Directory.Exists(versionsDir)
                ? Directory.GetDirectories(versionsDir).Select(Path.GetFileName).ToHashSet()
                : new HashSet<string?>();

            UiPostJob($"Устанавливаю {loader} {loaderVer}…");
            var psi = new ProcessStartInfo(java, $"-jar \"{installerJar}\" --installClient \"{basePath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = basePath
            };
            using (var proc = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить Java для установщика."))
            {
                string outp = await proc.StandardOutput.ReadToEndAsync();
                string errp = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0)
                {
                    LogDiag($"{loader} installer exit={proc.ExitCode}\nJAVA={java}\nURL={url}\nOUT:\n{outp}\nERR:\n{errp}");
                    throw new InvalidOperationException(
                        $"Установщик {loader} завершился с ошибкой (код {proc.ExitCode}). Подробности в blockify_err.txt.");
                }
            }
            try { File.Delete(installerJar); } catch { }

            // detect the produced profile folder (naming varies across versions)
            var after = Directory.GetDirectories(versionsDir).Select(Path.GetFileName).ToList();
            string expected = loader == "forge" ? $"{mc}-forge-{loaderVer}" : $"neoforge-{loaderVer}";
            string? vn = after.FirstOrDefault(d => d == expected)
                ?? after.FirstOrDefault(d => d != null && !before.Contains(d) && d.Contains(loader, StringComparison.OrdinalIgnoreCase))
                ?? after.FirstOrDefault(d => d != null && !before.Contains(d));
            if (string.IsNullOrEmpty(vn))
                throw new InvalidOperationException($"Профиль {loader} не найден после установки.");
            return vn!;
        }

        private string ResolveInstallerJava(BlockifyLib.Launcher.Version.Version vanilla)
        {
            try
            {
                var js = new Properties.Settings().GetJavaPath();
                if (!string.IsNullOrEmpty(js) && js != "javaw.exe" && File.Exists(js)) return js;
            }
            catch { }
            try
            {
                var jp = setting.launcher.GetJavaPath(vanilla);
                if (!string.IsNullOrEmpty(jp) && File.Exists(jp)) return jp;
            }
            catch { }
            try
            {
                var jd = setting.launcher.GetDefaultJavaPath();
                if (!string.IsNullOrEmpty(jd)) return jd;
            }
            catch { }
            return "java";
        }
    }
}
