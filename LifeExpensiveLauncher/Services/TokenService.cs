using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LifeExpensiveLauncher.Models;

namespace LifeExpensiveLauncher.Services
{
    public class TokenResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Token { get; set; } = "";
    }

    public class TokenService
    {
        private readonly HttpClient _http;
        private readonly LauncherConfig _config;

        public TokenService(LauncherConfig config)
        {
            _config = config;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>
        /// Genere et envoie un token signe au serveur apres le scan
        /// </summary>
        public async Task<TokenResponse> SendScanTokenAsync(string playerUid, string playerName,
            ScanResult scanResult, string scanHash)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Signer le token avec HMAC-SHA256
                var payload = $"{playerUid}:{scanResult.TotalPbos}:{scanResult.TotalFiles}:" +
                              $"{scanResult.SuspiciousFiles.Count}:{scanResult.GlobalHash}:{timestamp}";

                var signature = ComputeHmac(payload, _config.TokenSecret);

                var data = new
                {
                    uid = playerUid,
                    name = playerName,
                    pbo_count = scanResult.TotalPbos,
                    file_count = scanResult.TotalFiles,
                    suspicious_count = scanResult.SuspiciousFiles.Count,
                    suspicious_files = scanResult.SuspiciousFiles,
                    global_hash = scanResult.GlobalHash,
                    scan_hash = scanHash,
                    timestamp,
                    signature,
                    launcher_version = "1.0.0"
                };

                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = _config.ApiBaseUrl + _config.ApiTokenEndpoint;
                var response = await _http.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<TokenResponse>(responseBody)
                           ?? new TokenResponse { Success = false, Message = "Reponse invalide" };
                }

                return new TokenResponse
                {
                    Success = false,
                    Message = $"Erreur serveur: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                return new TokenResponse
                {
                    Success = false,
                    Message = $"Erreur connexion: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Verifie le statut du serveur Arma
        /// </summary>
        public async Task<(bool online, int players, int maxPlayers)> CheckServerStatusAsync()
        {
            try
            {
                var url = _config.ApiBaseUrl + "/api/launcher_status.php";
                var json = await _http.GetStringAsync(url);
                dynamic? status = JsonConvert.DeserializeObject(json);
                if (status != null)
                {
                    return ((bool)status.online, (int)status.players, (int)status.maxPlayers);
                }
            }
            catch { }

            return (false, 0, 0);
        }

        private string ComputeHmac(string data, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose() => _http.Dispose();
    }
}
