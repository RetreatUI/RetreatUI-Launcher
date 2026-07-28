# RetreatUI Launcher

Windows updater for **RetreatUI**, built for Project Ascension: Conquest of Azeroth.

## Current version

`0.2.3`

## Features

- Custom RetreatUI application icon and in-launcher branding
- Stable and Beta update channels
- Clear switching between Stable, Beta and local test builds
- Automatic release checks through the RetreatUI GitHub releases feed
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
- Automatic launcher self-update support through launcher GitHub releases

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
launcher-v0.2.3
```

GitHub Actions will create a GitHub release and attach both the EXE and ZIP. Once the repository is public, installed launchers can detect and install future launcher releases automatically.

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

