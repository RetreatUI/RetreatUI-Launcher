# RetreatUI Launcher

Windows updater for **RetreatUI**, built for Project Ascension: Conquest of Azeroth.

## Current version

`0.2.6`

## Features

- Custom RetreatUI application icon and in-launcher branding
- Stable and Beta update channels
- Clear switching between Stable, Beta and local test builds
- Automatic release checks through the RetreatUI GitHub releases feed
- Full Stable/Beta semantic version ordering with hard addon downgrade protection
- Automatic Project Ascension AddOns folder detection
- Manual folder selection as a fallback
- Installed and latest version display
- Release notes inside the launcher
- One-click RetreatUI installation and updating
- Validates the downloaded ZIP, addon versions and installed file copy
- Automatic backup before replacing addon files
- Automatic rollback when installation or verification fails
- Keeps the five newest backups
- Never touches WoW SavedVariables
- Opens the AddOns folder
- Launches Project Ascension when the executable is detected
- Automatic launcher self-updates from the public binary-only release repository
- SHA-256 verification and executable rollback for launcher updates

## Managed folders

The launcher only replaces:

- `RetreatUI`
- `RetreatUI_Classes`

## Release requirements

Every RetreatUI addon release must include one ZIP asset named like:

```text
RetreatUI_v1.0.11.zip
```

The ZIP must contain these folders at its root:

```text
RetreatUI/
RetreatUI_Classes/
```

Both `.toc` files must use the same version as the GitHub release tag.

Stable releases must not be marked as pre-releases.
Beta releases must be marked as pre-releases and may use tags such as:

```text
v1.0.12-beta.1
```

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

The included workflow builds the launcher automatically.

For a normal test build:

1. Open the repository's **Actions** tab.
2. Select **Build RetreatUI Launcher**.
3. Choose **Run workflow**.
4. Download the build artifact after the workflow finishes.

For a launcher release, create and push a tag such as:

```text
launcher-v0.2.6
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
