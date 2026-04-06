using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LifeExpensiveLauncher.Models;
using Microsoft.Win32;

namespace LifeExpensiveLauncher.Services
{
    public class ArmaLauncher
    {
        private readonly LauncherConfig _config;

        public ArmaLauncher(LauncherConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Detecte automatiquement le chemin d'installation d'Arma 3
        /// </summary>
        public string? DetectArmaPath()
        {
            // 1. Steam Registry
            try
            {
                var steamPath = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath", null) as string;

                if (steamPath != null)
                {
                    var armaPath = Path.Combine(steamPath, "steamapps", "common", "Arma 3");
                    if (Directory.Exists(armaPath) && File.Exists(Path.Combine(armaPath, "arma3_x64.exe")))
                        return armaPath;

                    // Chercher dans les library folders
                    var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraryFile))
                    {
                        var lines = File.ReadAllLines(libraryFile);
                        foreach (var line in lines)
                        {
                            if (line.Contains("\"path\""))
                            {
                                var path = line.Split('"').Where(s => s.Contains(":\\") || s.Contains(":/")).FirstOrDefault();
                                if (path != null)
                                {
                                    var testPath = Path.Combine(path, "steamapps", "common", "Arma 3");
                                    if (Directory.Exists(testPath) && File.Exists(Path.Combine(testPath, "arma3_x64.exe")))
                                        return testPath;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Arma 3 Registry direct
            try
            {
                var armaReg = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\bohemia interactive\arma 3",
                    "main", null) as string;
                if (armaReg != null && Directory.Exists(armaReg))
                    return armaReg;
            }
            catch { }

            // 3. Chemins communs
            string[] commonPaths = {
                @"C:\Program Files (x86)\Steam\steamapps\common\Arma 3",
                @"D:\Steam\steamapps\common\Arma 3",
                @"D:\SteamLibrary\steamapps\common\Arma 3",
                @"E:\Steam\steamapps\common\Arma 3",
                @"E:\SteamLibrary\steamapps\common\Arma 3",
                @"F:\Steam\steamapps\common\Arma 3",
                @"F:\SteamLibrary\steamapps\common\Arma 3"
            };

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "arma3_x64.exe")))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Lance Arma 3 avec les parametres de connexion
        /// </summary>
        public bool LaunchArma(string armaPath, string modsPath)
        {
            // Priorite : BattlEye launcher -> arma3_x64 -> arma3
            var exePath = Path.Combine(armaPath, "arma3battleye.exe");
            if (!File.Exists(exePath))
            {
                exePath = Path.Combine(armaPath, "arma3_x64.exe");
                if (!File.Exists(exePath))
                    exePath = Path.Combine(armaPath, "arma3.exe");
            }

            if (!File.Exists(exePath))
                return false;

            // Construire la ligne de commande
            var modDirs = string.Join(";", _config.RequiredMods.Select(m => Path.Combine(modsPath, m)));

            var args = $"2 1 -connect={_config.ServerHost} -port={_config.ServerPort} " +
                       $"\"-mod={modDirs}\" -nosplash -world=empty -skipIntro";

            // arma3battleye.exe attend "2 1" comme premiers args (2=x64, 1=avec BE)
            // Si on lance directement arma3_x64.exe, pas besoin de ces args
            if (!exePath.Contains("battleye", StringComparison.OrdinalIgnoreCase))
            {
                args = $"-connect={_config.ServerHost} -port={_config.ServerPort} " +
                       $"\"-mod={modDirs}\" -nosplash -world=empty -skipIntro";
            }

            if (!string.IsNullOrEmpty(_config.ServerPassword))
                args += $" -password={_config.ServerPassword}";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    WorkingDirectory = armaPath,
                    UseShellExecute = true
                };

                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
