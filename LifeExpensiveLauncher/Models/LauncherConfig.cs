using System.Collections.Generic;

namespace LifeExpensiveLauncher.Models
{
    public class LauncherConfig
    {
        // Serveur (valeurs par defaut ecrasees par launcher_config.json distant)
        public string ServerHost { get; set; } = "0.0.0.0";
        public int ServerPort { get; set; } = 2302;
        public string ServerPassword { get; set; } = "";
        public string ServerName { get; set; } = "LifeExpensive RP";

        // Liens
        public string TeamSpeakUrl { get; set; } = "";
        public string DiscordUrl { get; set; } = "";
        public string WebsiteUrl { get; set; } = "";
        public string WhitelistUrl { get; set; } = "";

        // API
        public string ApiBaseUrl { get; set; } = "";
        public string ApiTokenEndpoint { get; set; } = "/api/launcher_token.php";
        public string ApiModsEndpoint { get; set; } = "/api/launcher_mods.php";
        public string ApiNewsEndpoint { get; set; } = "/api/launcher_news.php";
        public string ChangelogUrl { get; set; } = "";

        // Mods
        public string ModDownloadBaseUrl { get; set; } = "";
        public string ModManifestUrl { get; set; } = "";
        public List<string> RequiredMods { get; set; } = new() { "@lifeexpensivemods", "@lifeexpensivemaps", "@modsmaps2024" };

        // Media (telecharges depuis le serveur boot)
        public string BackgroundUrl { get; set; } = "";
        public string VideoUrl { get; set; } = "";
        public string TfrPluginUrl { get; set; } = "";
        public List<string> MusicUrls { get; set; } = new();

        // Anti-triche (ecrase par launcher_config.json distant)
        public string TokenSecret { get; set; } = "CHANGE_ME";

        // Chemins locaux (sauves dans settings.json du joueur)
        public string ArmaPath { get; set; } = "";
        public string ModsPath { get; set; } = "";
    }

    public class ModFileInfo
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public string Hash { get; set; } = "";
        public bool IsCompressed { get; set; }
        public long SizeCompressed { get; set; }
    }

    public class ModRepository
    {
        public int Version { get; set; }
        public int FileCount { get; set; }
        public List<ModFileInfo> Files { get; set; } = new();
    }

    public class NewsItem
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Date { get; set; } = "";
        public string Type { get; set; } = "info"; // info, update, warning, event

        public string TypeLabel => Type switch
        {
            "update" => "MISE A JOUR",
            "warning" => "ALERTE",
            "event" => "EVENEMENT",
            _ => "INFO"
        };

        public System.Windows.Media.SolidColorBrush TypeBrush => Type switch
        {
            "update" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#28a745")),
            "warning" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#dc3545")),
            "event" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ff8c00")),
            _ => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#17a2b8"))
        };
    }

    public class ChangelogEntry
    {
        public string Version { get; set; } = "";
        public string Date { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Changes { get; set; } = new();
    }

    public class ScanResult
    {
        public List<PboEntry> Pbos { get; set; } = new();
        public List<FileEntry> Files { get; set; } = new();
        public List<string> SuspiciousFiles { get; set; } = new();
        public string GlobalHash { get; set; } = "";
        public int TotalPbos { get; set; }
        public int TotalFiles { get; set; }
    }

    public class PboEntry
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
    }

    public class FileEntry
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
    }
}
