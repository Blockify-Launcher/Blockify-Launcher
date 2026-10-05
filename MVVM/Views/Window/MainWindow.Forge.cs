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

            // NeoForge for 1.20.1 was still published under the old Forge coordinates (net/neoforged/forge)
            bool neoLegacy = loader == "neoforge" && mc == "1.20.1";
            string neoVer = neoLegacy && loaderVer.StartsWith(mc + "-") ? loaderVer[(mc.Length + 1)..] : loaderVer;
            string url = loader == "forge"
                ? $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mc}-{loaderVer}/forge-{mc}-{loaderVer}-installer.jar"
                : neoLegacy
                    ? $"https://maven.neoforged.net/releases/net/neoforged/forge/{mc}-{neoVer}/forge-{mc}-{neoVer}-installer.jar"
                    : $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVer}/neoforge-{loaderVer}-installer.jar";

            UiPostJob($"Скачиваю установщик {loader}…");
            string installerJar = Path.Combine(Path.GetTempPath(), $"blockify-{loader}-{SafeName(loaderVer)}-installer.jar");
            // streamed to disk; no whole-file timeout (slow connections), only a stall timeout
            await ModrinthService.DownloadToFileAsync(url, installerJar);

            var before = Directory.Exists(versionsDir)
                ? Directory.GetDirectories(versionsDir).Select(Path.GetFileName).ToHashSet()
                : new HashSet<string?>();

            UiPostJob($"Устанавливаю {loader} {loaderVer}…");
            // system proxy settings apply to the installer's own library downloads too
            var psi = new ProcessStartInfo(java, $"-Djava.net.useSystemProxies=true -jar \"{installerJar}\" --installClient \"{basePath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = basePath
            };
            using (var proc = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить Java для установщика."))
            {
                // drain both pipes at once: reading them one after another deadlocks when the installer
                // fills the other pipe's buffer; and never wait forever on a stuck installer
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
                {
                    try { await proc.WaitForExitAsync(cts.Token); }
                    catch (OperationCanceledException)
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        throw new TimeoutException($"Установщик {loader} не завершился за 15 минут и был остановлен. Проверь интернет и попробуй ещё раз.");
                    }
                }
                string outp = await outTask, errp = await errTask;
                if (proc.ExitCode != 0)
                {
                    LogDiag($"{loader} installer exit={proc.ExitCode}\nJAVA={java}\nURL={url}\nOUT:\n{outp}\nERR:\n{errp}");
                    throw new InvalidOperationException(
                        $"Установщик {loader} завершился с ошибкой (код {proc.ExitCode}). Подробности в журнале: {LogFile}");
                }
            }
            try { File.Delete(installerJar); } catch { }

            // detect the produced profile folder (naming varies across versions)
            var after = Directory.GetDirectories(versionsDir).Select(Path.GetFileName).ToList();
            var expected = loader == "forge"
                ? new[] { $"{mc}-forge-{loaderVer}" }
                : neoLegacy
                    ? new[] { $"{mc}-forge-{neoVer}", $"neoforge-{neoVer}", $"{mc}-neoforge-{neoVer}" }
                    : new[] { $"neoforge-{loaderVer}" };
            string? vn = after.FirstOrDefault(d => d != null && expected.Contains(d))
                ?? after.FirstOrDefault(d => d != null && !before.Contains(d) && d.Contains("forge", StringComparison.OrdinalIgnoreCase))
                ?? after.FirstOrDefault(d => d != null && !before.Contains(d))
                // reinstall: the profile folder already existed before this run
                ?? after.FirstOrDefault(d => d != null && d.Contains(neoVer, StringComparison.OrdinalIgnoreCase)
                                             && d.Contains("forge", StringComparison.OrdinalIgnoreCase));
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
