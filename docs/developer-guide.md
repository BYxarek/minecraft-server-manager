# Developer guide

[Русский](ru/developer-guide.md)

This guide describes version **0.2.1**. Minecraft Server Manager is a local WPF application for Windows 10/11 on .NET 10. The interface uses `MaterialDesignThemes` 5.2.1. There is no local web server or external database in the current application.

## Run and build

From the repository root:

```powershell
dotnet restore src/MinecraftServerManager/MinecraftServerManager.csproj
dotnet build src/MinecraftServerManager/MinecraftServerManager.csproj
dotnet run --project src/MinecraftServerManager/MinecraftServerManager.csproj
```

Building a release archive requires PowerShell 7 (`pwsh`):

```powershell
pwsh ./scripts/Build-Release.ps1
```

The script publishes a self-contained, single-file `win-x64` application and creates a ZIP under `dist`. It stops if that version's output directory or archive already exists. Set `Version` in the [project file](../src/MinecraftServerManager/MinecraftServerManager.csproj) and update the [changelog](../changelog.md). GitHub Releases are created and populated manually; the repository has no GitHub Actions workflow.

To build the Windows installer, install Inno Setup 6 and run:

```powershell
pwsh ./scripts/Build-Installer.ps1
```

The script reads the project version, publishes a fresh self-contained `win-x64` build, and compiles [`MinecraftServerManager.iss`](../installer/MinecraftServerManager.iss) with `ISCC.exe`. Pass `-IsccPath` if Inno Setup is installed elsewhere. The installer is written to `dist/MinecraftServerManager-v<version>-win-x64-setup.exe`. By default, existing installer output is not overwritten; a failed build keeps its staging directory for inspection. The script includes the published runtime files, excludes PDBs, and does not include Minecraft server binaries. The Inno Setup script defines per-user installation, x64 and Windows 10 requirements, English and Russian wizard languages, license display, shortcuts, app-close handling, optional launch, and an uninstaller. Keep its `AppId` stable across updates. User profiles, templates, worlds, and backups live outside the install directory and are preserved on uninstall. The executable is unsigned; sign it separately if distribution requires a trusted publisher identity.
For a rebuild of the same version, pass `-ReplaceExisting`; the new installer is compiled before it replaces the previous file.

The installer reads [`java-release.json`](../installer/java-release.json) and passes its pinned Temurin JRE 25 URL and SHA-256 to Inno Setup. The Java wizard page calls [`JavaSetup.ps1`](../installer/JavaSetup.ps1) in `Detect` mode to display the installed version. If the user opts in, Inno's download page verifies the archive, and `PrepareToInstall` runs the helper in `Install` mode before copying the application. The helper checks the Java major version, installs to `%LOCALAPPDATA%\MinecraftServerManager\Java25`, sets user PATH and JAVA_HOME, and writes `%APPDATA%\MinecraftServerManager\java.json`. The app uses the saved absolute executable path when present. Keep the URL, checksum, and expected major version in sync when updating the pinned Java release. The optional Java runtime survives application uninstall.

## Source layout

| File | Responsibility |
| --- | --- |
| [`App.xaml`](../src/MinecraftServerManager/App.xaml) | Starts `MainWindow` and defines visual resources. |
| [`MainWindow.xaml`](../src/MinecraftServerManager/MainWindow.xaml), [`MainWindow.xaml.cs`](../src/MinecraftServerManager/MainWindow.xaml.cs) | Tabs, commands, editor state, background work, and UI timers. |
| [`Localization.cs`](../src/MinecraftServerManager/Localization.cs), [`Resources`](../src/MinecraftServerManager/Resources/en.json) | Runtime language selection and English/Russian strings. |
| [`ServerProfile.cs`](../src/MinecraftServerManager/ServerProfile.cs), [`ProfileStore.cs`](../src/MinecraftServerManager/ProfileStore.cs) | Profile model and JSON persistence. |
| [`ServerManager.cs`](../src/MinecraftServerManager/ServerManager.cs) | Bedrock/Java processes, commands, logs, states, automatic restart, backups, and world restore. |
| [`PropertiesFile.cs`](../src/MinecraftServerManager/PropertiesFile.cs) | Reads and writes `server.properties` and individual keys. |
| [`SimplePropertiesEditor.cs`](../src/MinecraftServerManager/SimplePropertiesEditor.cs) | Builds switches and fields for common and existing server properties; applies only changed values to raw text. |
| [`JavaRuntime.cs`](../src/MinecraftServerManager/JavaRuntime.cs) | Reads the Java path saved by the installer and resolves default Java executables. |
| [`TemplateStore.cs`](../src/MinecraftServerManager/TemplateStore.cs), [`TemplateInstaller.cs`](../src/MinecraftServerManager/TemplateInstaller.cs) | Imports server files and prepares new server folders. |
| [`ServerDialog.cs`](../src/MinecraftServerManager/ServerDialog.cs), [`TemplateSetupDialog.cs`](../src/MinecraftServerManager/TemplateSetupDialog.cs), [`InlinePathBrowser.cs`](../src/MinecraftServerManager/InlinePathBrowser.cs) | Embedded forms and the folder viewer hosted inside `MainWindow`; system pickers select files and server folders. |
| [`UpdateChecker.cs`](../src/MinecraftServerManager/UpdateChecker.cs) | Checks the latest GitHub Release. |

