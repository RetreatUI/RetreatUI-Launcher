# First build and release

## 1. Create a new GitHub repository

Recommended name:

```text
RetreatUI-Launcher
```

Keep it separate from `RetreatUI-Addon`.

## 2. Upload this project

Upload everything inside this folder to the root of the new repository.

The repository root should show:

```text
.github/
RetreatUI.Launcher/
RetreatUI.Launcher.sln
.gitignore
BUILD_AND_RELEASE.md
LICENSE
README.md
```

## 3. Build the first test EXE

Open:

```text
Actions → Build RetreatUI Launcher → Run workflow
```

When the build finishes, open the workflow run and download:

```text
RetreatUI-Launcher-win-x64
```

That artifact contains:

```text
RetreatUI_Launcher.exe
RetreatUI_Launcher_win-x64.zip
```

## 4. Test safely

Before testing an actual update:

1. Close Project Ascension.
2. Keep a manual copy of the current `RetreatUI` and `RetreatUI_Classes` folders.
3. Start the launcher.
4. Confirm that it detects the correct `Interface\AddOns` path.
5. Confirm that the installed version is shown correctly.
6. Confirm that v1.0.10 is shown as the latest Stable release.

Because v1.0.10 is already installed, the first expected result is:

```text
RETREATUI IS UP TO DATE
```

## 5. Test a future update

The first complete update test happens when a newer addon release exists, for example `v1.0.11`.

Create the release exactly as normal and attach:

```text
RetreatUI_v1.0.11.zip
```

The launcher should detect it automatically without any launcher code changes.

## 6. Release the launcher

Create the tag:

```text
launcher-v0.1.0
```

The GitHub Actions workflow automatically builds and attaches the launcher files to a new GitHub release.
