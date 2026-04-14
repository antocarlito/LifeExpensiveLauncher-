using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using LifeExpensiveLauncher.Models;

namespace LifeExpensiveLauncher.Services
{
    /// <summary>
    /// Telecharge la config et les medias depuis le serveur boot.
    /// Tout est centralise dans launcher_config.json sur le serveur.
    /// </summary>
    public class RemoteConfig
    {
        // URL du boot serveur — chargee depuis boot_url.txt (non committe)
        // Fallback vide si le fichier n'existe pas
        public static readonly string BOOT_URL = LoadBootUrl();
        public static readonly string CONFIG_URL = BOOT_URL + "launcher_config.json";

        private readonly HttpClient _http;
        private readonly string _cacheDir;

        private static string LoadBootUrl()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "boot_url.txt");
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
            return "";
        }

        public RemoteConfig(string cacheDir)
        {
            _cacheDir = cacheDir;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            Directory.CreateDirectory(cacheDir);
        }

        /// <summary>
        /// Telecharge la config depuis le serveur et met a jour LauncherConfig
        /// </summary>
        public async Task<bool> LoadRemoteConfigAsync(LauncherConfig config)
        {
            try
            {
                var json = await _http.GetStringAsync(CONFIG_URL);
                dynamic? remote = JsonConvert.DeserializeObject(json);
                if (remote == null) return false;

                // Sauvegarder en cache local (si le serveur est down au prochain lancement)
                File.WriteAllText(Path.Combine(_cacheDir, "launcher_config.json"), json);

                // Appliquer la config
                ApplyConfig(config, remote);
                return true;
            }
            catch
            {
                // Essayer le cache local
                var cachePath = Path.Combine(_cacheDir, "launcher_config.json");
                if (File.Exists(cachePath))
                {
                    try
                    {
                        var json = File.ReadAllText(cachePath);
                        dynamic? cached = JsonConvert.DeserializeObject(json);
                        if (cached != null)
                        {
                            ApplyConfig(config, cached);
                            return true;
                        }
                    }
                    catch { }
                }
                return false;
            }
        }

        private void ApplyConfig(LauncherConfig config, dynamic remote)
        {
            // Serveur
            if (remote.server != null)
            {
                config.ServerHost = (string)(remote.server.host ?? config.ServerHost);
                config.ServerPort = (int)(remote.server.port ?? config.ServerPort);
                config.ServerPassword = (string)(remote.server.password ?? "");
                config.ServerName = (string)(remote.server.name ?? config.ServerName);
            }

            // Liens
            if (remote.links != null)
            {
                config.TeamSpeakUrl = (string)(remote.links.teamspeak ?? config.TeamSpeakUrl);
                config.DiscordUrl = (string)(remote.links.discord ?? config.DiscordUrl);
                config.WebsiteUrl = (string)(remote.links.website ?? config.WebsiteUrl);
                config.WhitelistUrl = (string)(remote.links.whitelist ?? config.WhitelistUrl);
            }

            // API
            if (remote.api != null)
            {
                config.ApiBaseUrl = (string)(remote.api.baseUrl ?? config.ApiBaseUrl);
                config.ApiTokenEndpoint = (string)(remote.api.tokenEndpoint ?? config.ApiTokenEndpoint);
                config.ApiNewsEndpoint = (string)(remote.api.newsEndpoint ?? config.ApiNewsEndpoint);
            }

            // Mods
            if (remote.mods != null)
            {
                config.ModDownloadBaseUrl = (string)(remote.mods.downloadBaseUrl ?? config.ModDownloadBaseUrl);
                config.ModManifestUrl = (string)(remote.mods.manifestUrl ?? config.ModManifestUrl);
                if (remote.mods.required != null)
                {
                    config.RequiredMods.Clear();
                    foreach (var mod in remote.mods.required)
                        config.RequiredMods.Add((string)mod);
                }
            }

            // Media
            if (remote.media != null)
            {
                config.BackgroundUrl = (string)(remote.media.backgroundUrl ?? "");
                config.VideoUrl = (string)(remote.media.videoUrl ?? "");
                config.TfrPluginUrl = (string)(remote.media.tfrPluginUrl ?? "");
                if (remote.media.music != null)
                {
                    config.MusicUrls.Clear();
                    foreach (var url in remote.media.music)
                        config.MusicUrls.Add((string)url);
                }
            }

            // API extra
            if (remote.api?.changelogUrl != null)
                config.ChangelogUrl = (string)remote.api.changelogUrl;

            // Anti-triche
            if (remote.anticheat != null)
            {
                config.TokenSecret = (string)(remote.anticheat.tokenSecret ?? config.TokenSecret);
            }
        }

        /// <summary>
        /// Telecharge un media (image/video) depuis le serveur et le met en cache
        /// </summary>
        public async Task<string?> DownloadMediaAsync(string url, string filename)
        {
            if (string.IsNullOrEmpty(url)) return null;

            var localPath = Path.Combine(_cacheDir, "media", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            try
            {
                var data = await _http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, data);
                return localPath;
            }
            catch
            {
                // Retourner le cache s'il existe
                return File.Exists(localPath) ? localPath : null;
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