`MainWindow` creates `ProfileStore`, `TemplateStore`, and `ServerManager`. At startup it loads profiles, passes the same list to the manager for shutdown, and opens file setup if no saved server file exists. A two-second UI timer refreshes displayed state; a one-minute timer checks backup schedules. `ServerManager.Changed` is forwarded to the UI thread through `Dispatcher`.

When `Selected` is null, `Refresh` collapses the tab control and shows `NoSelectionPanel`. Creating a server filters the edition list to those with imported templates. `ServerDialog` derives the default folder from `AppContext.BaseDirectory` and the sanitized server name until the user edits or browses to a custom folder. `TemplateInstaller` creates the target parent before staging the new server. Busy operations show indeterminate progress, so no percentage is implied.

Forms and confirmations use `InlineOverlay` in `MainWindow.xaml`. The form controls return a `Task<bool>` on save or cancel; the main window awaits completion and then removes the panel. Template file selection uses `OpenFileDialog`; server folder selection uses `OpenFolderDialog`. Both are owned by the main window and open the system Explorer picker. The first-run language button calls `Localization.SetLanguage`, and `MainWindow` synchronizes its sidebar selector. Icons use `PackIcon` with the shared `UiIcon` style and `IconContent` for forms built in C#. Keep application forms and confirmations in the overlay. External official download links still use the system browser. Keep all surfaces on the dark palette in `App.xaml`.

## Localization

English is the default. The language selector saves `en` or `ru` in `%APPDATA%\MinecraftServerManager\settings.json`. `Localization.Instance` loads that choice and embedded JSON dictionaries. XAML uses `{local:Loc Key}` for live-updating bindings; C# uses `T("Key")` or `F("Key", values...)` from the global static import. Add every new user-facing string to **both** `Resources/en.json` and `Resources/ru.json`. Keep server protocol values, paths, and JSON property names language-independent.

The current window refreshes calculated status labels after a language change. Newly opened dialogs read the current language. Text already emitted to the console remains as historical output.

## Profile and data files

`ServerProfile` has `Id`, `Name`, `Edition`, `Directory`, `Port`, `MemoryMb`, `JavaPath`, `AutoRestart`, `BackupIntervalHours`, and `LastBackupUtc`. `Endpoint` is derived as `127.0.0.1:Port` and is excluded from JSON.

| Location | Contents |
| --- | --- |
| `%APPDATA%\MinecraftServerManager\settings.json` | Selected application language. Missing or invalid settings default to English. |
| `%APPDATA%\MinecraftServerManager\java.json` | Java executable path saved by the optional installer step. |
| `%APPDATA%\MinecraftServerManager\servers.json` | Profiles. `ProfileStore.Save` writes a `.tmp` file and replaces the original. |
| `%APPDATA%\MinecraftServerManager\templates\templates.json` | Original filename and import time for each saved server file. |
| `%APPDATA%\MinecraftServerManager\templates\bedrock.zip` and `server.jar` | Copies of server files selected by the user. |
| `<Directory>\server.properties` | Server settings. Saving creates `server.properties.bak` from the previous file. |
| `<Directory>\backups\*.zip` | Backups for that profile. |
| `<Directory>\whitelist.json` or `allowlist.json` | Java or Bedrock allow list; direct edits preserve a `.bak` file. |

The template and profile stores do not contain worlds. Server folders are user-selected, so moving only `servers.json` does not move server data. `TemplateStore.Import` checks the extension and the presence of `bedrock_server.exe` or `META-INF/MANIFEST.MF` in the archive before replacing its saved copy. `TemplateInstaller` prepares a new server in a temporary sibling folder and moves it into the selected empty directory after preparation succeeds.

## Server lifecycle

`ServerManager.Start` launches `bedrock_server.exe` from the profile folder or Java with `-Xmx<MemoryMb>M -jar server.jar nogui`. Before a Java start, it checks `eula.txt`. Standard input, output, and error are redirected. The in-memory log is trimmed after it exceeds 200,000 characters; it is not written to disk.

