# RetreatUI Launcher

Windows installer and updater for **RetreatUI**, supporting both:

- **Project Ascension: Conquest of Azeroth (CoA)**
- **World of Warcraft: The Burning Crusade (TBC) Classic / Anniversary**

## Current version

`0.3.0`

## Features

- Dedicated game selector for CoA and TBC
- Separate saved AddOns paths and detected executables for each game
- Automatic Project Ascension AddOns folder detection
- Automatic detection of common World of Warcraft Classic / Anniversary folders
- Product-specific release filtering so a TBC profile can never install a CoA ZIP, or vice versa
- Custom RetreatUI application icon and in-launcher branding
- Stable and Beta update channels
- Clear switching between Stable, Beta and local test builds
- Automatic release checks through the RetreatUI GitHub releases feed
- Full Stable/Beta semantic version ordering with hard addon downgrade protection
- Manual folder selection as a fallback
- Installed and latest version display per selected game
- Product-specific release notes inside the launcher
- One-click RetreatUI installation and updating
- Automatic RetreatUI updates on the selected channel when the selected game is closed
- A discreet Support RetreatUI button linking to the official Ko-fi page
- Validates the downloaded ZIP, addon versions and installed file copy
- Automatic backup before replacing addon files
- Automatic rollback when installation or verification fails
- Keeps the five newest backups
- Never touches WoW SavedVariables
- Opens the selected AddOns folder
- Launches the selected game when its executable is detected
- Automatic launcher self-updates from the public binary-only release repository
- SHA-256 verification and executable rollback for launcher updates

## Managed folders

The launcher only replaces:

- `RetreatUI`
- `RetreatUI_Classes`

CoA and TBC use separate game paths, but the same managed addon folder names inside each client's `Interface\AddOns` folder.

## Addon release requirements

The launcher reads releases from `RetreatUI/RetreatUI-Addon` and selects assets by game edition.

### CoA asset

```text
RetreatUI_v1.0.11.zip
```

### TBC asset

```text
RetreatUI_TBC_v0.1.0.zip
```

`RetreatUI-TBC-v0.1.0.zip` is also accepted for compatibility.

Every ZIP must contain these folders at its root:

```text
RetreatUI/
RetreatUI_Classes/
```

Both `.toc` files must use the same version encoded in the asset filename. The release tag may be shared or product-specific; the launcher validates against the selected asset's version.

Stable releases must not be marked as pre-releases. Beta releases must be marked as pre-releases and may use versions such as:

```text
1.0.12-beta.1
0.1.0-beta.1
```

A release may contain both CoA and TBC assets. The launcher will only display a release for a game when a compatible asset is present.

## Launcher release channel

The source code remains in this private repository. Compiled launcher releases are published publicly in:

```text
RetreatUI/RetreatUI-Launcher-Releases
```

Every public launcher release must contain:

```text
RetreatUI_Launcher.exe
RetreatUI_Launcher.exe.sha256
RetreatUI_Launcher_win-x64.zip
```

Installed launchers from v0.2.6 onward check that public repository and can replace themselves after verifying the SHA-256 checksum.

## Building locally

Requires Visual Studio 2022 or the .NET 8 SDK with Windows Desktop support.

```powershell
dotnet publish .\RetreatUI.Launcher\RetreatUI.Launcher.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output .\publish
```

The finished executable will be:

```text
publish\RetreatUI_Launcher.exe
```

## Building with GitHub Actions

Pull requests run a compile validation automatically. For a downloadable test build:

1. Open the repository's **Actions** tab.
2. Select **Build RetreatUI Launcher**.
3. Choose **Run workflow** and select the desired branch.
4. Download the build artifact after the workflow finishes.

For a launcher release, create and push a tag such as:

```text
launcher-v0.3.0
```

The built EXE, checksum and ZIP are then transferred to the public launcher release repository.

## Data locations

Settings:

```text
%LOCALAPPDATA%\RetreatUI Launcher\settings.json
```

Backups:

```text
%LOCALAPPDATA%\RetreatUI Launcher\Backups
```

## License

This project is proprietary. All rights reserved.
