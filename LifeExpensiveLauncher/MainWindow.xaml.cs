using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using LifeExpensiveLauncher.Models;
using LifeExpensiveLauncher.Services;

namespace LifeExpensiveLauncher
{
    public partial class MainWindow : Window
    {
        private LauncherConfig _config = new();
        private readonly PboScanner _scanner = new();
        private ModDownloader? _downloader;
        private TokenService? _tokenService;
        private ArmaLauncher? _armaLauncher;
        private CancellationTokenSource? _downloadCts;

        private RemoteConfig? _remoteConfig;
        private ScanResult? _lastScan;
        private bool _modsReady;
        private bool _scanDone;
        private string _settingsPath = "";
        private bool _isMuted;
        private double _bgVolume = 0.3;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // === SPLASH SCREEN ===
            SplashStatus.Text = "Initialisation...";

            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LifeExpensiveLauncher");
            Directory.CreateDirectory(_settingsPath);

            LoadSettings();

            // Charger la config distante (adresses, mods, medias, etc.)
            SplashStatus.Text = "Chargement de la configuration...";
            _remoteConfig = new RemoteConfig(_settingsPath);
            var configLoaded = await _remoteConfig.LoadRemoteConfigAsync(_config);
            if (configLoaded)
                AddLog("Configuration serveur chargee");
            else
                AddLog("Config serveur indisponible, utilisation du cache", true);

            _downloader = new ModDownloader(_config);
            _tokenService = new TokenService(_config);
            _armaLauncher = new ArmaLauncher(_config);

            SplashStatus.Text = "Detection d'Arma 3...";
            await Task.Delay(400);

            if (string.IsNullOrEmpty(_config.ArmaPath))
            {
                var detected = _armaLauncher.DetectArmaPath();
                if (detected != null)
                {
                    _config.ArmaPath = detected;
                    if (string.IsNullOrEmpty(_config.ModsPath))
                        _config.ModsPath = detected;
                    SaveSettings();
                }
            }

            SplashStatus.Text = "Chargement des medias et musiques...";
            await LoadBackgroundAsync();

            // La musique joue deja pendant le splash !

            SplashStatus.Text = "Connexion au serveur...";
            await Task.Delay(500);

            UpdatePathDisplay();
            AddLog("Launcher demarre");

            _ = LoadNewsAsync();
            _ = LoadChangelogAsync();
            _ = CheckServerStatusAsync();

            SplashStatus.Text = "Pret !";
            await Task.Delay(600);

            // === TRANSITION SPLASH -> CONTENU ===
            // Fade out splash
            var fadeOutSplash = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOutSplash.Completed += (s, _) =>
            {
                SplashScreen.Visibility = Visibility.Collapsed;
            };
            SplashScreen.BeginAnimation(OpacityProperty, fadeOutSplash);

