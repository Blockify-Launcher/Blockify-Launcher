using System.IO;
using System.Reflection;

namespace BlockifyLauncher.Core
{
    /// <summary>
    /// Single source of the launcher version (from &lt;Version&gt; in the csproj or -p:Version in CI)
    /// and the User-Agent every HTTP client sends.
    /// </summary>
    public static class AppInfo
    {
        public static readonly string Version =
            (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
             ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")
            .Split('+')[0];   // drop the "+<commit sha>" build metadata the SDK appends

        public static readonly string UserAgent = $"BlockifyLauncher/{Version} (github.com/Blockify-Launcher)";
    }
}
