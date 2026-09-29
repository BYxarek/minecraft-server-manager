# Changelog

[Русский](changelog.ru.md)

## 2026-09-29 — Source publication

- Published the current application source, installer scripts, tests, and English/Russian documentation to the GitHub `main` branch. The application version remains 0.2.1; no release was created.
- Verified the Release build and smoke tests before publication.

## 0.2.1 — 2026-09-24

### Additional changes

- Replaced Overview's Remove from list with Delete server. After an explicit path confirmation, it deletes the stopped server's entire folder, including worlds and backups, and removes the profile. Protected and overlapping paths are rejected; deletion errors restore the profile. Updated both language guides; verified build, deletion smoke tests, WPF startup, and translations.
- Update checks now use a persisted 15-minute cooldown shared by automatic and manual checks. Repeated or concurrent clicks reuse the last result; GitHub's rate-limit reset or Retry-After can extend the wait. The button shows the remaining wait instead of sending another request. Added smoke tests for concurrency, restart persistence, and rate-limit reset.
- The Settings tab shows Java memory and executable path only for Java servers. The simple properties editor hides keys specific to the other server edition. The Overview's Open folder button now opens Windows File Explorer. Verified the build, smoke tests, and WPF startup.
- Added selection lists for game mode, difficulty, and default Bedrock player permission. Moved the allow list to its own tab with add, remove, and enable controls.
- Added an online player tab with refresh, kick, operator, Java ban and pardon, allow-list, game mode, and private message actions.
- Closing the window now keeps the app and servers running in the tray; explicit exit is available there. The window starts maximized.
- Backup creation notices and the backup tab can reveal a ZIP in File Explorer. Updated English and Russian documentation. Verified build, smoke tests, WPF startup, and translation keys.

- When no server is selected, tabs are replaced by a prompt to create or select a server. Removed the duplicate icon from the Create server button.
- The creation form lists only editions with an imported template. Its default folder is `<application folder>/<server name>`, follows name edits until a custom path is entered, and is created when needed.
- Added indeterminate progress bars and animated activity indicators for server installation, template import, backup and restore, and update checks.
- Added simple switches and fields for common and existing `server.properties` keys above the advanced raw editor, with shared saving and unsaved-change detection.
- The installer now reports the detected Java version and optionally downloads a SHA-256 verified Eclipse Temurin JRE 25, adds it to the user's PATH, and saves its path for new Java profiles.
- Updated English and Russian user and developer documentation. Verified a WPF startup, smoke tests, Java detection with Windows PowerShell, and an installer rebuild; optional Java installation itself was not run on the host.

### Added

- Inno Setup 6 installer build with English/Russian setup, per-user installation, shortcuts, upgrade handling, and uninstall support. The existing version remains 0.2.1.
- English and Russian localization with English as the default language and a saved language preference.
- Restoration of a selected world backup, with the previous world kept until replacement succeeds.
- Automatic backups every 6, 12, or 24 hours while the application is open and the server is stopped. Scheduling is disabled by default.
- Console search with a match count.
- States for starting, running, stopping, waiting to restart, and failure.
- A warning about unsaved settings when switching servers or closing the application.
- English and Russian user and developer documentation.

### Fixed

- Java backups use the directory named by `level-name` in `server.properties`.
- A pending automatic restart is canceled by manual stop, profile removal, or application exit.
- Creating and restoring backups, importing server files, and installing a new server no longer block the window.
- Console reads are synchronized with writes, and process exit races are handled.
- Previous profile values are restored if saving settings fails.
- The successful backup message remains visible.
- New server files are prepared in a temporary directory before installation.

### Removed

- The archived PowerShell/web panel and its launcher scripts.

### Build verification

- Rebuilt the 0.2.1 Windows x64 installer from the current code with `Build-Installer.ps1 -ReplaceExisting`; Inno Setup compiled successfully. Verified the output file and SHA-256 hash. No GitHub publication.
- Built the current application in Release configuration with `dotnet build`; completed with no warnings or errors. No version change, installer build, or publication.
- Built the 0.2.1 Windows x64 installer and verified silent installation and uninstallation.
- Rebuilt the installer after the interface changes; verified the installed application opens on a clean profile and uninstalls successfully.
- Rebuilt the 0.2.1 installer after restoring Explorer pickers, adding the first-run language switch, and adding icons. Verified application startup, all four tab icons, and matching translation keys.

### Interface refinement

- File and server-folder selection now opens the Windows Explorer picker; the other application forms remain inside the main window.
- Added a language switch to the upper-right corner of first-run setup and icons across navigation, tabs, and actions.
- Replaced standalone setup, server creation, file/folder selection, and confirmation windows with dark panels inside the main window.
- Removed white window surfaces and changed near-white text to the green-tinted palette, including the Windows title bar and application icon.
