# LifeExpensive RP Launcher

Launcher officiel pour le serveur Arma 3 LifeExpensive RP.

## Fonctionnalites

- Verification et telechargement automatique des mods
- Systeme anti-triche (scan PBO + token signe HMAC-SHA256)
- Statut serveur en temps reel
- Playlist musique configurable
- Image/video de fond
- Mode maintenance et whitelist
- Liens rapides (TeamSpeak, Discord, Site Web, TFR)
- Changelog des mises a jour
- Installateur Inno Setup

## Technologies

- C# / WPF (.NET 10)
- Newtonsoft.Json
- Inno Setup (installateur)

## Configuration

Toute la configuration se fait via `launcher_config.json` sur le serveur boot.

## Build

```
dotnet publish LifeExpensiveLauncher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Licence

MIT License
