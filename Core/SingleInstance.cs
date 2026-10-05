using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace BlockifyLauncher.Core
{
    /// <summary>
    /// One launcher per Windows session. A second start (double-click on the shortcut, a blockify://install/…
    /// link from the browser) hands its command line to the running launcher over a named pipe and exits;
    /// the running one brings its window to the front and handles the link.
    /// Set BLOCKIFY_MULTI_INSTANCE=1 to run several copies while developing.
    /// </summary>
    public static class SingleInstance
    {
        private const string MutexName = @"Local\BlockifyLauncher.Instance";
        private const int ProtocolVersion = 1;

        private static Mutex? _mutex;
        private static bool _owned;
        private static CancellationTokenSource? _serverCts;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;

        // per user: another account on the same PC (fast user switching) runs its own launcher
        private static string PipeName
        {
            get
            {
                string sid = "";
                try { sid = WindowsIdentity.GetCurrent().User?.Value ?? ""; } catch { }
                return "BlockifyLauncher-" + sid;
            }
        }

        /// <summary>
        /// true — this process is the launcher, go on starting. false — the arguments went to the launcher that
        /// is already running (or it hangs and the user was told so): exit right away.
        /// </summary>
        public static bool TryAcquire(string[] args)
        {
            if (Environment.GetEnvironmentVariable("BLOCKIFY_MULTI_INSTANCE") == "1") return true;

            try
            {
                _mutex = new Mutex(true, MutexName, out bool createdNew);
                if (createdNew) { _owned = true; return true; }
            }
            catch (Exception ex)
            {
                AppLog.Warn("single instance: mutex unavailable, running without it: " + ex.Message);
                return true;
            }

            // the running copy may take the foreground (Windows only lets it if we allow it)
            try { AllowSetForegroundWindow(ASFW_ANY); } catch { }

            if (TrySend(args))
            {
                AppLog.Info("single instance: arguments handed to the running launcher");
                return false;
            }

            // the other copy may be closing right now (e.g. restarting after a WebView crash) — wait for it a little
            try
            {
                if (_mutex.WaitOne(5000)) { _owned = true; return true; }
            }
            catch (AbandonedMutexException) { _owned = true; return true; }   // it died without releasing: ours now
            catch (Exception ex) { AppLog.Warn("single instance: wait failed: " + ex.Message); }

            AppLog.Warn("single instance: the running launcher does not respond");
            System.Windows.MessageBox.Show(
                "Blockify уже запущен, но не отвечает.\n\nЗакрой его (если нужно — через диспетчер задач) и запусти лаунчер снова.",
                "Blockify", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return false;
        }

        private static bool TrySend(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                client.Connect(3000);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);

                var payload = new JObject
                {
                    ["v"] = ProtocolVersion,
                    ["args"] = JArray.FromObject(args ?? Array.Empty<string>()),
                    ["cwd"] = Environment.CurrentDirectory
                };
                writer.WriteLine(payload.ToString(Formatting.None));

                var reply = reader.ReadLineAsync();
                if (!reply.Wait(3000)) return false;
                return reply.Result == "ok";
            }
            catch (Exception ex)
            {
                AppLog.Warn("single instance: cannot reach the running launcher: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Listens for later starts. <paramref name="onArgs"/> runs on a pool thread and returns false when
        /// the launcher is shutting down (the caller then waits for it to exit and starts normally).
        /// </summary>
        public static void StartServer(Func<string[], bool> onArgs)
        {
            if (!_owned || _serverCts != null) return;
            _serverCts = new CancellationTokenSource();
            var token = _serverCts.Token;
            string name = PipeName;

            Task.Run(async () =>
            {
                int failures = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await server.WaitForConnectionAsync(token);
                        failures = 0;

                        using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                        using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(5000);

                        string? line = await reader.ReadLineAsync(timeout.Token);
                        string[] args = ParseArgs(line);
                        bool accepted = false;
                        try { accepted = onArgs(args); }
                        catch (Exception ex) { AppLog.Warn("single instance: handler failed: " + ex.Message); }
                        await writer.WriteLineAsync(accepted ? "ok" : "busy");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("single instance: pipe server: " + ex.Message);
                        if (++failures >= 10) { AppLog.Warn("single instance: pipe server stopped"); break; }
                        try { await Task.Delay(500, token); } catch { break; }
                    }
                }
            });
        }

        private static string[] ParseArgs(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return Array.Empty<string>();
            try
            {
                var o = JObject.Parse(line);
                return (o["args"] as JArray)?
                    .Where(t => t.Type == JTokenType.String)
                    .Select(t => t.Value<string>() ?? "")
                    .ToArray() ?? Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        public static void StopServer()
        {
            try { _serverCts?.Cancel(); } catch { }
        }

        /// <summary>Call on exit, on the thread that started the app (the mutex belongs to it).</summary>
        public static void Release()
        {
            StopServer();
            try { if (_owned) _mutex?.ReleaseMutex(); } catch { }   // process exit frees it anyway
            try { _mutex?.Dispose(); } catch { }
            _mutex = null;
            _owned = false;
        }
    }
}
