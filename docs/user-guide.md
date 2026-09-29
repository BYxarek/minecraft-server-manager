# User guide

[Русский](ru/user-guide.md)

Minecraft Server Manager 0.2.1 is a Windows 10/11 application for running and controlling local Minecraft Bedrock and Java servers. You download the server files separately; they are not bundled with the application.

## Requirements

- **Bedrock:** an official Bedrock Dedicated Server ZIP.
- **Java:** a server JAR and a compatible Java installation in `PATH`, or a path to `java.exe` in the profile settings.
- **Running from source:** the .NET 10 SDK. The release archive contains a self-contained .NET application.

The first-run panel and **Server file settings** link to the official [Bedrock](https://www.minecraft.net/en-us/download/server/bedrock) and [Java](https://www.minecraft.net/en-us/download/server) downloads. Setup, server forms, and confirmations appear inside the main application window. **Choose** and **Browse** open Windows File Explorer dialogs for selecting a file or server folder. Official download links open your web browser.

## Install or remove the application

Run `MinecraftServerManager-v0.2.1-win-x64-setup.exe` on Windows 10/11 x64. The installer offers English and Russian, installs for your Windows account without administrator rights, creates a Start menu shortcut, and optionally creates a desktop shortcut. It can launch the application when setup finishes. Close the application before updating so running servers can stop cleanly. Remove it through Windows **Installed apps**. Uninstalling leaves `%APPDATA%\MinecraftServerManager` and your chosen server folders in place, including worlds and backups.

The installer reports whether Java is already installed and shows its version and executable path. If you plan to create Java Edition servers, select the optional Eclipse Temurin Java 25 installation. This downloads a Java 25 runtime, verifies its SHA-256 checksum, installs it under `%LOCALAPPDATA%\MinecraftServerManager\Java25`, adds its `bin` directory to your user PATH, and saves the executable path for the application. An internet connection is needed for this option; the application itself can be installed without Java. The Java runtime remains installed after removing the application.

## First launch and server files

1. Open the application. If there are no saved server files, the setup panel appears in the main window.
2. Download the server edition you need and select a Bedrock ZIP, a Java JAR, or both.
3. Select **Save files**. The application copies them to `%APPDATA%\MinecraftServerManager\templates`.

You can select **Later** and attach an existing server. To create a new one, first add the file for its edition through **Server file settings**. Replacing a saved file affects new servers only; it does not update existing server folders.

The interface starts in English. Use the language button in the upper-right corner of the first-run panel or the language selector in the sidebar to switch to Russian. The choice is saved for the next launch.

## Create or attach a server

### Create a new server

1. Select **Create server**. If no server is selected, the main area shows a prompt to create or select one instead of tabs.
2. Enter a name, edition, folder, and port. Only editions with an imported server file are offered. The initial folder is `<application installation folder>/<server name>` and follows name edits. Enter or browse to a custom path to keep it independent of the name.
3. Select **Create**. The application creates the folder if needed and copies the saved server files into it. Installation progress is indicated in the main window.

For a new Java server, the application creates `eula.txt` with `eula=false`. Read the [Minecraft EULA](https://www.minecraft.net/en-us/eula). If you accept it, change the value to `eula=true` in the server folder before starting.

### Attach an existing server

Select **Attach folder**, then choose the edition, folder, and port. The folder must contain `bedrock_server.exe` or `server.jar` at its root. Attaching creates a profile without copying server files.

A folder cannot be attached twice. Profiles of the same edition cannot use the same port. The displayed `127.0.0.1:port` address is for connecting from the same computer; the application does not configure a firewall or networking.

## Start, stop, and use the console

- **Start** launches the selected server process. The status reports starting, running, stopping, waiting to restart, or failure. **Process running** does not mean the game server is ready; check its console output.
- **Stop** sends `stop`. If the server does not exit within 30 seconds, the application terminates the process tree.
- **Restart** stops and then starts the server.
- **Open folder** on Overview opens the selected server directory in Windows File Explorer.
- The **Console** tab lets you send commands and search the current log. Search shows the number of matches and navigates to the first one.

The console log is held in memory and is not saved when the application exits. Closing the window minimizes the application to the notification area; servers and scheduled backups keep running. Double-click the tray icon or choose **Show window** to restore it. Choose **Exit application** in the tray menu to stop the launched servers and exit. The window starts maximized.

## Profile settings and `server.properties`

On the **Settings** tab, change the server name, port, maximum Java memory, Java executable path, or automatic restart setting. Select **Save settings** when finished. The port is also updated in `server.properties`. Java memory must be between 512 and 131072 MB; this setting does not affect Bedrock. New Java profiles use the Java 25 path saved by the installer when available; you can override it per profile.

Java memory and executable path are shown only for Java profiles. The simple options list also hides properties specific to the other edition. The advanced text editor still shows the complete `server.properties` file.

Below the profile fields, common `server.properties` values and other existing keys appear as switches and fields. Game mode, difficulty, and the Bedrock default player permission use selection lists. Select **Save server options** to apply them. The raw file editor remains below for advanced changes. Both save buttons write the same file and create a `.bak` copy. Save property changes before saving profile changes.

Edit `server.properties` in the lower part of the tab and select **Save server.properties**. The previous file is saved next to it as `.bak`. Change the port through the profile's **Port** field: saving `server.properties` with a different `server-port` is rejected.

Animated progress indicators appear while importing server files, installing a new server, creating or restoring a backup, and checking for updates. For operations without a measurable byte count, the progress bar indicates that work is ongoing rather than showing a percentage.

Restart the server to apply `server.properties` changes. The application warns about unsaved edits when you switch servers or exit through the tray menu.

Automatic restart makes at most three attempts within five minutes, with a three-second delay before each new start. **Stop**, profile removal, or closing the application cancels a pending restart.

## Players and allow list

The **Online players** tab requests the server’s `list` command and refreshes about every 10 seconds while the tab is open. Select a player to kick, grant or revoke operator status, add them to the allow list, change their game mode, or send a private message. Java also offers **Ban** and **Unban player** by name. These commands require a server started by this application. Use the Console tab for other supported server commands.

The **Allow list** tab enables or disables the list and shows the names in `whitelist.json` (Java) or `allowlist.json` (Bedrock). Add or remove names there. A running server handles the changes through its commands; while stopped, Bedrock names can be added or removed and Java names can be removed. Start the Java server before adding a name so it can resolve the player UUID. File edits keep a `.bak` copy.

## Back up and restore a world

### Manual backup

1. Stop the server.
2. Select **Back up world** on the Overview or Backups and files tab.
3. Wait for the message containing the ZIP path. Select **Show in Explorer** in the notification, or select an archive in **Backups and files** and use the same button to reveal its file.

Archives are saved in `<server folder>\backups`. Java copies the directory named by `level-name` in `server.properties` (default: `world`). Bedrock copies the whole `worlds` directory. If the world does not exist yet, the application reports an error. Backups are on the same drive as the server; move important copies elsewhere yourself if you need protection from drive failure.

### Automatic backup

On **Backups and files**, choose **Every 6 hours**, **Every 12 hours**, or **Every 24 hours**. Scheduling is disabled by default. The application checks about once a minute **while it is running, including in the tray** and copies only stopped servers. If the interval passes while a server is running, the backup is made at a later check after the server stops. Following a failure, the next attempt is delayed at least one hour. Old ZIP files are not deleted automatically.

### Restore a backup

1. Stop the server and select a ZIP in the list on **Backups and files**.
2. Select **Restore selected backup** and confirm replacement of the current world.
3. Wait for the completion message, then start the server.

The application can restore archives from this server's `backups` folder. During replacement, it temporarily keeps the current world so it can roll back if the operation fails. After success, that temporary world is deleted. Back up the current world first if you want to keep a separate copy.

## Updates and profile removal

The application checks for new versions at startup and when you select **Check for updates**. If a new version exists, it opens its GitHub Release page; installation is manual. Updating the application does not replace Minecraft server files.

Checks are limited to one GitHub request every 15 minutes, including across application restarts. Repeated clicks show when the next check is available and reuse the previous result. If GitHub asks the application to wait longer because of a rate limit, that time is respected.

**Delete server** on Overview permanently removes the profile and its entire server folder, including worlds, backups, and other files in that folder. Stop the server first. The confirmation shows the exact path. Folders that overlap another saved server or contain the application or a system location cannot be deleted. If deletion fails, the profile is restored so you can inspect the folder and retry.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| No saved file for the edition | Add a Bedrock ZIP or Java JAR in **Server file settings**. |
| `bedrock_server.exe` or `server.jar` not found | Check the profile folder and make sure the file is at its root. |
| Java requires `eula=true` | Read the EULA and, if you agree, change `eula.txt` in the server folder. |
| Java does not start | Check the installed Java and the path to `java.exe`. Look for details in the console. |
| World backup fails | Stop the server and confirm the world directory exists. |
| Scheduled backup did not appear | Keep the application open, stop the server, and check the selected interval. |
| Update check fails | Check access to GitHub. Server management does not depend on the update check. |
