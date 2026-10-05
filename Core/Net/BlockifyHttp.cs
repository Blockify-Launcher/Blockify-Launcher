using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace BlockifyLauncher.Core.Net
{
    /// <summary>
    /// Shared HTTP plumbing for the launcher's own requests (Modrinth, news, loader installers):
    /// one pooled handler with gzip/deflate/brotli, a bounded timeout for API calls, and file
    /// downloads that stream to disk guarded by an inactivity timeout instead of a whole-file
    /// deadline — so a 130 MB jar still finishes on a slow connection.
    /// </summary>
    public static class BlockifyHttp
    {
        public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(25);
        public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

        private static readonly SocketsHttpHandler Handler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),   // pick up DNS changes
            ConnectTimeout = TimeSpan.FromSeconds(20),
            MaxConnectionsPerServer = 8
        };

        /// <summary>JSON/API requests: the whole request is bounded by <see cref="ApiTimeout"/>.</summary>
        public static readonly HttpClient Api = Create(ApiTimeout);

        // file downloads: no overall deadline — progress is guarded by the stall timeout instead
        private static readonly HttpClient Files = Create(System.Threading.Timeout.InfiniteTimeSpan);

        private static HttpClient Create(TimeSpan timeout)
        {
            var h = new HttpClient(Handler, disposeHandler: false) { Timeout = timeout };
            h.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            return h;
        }

        /// <summary>GET a text/JSON resource. Non-2xx → <see cref="HttpRequestException"/> with StatusCode set;
        /// timeouts and connection failures come back with a Russian message.</summary>
        public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
        {
            try
            {
                using var resp = await Api.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException(
                        $"Сервер {HostOf(url)} ответил ошибкой {(int)resp.StatusCode}.", null, resp.StatusCode);
                return await resp.Content.ReadAsStringAsync(ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Сервер {HostOf(url)} не ответил за {ApiTimeout.TotalSeconds:0} с. Проверь интернет и попробуй ещё раз.");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == null)
            {
                throw new HttpRequestException($"Нет связи с {HostOf(url)}. Проверь интернет и попробуй ещё раз.", ex);
            }
        }

        /// <summary>POST a JSON body and return the response text (same error mapping as <see cref="GetStringAsync"/>).</summary>
        public static async Task<string> PostJsonAsync(string url, string json, CancellationToken ct = default)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await Api.PostAsync(url, content, ct);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException(
                        $"Сервер {HostOf(url)} ответил ошибкой {(int)resp.StatusCode}.", null, resp.StatusCode);
                return await resp.Content.ReadAsStringAsync(ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Сервер {HostOf(url)} не ответил за {ApiTimeout.TotalSeconds:0} с. Проверь интернет и попробуй ещё раз.");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == null)
            {
                throw new HttpRequestException($"Нет связи с {HostOf(url)}. Проверь интернет и попробуй ещё раз.", ex);
            }
        }

        /// <summary>
        /// Streams <paramref name="url"/> into <paramref name="dest"/> through a ".part" file; with
        /// <paramref name="sha1"/> the content is verified. The file appears under its final name
        /// only once it is complete (and verified), replacing any existing file.
        /// </summary>
        public static async Task DownloadToFileAsync(string url, string dest, string? sha1 = null,
            IProgress<long>? bytes = null, CancellationToken ct = default)
        {
            string full = Path.GetFullPath(dest);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string part = full + ".part";
            try
            {
                using (var hash = string.IsNullOrEmpty(sha1) ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
                {
                    await using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                        await FetchAsync(url, fs, hash, bytes, Path.GetFileName(full), ct);

                    if (hash != null)
                    {
                        string got = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                        if (!string.Equals(got, sha1, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException(
                                $"Файл {Path.GetFileName(full)} скачался повреждённым (не совпала контрольная сумма).");
                    }
                }
                File.Move(part, full, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                throw;
            }
        }

        /// <summary>Downloads a (small) file into memory with the same stall guard — for callers that need bytes.</summary>
        public static async Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await FetchAsync(url, ms, null, null, Path.GetFileName(new Uri(url).AbsolutePath), ct);
            return ms.ToArray();
        }

        // body → target, failing only when no byte arrives for StallTimeout (or the caller cancels)
        private static async Task FetchAsync(string url, Stream target, IncrementalHash? hash,
            IProgress<long>? bytes, string what, CancellationToken ct)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(StallTimeout);
            try
            {
                using var resp = await Files.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException(
                        $"Не удалось скачать {what}: сервер {HostOf(url)} ответил ошибкой {(int)resp.StatusCode}.", null, resp.StatusCode);

                await using var src = await resp.Content.ReadAsStreamAsync(stall.Token);
                var buf = new byte[81920];
                long total = 0;
                while (true)
                {
                    stall.CancelAfter(StallTimeout);   // re-arm: the limit is per read, not per file
                    int n = await src.ReadAsync(buf.AsMemory(0, buf.Length), stall.Token);
                    if (n == 0) break;
                    hash?.AppendData(buf, 0, n);
                    await target.WriteAsync(buf.AsMemory(0, n), ct);
                    total += n;
                    bytes?.Report(total);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Загрузка {what} остановилась: сервер {HostOf(url)} не отвечает {StallTimeout.TotalSeconds:0} с. Проверь интернет и попробуй ещё раз.");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == null)
            {
                throw new HttpRequestException($"Не удалось скачать {what}: нет связи с {HostOf(url)}.", ex);
            }
        }

        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }
    }
}
