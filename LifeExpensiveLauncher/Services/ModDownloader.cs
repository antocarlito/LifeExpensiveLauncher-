using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LifeExpensiveLauncher.Models;

namespace LifeExpensiveLauncher.Services
{
    public class DownloadProgress
    {
        public string FileName { get; set; } = "";
        public int CurrentFile { get; set; }
        public int TotalFiles { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedMbps { get; set; }
        public string Status { get; set; } = "";
    }

    public class ModDownloader
    {
        private readonly HttpClient _http;
        private readonly LauncherConfig _config;

        public ModDownloader(LauncherConfig config)
        {
            _config = config;
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        }

        public event Action<DownloadProgress>? OnProgress;

        /// <summary>
        /// Recupere le manifeste des mods depuis le serveur Uni-Launcher
        /// Format: { version, fileCount, files: [{ path, isCompress, size, sizeCompressed, hash }] }
        /// </summary>
        public async Task<ModRepository?> GetManifestAsync()
        {
            try
            {
                var url = _config.ModManifestUrl;
                var json = await _http.GetStringAsync(url);

                // Parser avec JObject pour un controle total
                var raw = Newtonsoft.Json.Linq.JObject.Parse(json);
                if (raw == null) return null;

                var repo = new ModRepository
                {
                    Version = (int)(raw["version"] ?? 1),
                    FileCount = (int)(raw["fileCount"] ?? 0),
                    Files = new List<ModFileInfo>()
                };

                var files = raw["files"] as Newtonsoft.Json.Linq.JArray;
                if (files != null)
                {
                    foreach (var f in files)
                    {
                        try
                        {
                            // Tout en string puis parse pour eviter les overflow uint64
                            var path = f["path"]?.ToString() ?? "";
                            var sizeStr = f["size"]?.ToString() ?? "0";
                            var sizeCompStr = f["sizeCompressed"]?.ToString() ?? "0";

                            long.TryParse(sizeStr, out long size);
                            long.TryParse(sizeCompStr, out long sizeComp);

                            repo.Files.Add(new ModFileInfo
                            {
                                Path = path,
                                Size = size,
                                Hash = f["hash"]?.ToString() ?? "",
                                IsCompressed = f["isCompress"]?.ToString() == "True" || f["isCompress"]?.ToString() == "true",
                                SizeCompressed = sizeComp
                            });
                        }
                        catch
                        {
                            // Ignorer un fichier mal forme, continuer avec les autres
                        }
                    }
                }

                repo.FileCount = repo.Files.Count;
                return repo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verifie et telecharge les fichiers manquants ou modifies
        /// </summary>
        public async Task<(int downloaded, int upToDate, List<string> errors)> SyncModsAsync(
            string modsPath, ModRepository manifest, CancellationToken ct = default)
        {
            int downloaded = 0;
            int upToDate = 0;
            var errors = new List<string>();

            long totalBytes = manifest.Files.Sum(f => f.Size);
            long totalDownloaded = 0;
            var startTime = DateTime.Now;

            for (int i = 0; i < manifest.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var entry = manifest.Files[i];
                var localPath = Path.Combine(modsPath, entry.Path);
                var localDir = Path.GetDirectoryName(localPath);

                // Verifier si le fichier est deja bon
                if (File.Exists(localPath))
                {
                    var info = new FileInfo(localPath);
                    if (info.Length == entry.Size)
                    {
                        upToDate++;
                        totalDownloaded += entry.Size;
                        ReportProgress(entry.Path, i + 1, manifest.Files.Count,
                            totalDownloaded, totalBytes, startTime, "Verifie");
                        continue;
                    }
                }

                // Creer le dossier si necessaire
                if (localDir != null && !Directory.Exists(localDir))
                    Directory.CreateDirectory(localDir);

                // Telecharger
                try
                {
                    var url = _config.ModDownloadBaseUrl + entry.Path.Replace('\\', '/');
                    using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write);

                    var buffer = new byte[81920];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                        totalDownloaded += bytesRead;

                        ReportProgress(entry.Path, i + 1, manifest.Files.Count,
                            totalDownloaded, totalBytes, startTime, "Telechargement");
                    }

                    downloaded++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{entry.Path}: {ex.Message}");
                }
            }

            return (downloaded, upToDate, errors);
        }

        /// <summary>
        /// Supprime les fichiers locaux qui ne sont plus dans le manifeste
        /// </summary>
        public List<string> CleanExtraFiles(string modsPath, ModRepository manifest)
        {
            var removed = new List<string>();
            var manifestPaths = new HashSet<string>(
                manifest.Files.Select(f => f.Path.ToLowerInvariant().Replace('/', '\\')));

            foreach (var mod in _config.RequiredMods)
            {
                var modDir = Path.Combine(modsPath, mod);
                if (!Directory.Exists(modDir)) continue;

                foreach (var file in Directory.GetFiles(modDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(modsPath, file)
                        .ToLowerInvariant().Replace('/', '\\');

                    if (!manifestPaths.Contains(relative))
                    {
                        try
                        {
                            File.Delete(file);
                            removed.Add(relative);
                        }
                        catch { }
                    }
                }
            }

            return removed;
        }

        private void ReportProgress(string fileName, int current, int total,
            long bytesDown, long totalBytes, DateTime startTime, string status)
        {
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            var speed = elapsed > 0 ? (bytesDown / 1024.0 / 1024.0) / elapsed : 0;

            OnProgress?.Invoke(new DownloadProgress
            {
                FileName = Path.GetFileName(fileName),
                CurrentFile = current,
                TotalFiles = total,
                BytesDownloaded = bytesDown,
                TotalBytes = totalBytes,
                SpeedMbps = speed,
                Status = status
            });
        }

        public void Dispose() => _http.Dispose();
    }
}