            // Fade in contenu
            var fadeInContent = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600))
            {
                BeginTime = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            MainContent.BeginAnimation(OpacityProperty, fadeInContent);

            // Auto-verif mods
            await Task.Delay(800);
            if (!string.IsNullOrEmpty(_config.ArmaPath) && !string.IsNullOrEmpty(_config.ModsPath))
            {
                await CheckAndSyncModsAsync();
            }
            else
            {
                AddLog("Arma 3 non detecte. Cliquez sur le chemin pour le configurer.", true);
            }
        }

        // ========================================
        // SERVEUR STATUS
        // ========================================
        private bool _isMaintenanceMode;

        private async Task CheckServerStatusAsync()
        {
            while (true)
            {
                try
                {
                    // Verifier maintenance
                    await CheckMaintenanceAsync();

                    var (online, players, max) = await _tokenService!.CheckServerStatusAsync();
                    Dispatcher.Invoke(() =>
                    {
                        PlayerCountText.Text = online ? players.ToString() : "--";
                        PlayerMaxText.Text = online ? $" / {max}" : " / --";

                        if (online)
                        {
                            var green = new SolidColorBrush(Color.FromRgb(0, 200, 83));
                            ServerStatusDot.Fill = green;
                            ServerStatusDotBottom.Fill = green;
                            ServerStatusText.Text = $"EN LIGNE - {players}/{max}";
                            ServerStatusText.Foreground = FindResource("TextBrush") as SolidColorBrush;
                        }
                        else
                        {
                            var red = new SolidColorBrush(Color.FromRgb(255, 23, 68));
                            ServerStatusDot.Fill = red;
                            ServerStatusDotBottom.Fill = red;
                            ServerStatusText.Text = "HORS LIGNE";
                            ServerStatusText.Foreground = FindResource("DangerBrush") as SolidColorBrush;
                        }

                        if (_isMaintenanceMode)
                        {
                            var yellow = new SolidColorBrush(Color.FromRgb(255, 214, 0));
                            ServerStatusDot.Fill = yellow;
                            ServerStatusDotBottom.Fill = yellow;
                            ServerStatusText.Text = "MAINTENANCE";
                            ServerStatusText.Foreground = yellow;
                        }

                        UpdatePlayButton();
                    });
                }
                catch { }

                await Task.Delay(30000);
            }
        }

        private string _maintenanceMessage = "";
        private string _whitelistMessage = "";
        private bool _isWhitelistMode;
        private bool _isWhitelistBlocked;

        private async Task CheckMaintenanceAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var json = await http.GetStringAsync(_config.ApiBaseUrl + "/api/launcher_maintenance.php");
                dynamic? data = JsonConvert.DeserializeObject(json);
                if (data != null)
                {
                    bool maint = (bool)data.maintenance;
                    string msg = (string)(data.message ?? "Serveur en maintenance");
                    _maintenanceMessage = msg;
                    bool isStaff = false;

                    // Whitelist
                    bool whitelist = false;
                    try { whitelist = (bool)(data.whitelist ?? false); } catch { }
                    _whitelistMessage = (string)(data.whitelist_message ?? "Serveur en whitelist. Inscrivez-vous sur lifeexpensive.com");
                    _isWhitelistMode = whitelist;

                    // Verifier si le joueur est staff
                    string uid = GetPlayerUid();
                    if (data.staff_uids != null)
                    {
                        foreach (var staffUid in data.staff_uids)
                        {
                            if ((string)staffUid == uid) { isStaff = true; break; }
                        }
                    }

                    _isMaintenanceMode = maint && !isStaff;

                    // Verifier whitelist du joueur via API
                    bool playerWhitelisted = true;
                    if (whitelist && !isStaff && !string.IsNullOrEmpty(uid))
                    {
                        try
                        {
                            var wlJson = await http.GetStringAsync(_config.ApiBaseUrl + "/api/launcher_whitelist_check.php?uid=" + uid);
                            dynamic? wlData = JsonConvert.DeserializeObject(wlJson);
                            if (wlData != null)
                            {
                                playerWhitelisted = (bool)(wlData.whitelisted ?? true);
                            }
                        }
                        catch { playerWhitelisted = true; }
                    }
                    _isWhitelistBlocked = whitelist && !isStaff && !playerWhitelisted;

                    Dispatcher.Invoke(() =>
                    {
                        // Bandeau maintenance
                        if (maint && !isStaff)
                        {
                            MaintenanceBanner.Visibility = Visibility.Visible;
                            MaintenanceBannerText.Text = "MAINTENANCE EN COURS — " + msg;
                            AddLog($"MAINTENANCE: {msg}", true);
                        }
                        else if (maint && isStaff)
                        {
                            MaintenanceBanner.Visibility = Visibility.Visible;
                            MaintenanceBannerText.Text = "MAINTENANCE (Staff) — " + msg;
                            AddLog("Maintenance active (vous etes staff, acces autorise)");
                        }
                        else
                        {
                            MaintenanceBanner.Visibility = Visibility.Collapsed;
                        }

                        // Bandeau whitelist
                        if (_isWhitelistBlocked)
                        {
                            WhitelistBanner.Visibility = Visibility.Visible;
                            AddLog("WHITELIST: Vous n'etes pas whiteliste. Inscrivez-vous sur lifeexpensive.com", true);
                        }
                        else if (whitelist && !MaintenanceBanner.IsVisible)
                        {
                            WhitelistBanner.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            WhitelistBanner.Visibility = Visibility.Collapsed;
                        }

                        UpdatePlayButton();
                    });
                }
            }
            catch
            {
                _isMaintenanceMode = false;
            }
        }

        // ========================================
        // NEWS
        // ========================================
        private async Task LoadNewsAsync()
        {
            try
            {
                var url = _config.ApiBaseUrl + _config.ApiNewsEndpoint;
                using var http = new System.Net.Http.HttpClient();
                var json = await http.GetStringAsync(url);
                var news = JsonConvert.DeserializeObject<List<NewsItem>>(json);

                Dispatcher.Invoke(() =>
                {
                    if (news != null && news.Count > 0)
                    {
                        NewsList.ItemsSource = news;
                    }
                    else
                    {
                        NewsList.ItemsSource = new List<NewsItem>
                        {
                            new() { Title = "Bienvenue", Date = DateTime.Now.ToString("dd/MM/yyyy"),
                                    Content = "Bienvenue sur le launcher LifeExpensive RP !" }
                        };
                    }
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    NewsList.ItemsSource = new List<NewsItem>
                    {
                        new() { Title = "LifeExpensive RP", Date = DateTime.Now.ToString("dd/MM/yyyy"),
                                Content = "Impossible de charger les actualites. Verifiez votre connexion." }
                    };
                });
            }
        }

        // ========================================
        // CHANGELOG
        // ========================================
        private async Task LoadChangelogAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var json = await http.GetStringAsync(_config.ChangelogUrl);
                var changelog = JsonConvert.DeserializeObject<List<ChangelogEntry>>(json);

                Dispatcher.Invoke(() =>
                {
                    if (changelog != null && changelog.Count > 0)
                    {
                        ChangelogList.ItemsSource = changelog;
                    }
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    ChangelogList.ItemsSource = new List<ChangelogEntry>
                    {
                        new() { Version = "?", Date = "", Title = "Impossible de charger le changelog" }
                    };
                });
            }
        }

        private void TabNews_Click(object sender, RoutedEventArgs e)
        {
            NewsPanel.Visibility = Visibility.Visible;
            ChangelogPanel.Visibility = Visibility.Collapsed;
            TabNews.Foreground = FindResource("CyanBrush") as SolidColorBrush;
            TabChangelog.Foreground = FindResource("TextDimBrush") as SolidColorBrush;
        }

        private void TabChangelog_Click(object sender, RoutedEventArgs e)
        {
            NewsPanel.Visibility = Visibility.Collapsed;
            ChangelogPanel.Visibility = Visibility.Visible;
            TabChangelog.Foreground = FindResource("OrangeBrush") as SolidColorBrush;
            TabNews.Foreground = FindResource("TextDimBrush") as SolidColorBrush;
        }

        // ========================================
        // VERIFICATION & TELECHARGEMENT MODS
        // ========================================
        private async void BtnCheckMods_Click(object sender, RoutedEventArgs e)
        {
            await CheckAndSyncModsAsync();
        }

        private async Task CheckAndSyncModsAsync()
        {
            if (string.IsNullOrEmpty(_config.ModsPath))
            {
                AddLog("Configurez le dossier des mods d'abord.", true);
                return;
            }

            BtnCheckMods.IsEnabled = false;
            BtnCheckMods.Content = "Verification...";
            _modsReady = false;
            UpdatePlayButton();

            AddLog("Recuperation du manifeste serveur...");
            ModStatusDetail.Text = "Connexion au serveur...";
            ModStatusDot.Fill = FindResource("TextDimBrush") as SolidColorBrush;

            var manifest = await _downloader!.GetManifestAsync();
            if (manifest == null)
            {
                AddLog("Impossible de recuperer le manifeste. Verifiez votre connexion.", true);
                ModStatusDetail.Text = "Erreur connexion";
                ModStatusDot.Fill = FindResource("DangerBrush") as SolidColorBrush;
                BtnCheckMods.IsEnabled = true;
                BtnCheckMods.Content = "Reessayer";
                return;
            }

            AddLog($"Manifeste: {manifest.FileCount} fichiers (parses: {manifest.Files.Count})");

            // Verifier localement
            AddLog($"Verification dans: {_config.ModsPath}");
            ModStatusDetail.Text = $"Verification de {manifest.FileCount} fichiers...";

            var (missing, modified, extra) = await Task.Run(() =>
            {
                // Verifier par rapport au premier mod requis
                var modRoot = Path.GetDirectoryName(
                    Path.Combine(_config.ModsPath, manifest.Files[0].Path)) ?? _config.ModsPath;
                // On passe le dossier parent des mods
                return _scanner.VerifyAgainstManifest(_config.ModsPath, manifest.Files);
            });

            // Supprimer les fichiers obsoletes (plus dans le manifeste)
            if (extra.Count > 0)
            {
                AddLog($"Nettoyage de {extra.Count} fichier(s) obsolete(s)...");
                var removed = await Task.Run(() => _downloader!.CleanExtraFiles(_config.ModsPath, manifest));
                foreach (var r in removed)
                    AddLog($"  Supprime: {r}");
                AddLog($"{removed.Count} fichier(s) obsolete(s) supprime(s)");
            }

            AddLog($"Resultat: {missing.Count} manquant(s), {modified.Count} modifie(s), {extra.Count} en trop");

            if (missing.Count == 0 && modified.Count == 0)
            {
                AddLog($"Tous les fichiers sont a jour ! ({manifest.FileCount} verifies)");

                ModStatusDetail.Text = $"{manifest.FileCount} fichiers - A jour";
                ModStatusDot.Fill = FindResource("SuccessBrush") as SolidColorBrush;
                _modsReady = true;
                BtnCheckMods.Content = "Mods OK";
                BtnCheckMods.IsEnabled = true;

                // Lancer le scan anti-triche
                await RunAntiCheatScanAsync();
            }
            else
            {
                int toDownload = missing.Count + modified.Count;
                AddLog($"  {missing.Count} manquant(s), {modified.Count} modifie(s)");
                ModStatusDetail.Text = $"{toDownload} fichier(s) a telecharger";
                ModStatusDot.Fill = (SolidColorBrush)FindResource("OrangeBrush");

                var result = MessageBox.Show(
                    $"{toDownload} fichier(s) doivent etre telecharges/mis a jour.\n\nLancer le telechargement ?",
                    "Mise a jour des mods",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await DownloadModsAsync(manifest);
                }
                else
                {
                    BtnCheckMods.Content = "Telecharger les mods";
                    BtnCheckMods.IsEnabled = true;
                }
            }
        }

        private async Task DownloadModsAsync(ModRepository manifest)
        {
            DownloadPanel.Visibility = Visibility.Visible;
            BtnCheckMods.IsEnabled = false;
            BtnCheckMods.Content = "Telechargement...";

            _downloadCts = new CancellationTokenSource();

            _downloader!.OnProgress += progress =>
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadFileName.Text = progress.FileName;
                    DownloadSpeed.Text = $"{progress.SpeedMbps:F1} MB/s";
                    DownloadCount.Text = $"{progress.CurrentFile}/{progress.TotalFiles}";
                    DownloadSize.Text = $"{progress.BytesDownloaded / 1024 / 1024} / {progress.TotalBytes / 1024 / 1024} MB";

                    if (progress.TotalBytes > 0)
                        DownloadProgress.Value = (double)progress.BytesDownloaded / progress.TotalBytes * 100;
                });
            };

            try
            {
                AddLog("Telechargement des mods...");
                var (downloaded, upToDate, errors) = await _downloader.SyncModsAsync(
                    _config.ModsPath, manifest, _downloadCts.Token);

                AddLog($"Termine : {downloaded} telecharge(s), {upToDate} deja a jour");
                if (errors.Count > 0)
                {
                    foreach (var err in errors)
                        AddLog($"  Erreur: {err}", true);
                }

                if (errors.Count == 0)
                {
                    ModStatusDetail.Text = $"{manifest.FileCount} fichiers - A jour";
                    ModStatusDot.Fill = FindResource("SuccessBrush") as SolidColorBrush;
                    _modsReady = true;

                    // Lancer le scan anti-triche
                    await RunAntiCheatScanAsync();
                }
                else
                {
                    ModStatusDetail.Text = $"{errors.Count} erreur(s)";
                    ModStatusDot.Fill = FindResource("DangerBrush") as SolidColorBrush;
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("Telechargement annule.");
            }
            finally
            {
                DownloadPanel.Visibility = Visibility.Collapsed;
                BtnCheckMods.IsEnabled = true;
                BtnCheckMods.Content = _modsReady ? "Mods OK" : "Reessayer";
                UpdatePlayButton();
            }
        }

        // ========================================
        // SCAN ANTI-TRICHE
        // ========================================
        private async Task RunAntiCheatScanAsync()
        {
            _scanDone = false;
            ScanProgress.Visibility = Visibility.Visible;
            ScanStatusText.Text = "Scan en cours...";
            AddLog("Scan anti-triche en cours...");

            _lastScan = await Task.Run(() =>
            {
                var result = new ScanResult();

                // Scanner UNIQUEMENT les mods requis du serveur
                foreach (var mod in _config.RequiredMods)
                {
                    var modPath = Path.Combine(_config.ModsPath, mod);
                    var modScan = _scanner.ScanMod(modPath);

                    result.Pbos.AddRange(modScan.Pbos);
                    result.Files.AddRange(modScan.Files);
                    result.TotalPbos += modScan.TotalPbos;
                    result.TotalFiles += modScan.TotalFiles;
                }

                // Verifier les fichiers en trop dans le mod (pas dans le manifeste = suspect)
                // Les autres mods du joueur ne sont PAS scannes
                result.GlobalHash = _scanner.ComputeScanHash(result, GetPlayerUid());
                return result;
            });

            ScanProgress.Visibility = Visibility.Collapsed;
            ScanPboCount.Text = _lastScan.TotalPbos.ToString();
            ScanFileCount.Text = _lastScan.TotalFiles.ToString();

            if (_lastScan.SuspiciousFiles.Count > 0)
            {
                ScanSuspicious.Text = _lastScan.SuspiciousFiles.Count.ToString();
                ScanSuspicious.Foreground = FindResource("DangerBrush") as SolidColorBrush;
                AddLog($"ATTENTION : {_lastScan.SuspiciousFiles.Count} fichier(s) suspect(s) detecte(s) !", true);
                foreach (var f in _lastScan.SuspiciousFiles)
                    AddLog($"  > {f}", true);
            }
            else
            {
                ScanSuspicious.Text = "0";
                ScanSuspicious.Foreground = FindResource("SuccessBrush") as SolidColorBrush;
            }

            AddLog($"Scan termine : {_lastScan.TotalPbos} PBOs, {_lastScan.TotalFiles} fichiers");

            // Envoyer le token au serveur
            await SendTokenAsync();
        }

        private async Task SendTokenAsync()
        {
            if (_lastScan == null) return;

            ScanStatusText.Text = "Envoi du token...";
            ScanTokenStatus.Text = "...";
            ScanTokenStatus.Foreground = FindResource("TextDimBrush") as SolidColorBrush;

            var scanHash = _scanner.ComputeScanHash(_lastScan, GetPlayerUid());
            var response = await _tokenService!.SendScanTokenAsync(
                GetPlayerUid(), GetPlayerName(), _lastScan, scanHash);

            if (response.Success)
            {
                ScanTokenStatus.Text = "Valide";
                ScanTokenStatus.Foreground = FindResource("SuccessBrush") as SolidColorBrush;
                ScanStatusText.Text = "Pret a jouer !";
                ScanStatusText.Foreground = FindResource("SuccessBrush") as SolidColorBrush;
                _scanDone = true;
                AddLog("Token anti-triche envoye et valide");
            }
            else
            {
                ScanTokenStatus.Text = "Erreur";
                ScanTokenStatus.Foreground = FindResource("DangerBrush") as SolidColorBrush;
                ScanStatusText.Text = response.Message;
                ScanStatusText.Foreground = FindResource("DangerBrush") as SolidColorBrush;
                AddLog($"Erreur token : {response.Message}", true);
                // Permettre de jouer quand meme si le serveur API est down
                _scanDone = true;
            }

            UpdatePlayButton();
        }

        // ========================================
        // BOUTON JOUER
        // ========================================
        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_config.ArmaPath))
            {
                MessageBox.Show("Configurez le chemin d'Arma 3 d'abord.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnPlay.IsEnabled = false;
            BtnPlay.Content = "Verification...";

            // === RE-SCAN AVANT LANCEMENT ===
            AddLog("Re-verification des mods avant lancement...");
            var finalCheck = await Task.Run(() =>
            {
                var result = new ScanResult();
                foreach (var mod in _config.RequiredMods)
                {
                    var modPath = Path.Combine(_config.ModsPath, mod);
                    var modScan = _scanner.ScanMod(modPath);
                    result.Pbos.AddRange(modScan.Pbos);
                    result.Files.AddRange(modScan.Files);
                    result.TotalPbos += modScan.TotalPbos;
                    result.TotalFiles += modScan.TotalFiles;
                }
                result.GlobalHash = _scanner.ComputeScanHash(result, GetPlayerUid());
                return result;
            });

            // Comparer le hash avec le scan initial
            if (_lastScan != null && finalCheck.GlobalHash != _lastScan.GlobalHash)
            {
                AddLog("ALERTE : Les fichiers ont ete modifies depuis le scan initial !", true);
                MessageBox.Show(
                    "Les fichiers du mod ont ete modifies depuis la verification.\n" +
                    "Le launcher va re-scanner et envoyer un nouveau token.",
                    "Fichiers modifies", MessageBoxButton.OK, MessageBoxImage.Warning);

                _lastScan = finalCheck;
                await SendTokenAsync();
                BtnPlay.IsEnabled = true;
                BtnPlay.Content = "\u25B6  JOUER";
                return;
            }

            // Renvoyer un token frais (l'ancien a peut-etre expire)
            AddLog("Envoi du token final...");
            _lastScan = finalCheck;
            var tokenResponse = await _tokenService!.SendScanTokenAsync(
                GetPlayerUid(), GetPlayerName(), finalCheck,
                _scanner.ComputeScanHash(finalCheck, GetPlayerUid()));

            if (!tokenResponse.Success)
            {
                AddLog($"Erreur token: {tokenResponse.Message}", true);
                // On laisse jouer quand meme si l'API est down
            }

            BtnPlay.Content = "Lancement...";
            AddLog("Lancement d'Arma 3...");

            await Task.Delay(500);

            if (_armaLauncher!.LaunchArma(_config.ArmaPath, _config.ModsPath))
            {
                AddLog("Arma 3 lance ! Bon jeu sur LifeExpensive RP !");
                await Task.Delay(3000);
                Application.Current.Shutdown();
            }
            else
            {
                AddLog("Erreur : impossible de lancer Arma 3.", true);
                MessageBox.Show("Impossible de lancer Arma 3.\nVerifiez le chemin d'installation.",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnPlay.IsEnabled = true;
                BtnPlay.Content = "\u25B6  JOUER";
            }
        }

        private void UpdatePlayButton()
        {
            BtnPlay.IsEnabled = _modsReady && _scanDone && !string.IsNullOrEmpty(_config.ArmaPath) && !_isMaintenanceMode && !_isWhitelistBlocked;
            if (_isMaintenanceMode)
            {
                BtnPlay.Content = "MAINTENANCE";
            }
            else if (_isWhitelistBlocked)
            {
                BtnPlay.Content = "WHITELIST";
            }
            else
            {
                BtnPlay.Content = "\u25B6  JOUER";
            }
        }

        // ========================================
        // LIENS RAPIDES (TS, Discord, Site, TFR)
        // ========================================
        private void BtnTeamSpeak_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(_config.TeamSpeakUrl);
            AddLog("Connexion TeamSpeak...");
        }

        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            // Remplacer par votre lien Discord
            OpenUrl(_config.DiscordUrl);
            AddLog("Ouverture Discord...");
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(_config.WebsiteUrl);
        }

        private void BtnInstallTFR_Click(object sender, RoutedEventArgs e)
        {
            // Le plugin TFR est dans le boot de Uni-Launcher
            var tfrUrl = _config.TfrPluginUrl;
            var tempPath = Path.Combine(Path.GetTempPath(), "task_force_radio.ts3_plugin");

            BtnTFR.IsEnabled = false;
            TfrStatusText.Text = "Installation...";
            AddLog("Telechargement du plugin TFR...");

            Task.Run(async () =>
            {
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    var data = await http.GetByteArrayAsync(tfrUrl);
                    await File.WriteAllBytesAsync(tempPath, data);

                    // Lancer l'installation du plugin TS
                    Dispatcher.Invoke(() =>
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = tempPath,
                            UseShellExecute = true
                        });
                        TfrStatusText.Text = "TFR installe";
                        BtnTFR.IsEnabled = true;
                        AddLog("Plugin TFR telecharge et lance ! Acceptez dans TeamSpeak.");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TfrStatusText.Text = "Erreur TFR";
                        BtnTFR.IsEnabled = true;
                        AddLog($"Erreur TFR: {ex.Message}", true);
                    });
                }
            });
        }

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        // ========================================
        // PARAMETRES
        // ========================================
        private void ArmaPathText_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selectionnez le dossier d'Arma 3",
                ShowNewFolderButton = false
            };

            if (!string.IsNullOrEmpty(_config.ArmaPath))
                dialog.SelectedPath = _config.ArmaPath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (File.Exists(Path.Combine(dialog.SelectedPath, "arma3_x64.exe")) ||
                    File.Exists(Path.Combine(dialog.SelectedPath, "arma3.exe")))
                {
                    _config.ArmaPath = dialog.SelectedPath;
                    if (string.IsNullOrEmpty(_config.ModsPath))
                        _config.ModsPath = dialog.SelectedPath;
                    SaveSettings();
                    UpdatePathDisplay();
                    AddLog($"Chemin Arma 3 : {_config.ArmaPath}");
                }
                else
                {
                    MessageBox.Show("arma3_x64.exe non trouve dans ce dossier.",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void ModsPathText_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selectionnez le dossier des mods (contient @lifeexpensive_modspack)",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrEmpty(_config.ModsPath))
                dialog.SelectedPath = _config.ModsPath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _config.ModsPath = dialog.SelectedPath;
                SaveSettings();
                UpdatePathDisplay();
                AddLog($"Dossier mods : {_config.ModsPath}");
            }
        }

        private void UpdatePathDisplay()
        {
            ArmaPathText.Text = string.IsNullOrEmpty(_config.ArmaPath) ? "Cliquer pour configurer" : _config.ArmaPath;
            ModsPathText.Text = string.IsNullOrEmpty(_config.ModsPath) ? "Cliquer pour configurer" : _config.ModsPath;
        }

        // ========================================
        // SETTINGS PERSISTENCE
        // ========================================
        private void LoadSettings()
        {
            var path = Path.Combine(_settingsPath, "settings.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var saved = JsonConvert.DeserializeObject<LauncherConfig>(json);
                    if (saved != null)
                    {
                        _config.ArmaPath = saved.ArmaPath;
                        _config.ModsPath = saved.ModsPath;
                    }
                }
                catch { }
            }
        }

        private void SaveSettings()
        {
            var path = Path.Combine(_settingsPath, "settings.json");
            var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        // ========================================
        // UTILITAIRES
        // ========================================
        private string GetPlayerUid()
        {
            // Lire le UID Steam depuis le registry
            try
            {
                var steamUser = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam\ActiveProcess",
                    "ActiveUser", null);
                if (steamUser is int userId && userId > 0)
                {
                    long steamId64 = 76561197960265728L + userId;
                    return steamId64.ToString();
                }
            }
            catch { }
            return "unknown";
        }

        private string GetPlayerName()
        {
            try
            {
                var name = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam",
                    "LastGameNameUsed", null) as string;
                return name ?? Environment.UserName;
            }
            catch { return Environment.UserName; }
        }

        private void AddLog(string message, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                var prefix = isError ? "[!] " : "";
                LogTextBox.AppendText($"[{time}] {prefix}{message}\n");
                LogTextBox.ScrollToEnd();
            });
        }

        // ========================================
        // FOND (IMAGE / VIDEO) + MUSIQUE
        // ========================================
        // Fichiers media a placer dans le dossier "media" a cote du .exe :
        //   media/background.png  ou  background.jpg   (image de fond)
        //   media/background.mp4                        (video de fond, optionnel, prioritaire sur l'image)
        //   media/music1.mp3                            (musiques, toutes les .mp3/.ogg du dossier)
        //   media/music2.mp3
        //   etc.

        private readonly MediaPlayer _musicPlayer = new();
        private List<string> _playlist = new();
        private int _playlistIndex;
        private Random _playlistRandom = new();

        private async Task LoadBackgroundAsync()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var mediaDir = Path.Combine(exeDir, "media");
            Directory.CreateDirectory(mediaDir);

            // 1. Essayer de telecharger depuis le serveur (config distante)
            string? videoPath = null;
            string? imagePath = null;

            if (_remoteConfig != null)
            {
                if (!string.IsNullOrEmpty(_config.VideoUrl))
                {
                    videoPath = await _remoteConfig.DownloadMediaAsync(_config.VideoUrl, "background.mp4");
                }
                if (!string.IsNullOrEmpty(_config.BackgroundUrl))
                {
                    var ext = Path.GetExtension(new Uri(_config.BackgroundUrl).LocalPath);
                    imagePath = await _remoteConfig.DownloadMediaAsync(_config.BackgroundUrl, "background" + ext);
                }
            }

            // 2. Sinon, chercher en local dans le dossier media/
            if (videoPath == null)
                videoPath = FindMediaFile(mediaDir, "background", ".mp4", ".avi", ".wmv", ".webm");
            if (imagePath == null)
                imagePath = FindMediaFile(mediaDir, "background", ".png", ".jpg", ".jpeg", ".bmp");

            // 3. Afficher (video prioritaire sur image)
            if (videoPath != null)
                LoadBackgroundVideo(videoPath);
            else if (imagePath != null)
                LoadBackgroundImage(imagePath);

            // 4. Telecharger les musiques depuis le serveur + charger les locales
            await DownloadAndLoadMusicAsync(mediaDir);
        }

        private async Task DownloadAndLoadMusicAsync(string mediaDir)
        {
            var musicDir = Path.Combine(mediaDir, "music");
            Directory.CreateDirectory(musicDir);

            // Telecharger les musiques depuis le serveur
            if (_remoteConfig != null && _config.MusicUrls.Count > 0)
            {
                foreach (var url in _config.MusicUrls)
                {
                    try
                    {
                        var fileName = Path.GetFileName(new Uri(url).LocalPath);
                        var localPath = Path.Combine(musicDir, fileName);

                        // Ne re-telecharger que si absent
                        if (!File.Exists(localPath))
                        {
                            await _remoteConfig.DownloadMediaAsync(url, Path.Combine("music", fileName));
                            AddLog($"Musique telechargee: {fileName}");
                        }
                    }
                    catch { }
                }
            }

            // Charger toutes les musiques (telechargees + locales)
            LoadPlaylist(mediaDir);
        }

        private string? FindMediaFile(string dir, string baseName, params string[] extensions)
        {
            foreach (var ext in extensions)
            {
                var path = Path.Combine(dir, baseName + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private void LoadBackgroundVideo(string path)
        {
            try
            {
                BgVideo.Source = new Uri(path);
                BgVideo.Volume = _isMuted ? 0 : _bgVolume;
                BgVideo.Visibility = Visibility.Visible;
                BgImage.Visibility = Visibility.Collapsed;
                BgVideo.Play();
                AddLog($"Video de fond: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                AddLog($"Erreur video: {ex.Message}", true);
            }
        }

        private void LoadBackgroundImage(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                BgImage.Source = bitmap;
                BgImage.Visibility = Visibility.Visible;
                BgVideo.Visibility = Visibility.Collapsed;
                AddLog($"Image de fond: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                AddLog($"Erreur image: {ex.Message}", true);
            }
        }

        private void BgVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Boucle infinie pour la video
            BgVideo.Position = TimeSpan.Zero;
            BgVideo.Play();
        }

        private void BgVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            BgVideo.Volume = _isMuted ? 0 : _bgVolume;
        }

        // ========================================
        // PLAYLIST MUSIQUE
        // ========================================
        private void LoadPlaylist(string mediaDir)
        {
            _playlist.Clear();

            // Chercher les musiques dans media/, media/music/ et le cache
            var cacheMusic = Path.Combine(_settingsPath, "media", "music");
            string[] searchDirs = { mediaDir, Path.Combine(mediaDir, "music"), cacheMusic };
            string[] musicExtensions = { "*.mp3", "*.ogg", "*.wav", "*.wma", "*.flac" };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var pattern in musicExtensions)
                {
                    foreach (var file in Directory.GetFiles(dir, pattern))
                    {
                        if (!Path.GetFileNameWithoutExtension(file).StartsWith("background", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!_playlist.Contains(file))
                                _playlist.Add(file);
                        }
                    }
                }
            }

            if (_playlist.Count == 0) return;

            // Melanger la playlist
            for (int i = _playlist.Count - 1; i > 0; i--)
            {
                int j = _playlistRandom.Next(i + 1);
                (_playlist[i], _playlist[j]) = (_playlist[j], _playlist[i]);
            }

            _playlistIndex = 0;
            _musicPlayer.Volume = _isMuted ? 0 : _bgVolume;
            _musicPlayer.MediaEnded += MusicPlayer_MediaEnded;

            PlayCurrentTrack();
            AddLog($"Playlist: {_playlist.Count} musique(s) chargee(s)");
        }

        private void PlayCurrentTrack()
        {
            if (_playlist.Count == 0) return;

            try
            {
                _musicPlayer.Open(new Uri(_playlist[_playlistIndex]));
                _musicPlayer.Volume = _isMuted ? 0 : _bgVolume;
                _musicPlayer.Play();

                // Afficher le nom de la musique
                var trackName = Path.GetFileNameWithoutExtension(_playlist[_playlistIndex])
                    .Replace('_', ' ');
                Dispatcher.Invoke(() =>
                {
                    NowPlayingText.Text = $"\u266B {trackName}";
                });
            }
            catch { }
        }

        private void MusicPlayer_MediaEnded(object? sender, EventArgs e)
        {
            // Passer a la musique suivante
            _playlistIndex = (_playlistIndex + 1) % _playlist.Count;
            Dispatcher.Invoke(() => PlayCurrentTrack());
        }

        /// <summary>Passer a la musique suivante (appele par le bouton)</summary>
        public void NextTrack()
        {
            if (_playlist.Count == 0) return;
            _playlistIndex = (_playlistIndex + 1) % _playlist.Count;
            PlayCurrentTrack();
            AddLog($"Musique: {Path.GetFileNameWithoutExtension(_playlist[_playlistIndex])}");
        }

        /// <summary>Passer a la musique precedente</summary>
        public void PrevTrack()
        {
            if (_playlist.Count == 0) return;
            _playlistIndex = (_playlistIndex - 1 + _playlist.Count) % _playlist.Count;
            PlayCurrentTrack();
            AddLog($"Musique: {Path.GetFileNameWithoutExtension(_playlist[_playlistIndex])}");
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e) => PrevTrack();
        private void BtnNext_Click(object sender, RoutedEventArgs e) => NextTrack();

        private void MaintenanceBanner_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowPopup("⚠", "MAINTENANCE", _maintenanceMessage);
        }

        private void WhitelistBanner_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowPopup("🔒", "WHITELIST", _whitelistMessage + "\n\nCliquez sur le bouton Site Web pour vous inscrire.");
        }

        // ========================================
        // POPUP CUSTOM
        // ========================================
        private void ShowPopup(string icon, string title, string message)
        {
            PopupIcon.Text = icon;
            PopupTitle.Text = title;
            PopupMessage.Text = message;
            PopupOverlay.Visibility = Visibility.Visible;
        }

        private void PopupOk_Click(object sender, RoutedEventArgs e)
        {
            PopupOverlay.Visibility = Visibility.Collapsed;
        }

        private void PopupOverlay_Close(object sender, MouseButtonEventArgs e)
        {
            PopupOverlay.Visibility = Visibility.Collapsed;
        }

        private void PopupBox_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Empecher de fermer en cliquant sur la popup elle-meme
        }

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;

            // Video
            BgVideo.Volume = _isMuted ? 0 : _bgVolume;

            // Musique
            _musicPlayer.Volume = _isMuted ? 0 : _bgVolume;

            // Icone bouton
            BtnMute.Content = _isMuted ? "\U0001F507" : "\U0001F50A";
        }

        // ========================================
        // TOGGLES (JOURNAL / PARAMETRES)
        // ========================================
        private void BtnToggleLog_Click(object sender, RoutedEventArgs e)
        {
            if (LogPanel.Visibility == Visibility.Visible)
            {
                LogPanel.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
            }
            else
            {
                LogPanel.Visibility = Visibility.Visible;
                SettingsPanel.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                SettingsPanel.Visibility = Visibility.Visible;
                LogPanel.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _bgVolume = e.NewValue;
            if (!_isMuted)
            {
                _musicPlayer.Volume = _bgVolume;
                if (BgVideo != null) BgVideo.Volume = _bgVolume;
            }
        }

        // ========================================
        // FENETRE
        // ========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _musicPlayer.Stop();
            _musicPlayer.Close();
            BgVideo.Stop();
            Application.Current.Shutdown();
        }

        private void OpenWebsite_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://lifeexpensive.com");
        }
    }
}
