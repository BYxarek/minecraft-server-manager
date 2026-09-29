# Minecraft Server Manager

[Русский](README.ru.md) · [Documentation](docs/README.md) · [Changelog](changelog.md)

A native Windows application for managing local Minecraft Bedrock and Java servers. The interface uses WPF and does not start a local web server.

## Features

- Starts maximized and continues running in the system tray after the window is closed.
- Online player actions, a separate allow-list tab, and selection lists for game mode, difficulty, and default Bedrock permissions.
- Backup notifications and the backup list can reveal an archive in File Explorer.

- Any number of Bedrock and Java profiles, each with its own folder and port.
- First-run setup with links to the official server downloads. Add either server file or both; replace them later in **Server file settings**.
- Create servers from saved files or attach an existing server folder. The creation form offers only editions with an imported file and proposes `<application folder>/<server name>` as the folder.
- Start, stop, restart, send commands, search the console, and see process memory and state.
- Configure server properties with switches and fields, or edit `server.properties` below them; saving keeps a `.bak` copy. Configure Java memory and executable path separately.
- Progress indicators show server file installation, backup and restore, template import, and update checks. Without a selected server, the workspace prompts you to create or select one.
- Create and restore ZIP world backups while the server is stopped. Optional backups every 6, 12, or 24 hours while the application is running, including in the tray.
- Restart a server after an unexpected exit, up to three attempts in five minutes.
- Check [GitHub Releases](https://github.com/BYxarek/minecraft-server-manager/releases) for application updates at startup or on demand. Installation is manual.

## Run from source

Requires Windows 10/11 and the .NET 10 SDK. Java servers also require a compatible Java installation in `PATH` or a path to `java.exe` in the profile settings.

```powershell
dotnet run --project src/MinecraftServerManager/MinecraftServerManager.csproj
```

On first launch, download a [Bedrock ZIP](https://www.minecraft.net/en-us/download/server/bedrock) and/or [Java JAR](https://www.minecraft.net/en-us/download/server), then select them in the setup window. The application copies them to `%APPDATA%\MinecraftServerManager\templates`. Replacing a saved server file affects newly created profiles only. The local `deffolt-minecraft-server` folder is not used automatically.

For a new Java server, the application creates `eula.txt` with `eula=false`. Read the [Minecraft EULA](https://www.minecraft.net/en-us/eula) and, if you accept it, change the value to `eula=true` before starting. Profiles are stored in `%APPDATA%\MinecraftServerManager\servers.json`; worlds and backups remain in the folders you choose.

The interface defaults to English. Use the language selector in the sidebar to switch to Russian; the choice is saved in `%APPDATA%\MinecraftServerManager\settings.json`.

## Release build

```powershell
pwsh ./scripts/Build-Release.ps1
pwsh ./scripts/Build-Installer.ps1
```

The first command builds a portable ZIP; the second builds a Windows installer with Inno Setup 6. The installer shows the existing Java version and can download Eclipse Temurin Java 25 for the current user, add it to the user PATH, and save its path for the application. Both outputs go to `dist`. Set the version in `src/MinecraftServerManager/MinecraftServerManager.csproj` under `Version` and record changes in [changelog.md](changelog.md). Publishing a GitHub Release is a separate step. The repository does not use GitHub Actions.

## License and server files

The application code is distributed under the [Source-Available Noncommercial Share-Alike License 1.0](LICENSE). Modified versions require attribution, source availability, and the same license; commercial use is prohibited. This is **source-available**, not OSI-approved open source. The [Russian translation](LICENSE.ru.md) is provided for convenience; the English license controls.

Minecraft server files belong to Mojang/Microsoft. They are not included in this repository or the application archive and are not covered by this license. Obtain them from the [official website](https://www.minecraft.net/en-us/download/server).

This project is not affiliated with Mojang Studios or Microsoft.
