using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LifeExpensiveLauncher.Models;

namespace LifeExpensiveLauncher.Services
{
    /// <summary>
    /// Port C# de pbocheck.c — scanne les PBOs et fichiers des mods
    /// </summary>
    public class PboScanner
    {
        // Extensions suspectes (hack menus, injectors, etc.)
        private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf", ".hta"
        };

        // Noms de fichiers suspects connus
        private static readonly string[] SuspiciousNames = new[]
        {
            "cheat", "hack", "inject", "bypass", "trainer", "godmode", "aimbot",
            "esp", "wallhack", "speedhack", "teleport", "spawn_menu", "debug_console",
            "infistar_bypass", "battleye_bypass", "be_bypass"
        };

        // Fichiers Arma 3 legitimes (pas suspects)
        private static readonly HashSet<string> ArmaLegitFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "arma3.exe", "arma3_x64.exe", "arma3server.exe", "arma3server_x64.exe",
            "arma3battleye.exe", "arma3launcher.exe"
        };

        /// <summary>
        /// Scanne un dossier de mod : PBOs + tous les fichiers
        /// </summary>
        public ScanResult ScanMod(string modPath)
        {
            var result = new ScanResult();

            if (!Directory.Exists(modPath))
                return result;

            try
            {
                // Scanner les PBOs dans addons/
                var addonsPath = Path.Combine(modPath, "addons");
                if (Directory.Exists(addonsPath))
                {
                    foreach (var file in Directory.GetFiles(addonsPath, "*.pbo"))
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            result.Pbos.Add(new PboEntry
                            {
                                Name = info.Name.ToLowerInvariant(),
                                Size = info.Length
                            });
                        }
                        catch { }
                    }
                }

                // Scanner TOUS les fichiers (racine + addons, sauf .pbo et .bisign)
                ScanDirectory(modPath, "", result);
                if (Directory.Exists(addonsPath))
                {
                    ScanDirectory(addonsPath, "addons\\", result);
                }
            }
            catch { /* Ignorer les erreurs d'acces (symlinks, junctions, etc.) */ }

            // Trier
            result.Pbos = result.Pbos.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            result.Files = result.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

            result.TotalPbos = result.Pbos.Count;
            result.TotalFiles = result.Files.Count;

            // Hash global (meme algorithme djb2 que pbocheck.c)
            result.GlobalHash = ComputeGlobalHash(result.Pbos);

            return result;
        }

        /// <summary>
        /// Scan complet du dossier Arma pour fichiers suspects
        /// </summary>
        public List<string> ScanForSuspiciousFiles(string armaPath, List<string>? allowedMods = null)
        {
            var suspicious = new List<string>();

            if (!Directory.Exists(armaPath))
                return suspicious;

            // Mods autorises (pas suspects)
            var allowedModDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (allowedMods != null)
            {
                foreach (var mod in allowedMods)
                    allowedModDirs.Add(Path.Combine(armaPath, mod).ToLowerInvariant());
            }

            try
            {
                // Scanner la racine d'Arma (ignorer les exe Arma legitimes)
                foreach (var file in Directory.GetFiles(armaPath))
                {
                    var name = Path.GetFileName(file);
                    var ext = Path.GetExtension(file).ToLowerInvariant();

                    if (ArmaLegitFiles.Contains(name))
                        continue;

                    if (IsSuspiciousFile(name.ToLowerInvariant(), ext))
                        suspicious.Add(file);
                }

                // Scanner les dossiers de mods (ignorer les mods autorises)
                foreach (var dir in Directory.GetDirectories(armaPath, "@*"))
                {
                    if (allowedModDirs.Contains(dir.ToLowerInvariant()))
                        continue;

                    ScanDirForSuspicious(dir, suspicious);
                }
            }
            catch { }

            return suspicious;
        }

        /// <summary>
        /// Verifie l'integrite des mods par rapport au manifeste serveur
        /// </summary>
        public (List<string> missing, List<string> modified, List<string> extra) VerifyAgainstManifest(
            string modPath, List<ModFileInfo> manifest)
        {
            var missing = new List<string>();
            var modified = new List<string>();
            var extra = new List<string>();

            var manifestDict = manifest.ToDictionary(f => f.Path.ToLowerInvariant(), f => f);

            // Verifier chaque fichier du manifeste
            foreach (var entry in manifest)
            {
                var localPath = Path.Combine(modPath, entry.Path);
                if (!File.Exists(localPath))
                {
                    missing.Add(entry.Path);
                }
                else
                {
                    var info = new FileInfo(localPath);
                    if (info.Length != entry.Size)
                    {
                        modified.Add(entry.Path);
                    }
                }
            }

            // Chercher les fichiers en trop (pas dans le manifeste)
            try
            {
                if (!Directory.Exists(modPath)) return (missing, modified, extra);
                foreach (var file in Directory.EnumerateFiles(modPath, "*", new EnumerationOptions
                    { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    var relative = Path.GetRelativePath(modPath, file).ToLowerInvariant().Replace('/', '\\');
                    // Ignorer .bisign
                    if (relative.EndsWith(".bisign", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Construire le chemin avec le nom du mod
                    if (!manifestDict.ContainsKey(relative))
                    {
                        extra.Add(relative);
                    }
                }
            }
            catch { /* Ignorer les erreurs d'acces */ }

            return (missing, modified, extra);
        }

        /// <summary>
        /// Genere un hash SHA256 complet du scan pour le token
        /// </summary>
        public string ComputeScanHash(ScanResult result, string playerUid)
        {
            using var sha = SHA256.Create();
            var sb = new StringBuilder();

            sb.Append(playerUid);
            foreach (var pbo in result.Pbos)
            {
                sb.Append(pbo.Name);
                sb.Append(pbo.Size);
            }
            foreach (var file in result.Files)
            {
                sb.Append(file.Name);
                sb.Append(file.Size);
            }
            sb.Append(result.TotalPbos);
            sb.Append(result.SuspiciousFiles.Count);

            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private void ScanDirectory(string dirPath, string prefix, ScanResult result)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dirPath))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".pbo" || ext == ".bisign")
                        continue;

                    var info = new FileInfo(file);
                    var name = string.IsNullOrEmpty(prefix)
                        ? info.Name.ToLowerInvariant()
                        : (prefix + info.Name).ToLowerInvariant();

                    result.Files.Add(new FileEntry
                    {
                        Name = name,
                        Size = info.Length
                    });

                    // Check suspect
                    if (IsSuspiciousFile(info.Name.ToLowerInvariant(), ext))
                        result.SuspiciousFiles.Add(file);
                }
            }
            catch { /* Ignorer erreurs acces */ }
        }

        private void ScanDirForSuspicious(string dirPath, List<string> suspicious)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file).ToLowerInvariant();
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (IsSuspiciousFile(name, ext))
                        suspicious.Add(file);
                }
            }
            catch { }
        }

        private bool IsSuspiciousFile(string name, string ext)
        {
            // Extensions suspectes dans un dossier de mod
            if (SuspiciousExtensions.Contains(ext))
                return true;

            // Noms suspects
            foreach (var suspect in SuspiciousNames)
            {
                if (name.Contains(suspect))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Hash djb2 identique a pbocheck.c
        /// </summary>
        private string ComputeGlobalHash(List<PboEntry> pbos)
        {
            uint hash = 5381;
            foreach (var pbo in pbos)
            {
                foreach (char c in pbo.Name)
                {
                    hash = ((hash << 5) + hash) + (byte)c;
                }
                hash = ((hash << 5) + hash) + (uint)(pbo.Size & 0xFFFFFFFF);
                hash = ((hash << 5) + hash) + (uint)(pbo.Size >> 32);
            }
            return hash.ToString();
        }
    }
}