States are `Stopped`, `Starting`, `Running`, `Stopping`, `Restarting`, and `Failed`. `Running` means the process is alive, not that the game server has announced readiness. `Stop` sends `stop`, waits up to 30 seconds, then terminates the process tree if needed. `StopAll` runs on explicit exit from the tray menu.

After an unexpected exit with `AutoRestart` enabled, the manager waits three seconds and tries again. It allows up to three attempts in a rolling five-minute window. Manual stop, profile removal, and application exit cancel a pending attempt. Access to the log uses a `StringBuilder` lock; process operations coordinate through `processGate` and dictionaries keyed by profile `Id`.

## Settings consistency

The profile save handler in `MainWindow` validates the name, port, Java memory, and port conflicts within the same edition. It then updates `server-port` in `server.properties` and saves JSON. If saving fails, it restores the previous profile values and attempts to restore the previous properties text.

The simple properties editor and raw `server.properties` editor share a save handler. Simple controls apply only values changed since loading, leaving other raw edits and comments intact. The handler rejects a `server-port` that differs from the profile's port. Profile fields, simple controls, and raw file text participate in unsaved-change detection, so switching profiles, removing one, or exiting from the tray warns about edits. `PropertiesFile.Write` uses a `.tmp` file and keeps a `.bak` copy of the previous version.

## Player management and tray

`MainWindow` shows Java memory and executable settings only for Java profiles and preserves those stored values when saving a Bedrock profile. `SimplePropertiesEditor` filters edition-only keys from the other edition, including when they already exist in `server.properties`; the raw editor leaves the file visible. The Overview's Open folder action starts Windows File Explorer for the selected profile directory.

The Overview's Delete server action confirms the exact folder and removes both the profile and its files. `ServerFolderDeletion` rejects drive roots, protected application/system locations, reparse-point server roots, and directories overlapping another profile. Its recursive deletion removes directory links themselves without traversing them. The server must be stopped and have no backup operation in progress. The profile is saved as removed before file deletion; on a deletion error, the handler restores the profile and saves it again. A partially deleted folder may still require manual inspection.

`ServerManager` parses responses to the console `list` command into an in-memory online player list. `MainWindow` requests a refresh about every 10 seconds while the player tab is visible. Player actions validate and quote names before sending commands. Java list changes use server commands while running; offline Java removal edits `whitelist.json`, while adding requires a running server to resolve UUIDs. Bedrock uses `allowlist.json` offline. The allow-list toggle updates `server.properties` and sends an on/off command when the server is running.

The window starts maximized. Its close button hides it; `NotifyIcon` restores it by double-click or menu. Explicit tray exit checks unsaved settings, waits for file operations, stops managed servers, and disposes the icon.

## Backups

`ServerManager.CreateBackup` runs only while the server is stopped. Java uses `level-name` from `server.properties`, defaulting to `world`; Bedrock archives the entire `worlds` directory. The ZIP contains the source directory's contents. Filenames include the edition, date, milliseconds, and a random identifier. An incomplete ZIP is deleted on failure.

`RestoreBackup` accepts only ZIP files from that profile's `backups` directory. It extracts into a temporary sibling directory and temporarily renames the existing world. If replacement fails, it attempts to move the previous world back. After success, it deletes the temporary previous copy.

The schedule is stored on the profile: `0` disables it; the UI offers 6, 12, and 24 hours. The application checks once a minute while open, skipping running servers and those waiting to restart. After a successful backup, it updates `LastBackupUtc` and saves profiles. After failure, an in-memory retry delay of one hour applies. Old ZIP files are not removed automatically. Backup, restore, and file import use `Task.Run`; the window waits for file operations before closing.

## Updates and extension points

`UpdateChecker` calls GitHub's `releases/latest` endpoint with a ten-second timeout, compares the release tag with the assembly version, and returns the release page link. A `SemaphoreSlim` serializes checks, and `%APPDATA%\\MinecraftServerManager\\update-check.json` stores the last result and next allowed UTC time. Automatic and manual checks share a 15-minute minimum interval across restarts. `Retry-After` and `X-RateLimit-Reset` extend it when GitHub reports a limit. Cache write failures leave the in-memory cooldown active. Download and installation are not implemented.

When adding a profile setting, update `ServerProfile`, the controls in `MainWindow.xaml`, field loading and saving in `MainWindow.xaml.cs`, both localization JSON files, and both documentation languages. For new world file operations, account for process state, errors, and work off the UI thread. Mojang/Microsoft server binaries must not be added to this repository or application release.
