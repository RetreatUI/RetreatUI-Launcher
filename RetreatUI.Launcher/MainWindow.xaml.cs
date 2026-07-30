using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RetreatUI.Launcher.Models;
using RetreatUI.Launcher.Services;

namespace RetreatUI.Launcher;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly GitHubReleaseService _releaseService = new();
    private readonly LauncherUpdateService _launcherUpdateService = new();
    private readonly GamePathService _gamePathService = new();
    private readonly AddonInstallerService _installerService = new();

    private LauncherSettings _settings = new();
    private GitHubRelease? _latestRelease;
    private LauncherUpdate? _launcherUpdate;
    private AddonAction _currentAddonAction = AddonAction.None;
    private bool _isBusy;
    private bool _isInitialized;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LauncherVersionText.Text = $"Launcher v{_launcherUpdateService.CurrentVersion}";
        _settings = await _settingsService.LoadAsync();

        if (string.IsNullOrWhiteSpace(_settings.AddOnsPath)
            || !_gamePathService.HasValidAddOnsPath(_settings.AddOnsPath))
        {
            _settings.AddOnsPath = _gamePathService.AutoDetectAddOnsPath() ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(_settings.AddOnsPath)
            && string.IsNullOrWhiteSpace(_settings.GameExecutablePath))
        {
            _settings.GameExecutablePath = _gamePathService.FindGameExecutable(_settings.AddOnsPath) ?? string.Empty;
        }

        BetaRadio.IsChecked = _settings.IncludeBeta;
        StableRadio.IsChecked = !_settings.IncludeBeta;
        _isInitialized = true;

        UpdatePathDisplay();
        await RefreshAsync();
        await CheckLauncherUpdateAsync();
    }

    private async Task RefreshAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        _currentAddonAction = AddonAction.None;
        SetStatus("Checking for updates...", StatusKind.Neutral);

        try
        {
            UpdateInstalledVersion();
            _latestRelease = await _releaseService.GetLatestReleaseAsync(_settings.IncludeBeta);

            if (_latestRelease is null)
            {
                LatestVersionText.Text = "Unavailable";
                ReleaseNotesText.Text = "No compatible RetreatUI release asset was found.";
                UpdateButton.Content = "NO RELEASE FOUND";
                UpdateButton.IsEnabled = false;
                SetStatus("No compatible release found.", StatusKind.Error);
                return;
            }

            string latestVersion = NormalizeVersion(_latestRelease.TagName);
            LatestVersionText.Text = latestVersion;
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_latestRelease.Body)
                ? "No release notes were provided."
                : _latestRelease.Body;

            bool validPath = _gamePathService.HasValidAddOnsPath(_settings.AddOnsPath);
            OpenFolderButton.IsEnabled = validPath;
            LaunchButton.IsEnabled = File.Exists(_settings.GameExecutablePath);
            ConfigureAddonAction(InstalledVersionText.Text, latestVersion, validPath);
        }
        catch (HttpRequestException ex)
        {
            LatestVersionText.Text = "Offline";
            ReleaseNotesText.Text = "The launcher could not contact GitHub. Check your internet connection and try again.";
            UpdateButton.Content = "RETRY";
            UpdateButton.IsEnabled = true;
            SetStatus($"Update check failed: {ex.Message}", StatusKind.Error);
        }
        catch (Exception ex)
        {
            UpdateButton.Content = "RETRY";
            UpdateButton.IsEnabled = true;
            SetStatus($"Unexpected error: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ConfigureAddonAction(string installed, string latestVersion, bool validPath)
    {
        if (installed == "Not installed")
        {
            SetAddonAction(
                AddonAction.Install,
                "INSTALL RETREATUI",
                validPath,
                validPath ? "RetreatUI is ready to install." : "Choose your AddOns folder.");
            return;
        }

        if (VersionsMatch(installed, latestVersion))
        {
            SetAddonAction(
                AddonAction.None,
                "RETREATUI IS UP TO DATE",
                false,
                $"RetreatUI {installed} is up to date.",
                StatusKind.Success);
            return;
        }

        int coreComparison = CompareCoreVersions(installed, latestVersion);
        int fullComparison = AddonVersion.Compare(installed, latestVersion);
        bool installedPrerelease = HasPrereleaseLabel(installed);
        bool latestPrerelease = _latestRelease?.Prerelease == true || HasPrereleaseLabel(latestVersion);

        if (!_settings.IncludeBeta && installedPrerelease)
        {
            if (coreComparison < 0)
            {
                SetAddonAction(
                    AddonAction.Update,
                    $"UPDATE TO STABLE {latestVersion.ToUpperInvariant()}",
                    validPath,
                    validPath ? $"Stable RetreatUI {latestVersion} is available." : "Choose your AddOns folder.");
            }
            else
            {
                SetAddonAction(
                    AddonAction.SwitchToStable,
                    $"SWITCH TO STABLE {latestVersion.ToUpperInvariant()}",
                    validPath,
                    validPath
                        ? $"A test or Beta build is installed. Stable {latestVersion} is selected."
                        : "Choose your AddOns folder.");
            }
            return;
        }

        if (_settings.IncludeBeta && latestPrerelease && !installedPrerelease && coreComparison <= 0)
        {
            SetAddonAction(
                AddonAction.SwitchToBeta,
                $"SWITCH TO BETA {latestVersion.ToUpperInvariant()}",
                validPath,
                validPath ? $"Beta RetreatUI {latestVersion} is available." : "Choose your AddOns folder.");
            return;
        }

        if (fullComparison == 0)
        {
            SetAddonAction(
                AddonAction.None,
                "RETREATUI IS UP TO DATE",
                false,
                $"RetreatUI {installed} is up to date.",
                StatusKind.Success);
            return;
        }

        if (fullComparison > 0)
        {
            SetAddonAction(
                AddonAction.None,
                "INSTALLED VERSION IS NEWER",
                false,
                $"RetreatUI {installed} is newer than the latest selected-channel release.",
                StatusKind.Neutral);
            return;
        }

        SetAddonAction(
            AddonAction.Update,
            $"UPDATE TO {latestVersion.ToUpperInvariant()}",
            validPath,
            validPath ? $"RetreatUI {latestVersion} is available." : "Choose your AddOns folder.");
    }

    private void SetAddonAction(
        AddonAction action,
        string buttonText,
        bool enabled,
        string status,
        StatusKind statusKind = StatusKind.Neutral)
    {
        _currentAddonAction = action;
        UpdateButton.Content = buttonText;
        UpdateButton.IsEnabled = enabled;
        SetStatus(status, statusKind);
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_latestRelease is null || _currentAddonAction == AddonAction.None)
        {
            await RefreshAsync();
            return;
        }

        if (!_gamePathService.HasValidAddOnsPath(_settings.AddOnsPath))
        {
            MessageBox.Show(
                this,
                "Choose the Project Ascension Interface\\AddOns folder first.",
                "RetreatUI Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_installerService.IsGameRunning())
        {
            MessageBox.Show(
                this,
                "Close Project Ascension before updating RetreatUI.",
                "Project Ascension is running",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_currentAddonAction is AddonAction.SwitchToStable or AddonAction.SwitchToBeta)
        {
            string channel = _currentAddonAction == AddonAction.SwitchToStable ? "Stable" : "Beta";
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"This will replace the currently installed RetreatUI build with {channel} " +
                $"{NormalizeVersion(_latestRelease.TagName)}. Your current addon folders will be backed up first.",
                $"Switch to {channel}?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        GitHubAsset? asset = GitHubReleaseService.FindRetreatUiAsset(_latestRelease);
        if (asset is null)
        {
            MessageBox.Show(this, "The selected release has no RetreatUI ZIP asset.", "RetreatUI Launcher");
            return;
        }

        AddonAction completedAction = _currentAddonAction;
        string latestVersion = NormalizeVersion(_latestRelease.TagName);

        if (_currentAddonAction == AddonAction.Update
            && AddonVersion.Compare(latestVersion, InstalledVersionText.Text) <= 0)
        {
            _currentAddonAction = AddonAction.None;
            UpdateButton.Content = "INSTALLED VERSION IS NEWER";
            UpdateButton.IsEnabled = false;
            SetStatus(
                $"RetreatUI {InstalledVersionText.Text} is newer than {latestVersion}. Downgrade blocked.",
                StatusKind.Neutral);
            return;
        }

        string tempDirectory = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher");
        Directory.CreateDirectory(tempDirectory);
        string zipPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.zip");

        SetBusy(true);
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;

        try
        {
            SetStatus($"Downloading {asset.Name}...", StatusKind.Neutral);
            Progress<double> progress = new(value => DownloadProgress.Value = value);
            await _releaseService.DownloadAssetAsync(asset, zipPath, progress);

            Progress<string> installationStatus = new(message => SetStatus(message, StatusKind.Neutral));
            InstallResult result = await _installerService.InstallAsync(
                zipPath,
                _settings.AddOnsPath,
                latestVersion,
                installationStatus);

            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "The update could not be installed.");
            }

            UpdateInstalledVersion();
            _currentAddonAction = AddonAction.None;
            UpdateButton.Content = "RETREATUI IS UP TO DATE";
            UpdateButton.IsEnabled = false;

            (string title, string message, string status) = completedAction switch
            {
                AddonAction.Install => (
                    "Installation complete",
                    "RetreatUI was installed successfully.",
                    $"RetreatUI {InstalledVersionText.Text} installed successfully."),
                AddonAction.SwitchToStable => (
                    "Channel change complete",
                    "RetreatUI was switched to the Stable channel successfully.",
                    $"Stable RetreatUI {InstalledVersionText.Text} installed successfully."),
                AddonAction.SwitchToBeta => (
                    "Channel change complete",
                    "RetreatUI was switched to the Beta channel successfully.",
                    $"Beta RetreatUI {InstalledVersionText.Text} installed successfully."),
                _ => (
                    "Update complete",
                    "RetreatUI was updated successfully.",
                    $"RetreatUI {InstalledVersionText.Text} updated successfully.")
            };

            SetStatus(status, StatusKind.Success);
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UpdateInstalledVersion();
            SetStatus($"Update failed: {ex.Message}", StatusKind.Error);
            MessageBox.Show(
                this,
                ex.Message,
                "RetreatUI installation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TryDelete(zipPath);
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private async Task CheckLauncherUpdateAsync()
    {
        try
        {
            _launcherUpdate = await _launcherUpdateService.CheckForUpdateAsync();
            if (_launcherUpdate is null
                || !LauncherUpdateService.IsNewerVersion(
                    _launcherUpdate.Version,
                    _launcherUpdateService.CurrentVersion))
            {
                LauncherUpdateButton.Visibility = Visibility.Collapsed;
                return;
            }

            LauncherUpdateButton.Content = $"UPDATE LAUNCHER TO {_launcherUpdate.Version.ToUpperInvariant()}";
            LauncherUpdateButton.Visibility = Visibility.Visible;
            LauncherUpdateButton.IsEnabled = true;
        }
        catch
        {
            // Launcher self-update must never block addon updates.
            LauncherUpdateButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void LauncherUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _launcherUpdate is null)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Install RetreatUI Launcher {_launcherUpdate.Version} now? The launcher will restart automatically.",
            "Update RetreatUI Launcher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;

        try
        {
            SetStatus($"Downloading launcher {_launcherUpdate.Version}...", StatusKind.Neutral);
            Progress<double> progress = new(value => DownloadProgress.Value = value);
            await _launcherUpdateService.PrepareUpdateAndRestartAsync(_launcherUpdate, progress);
            SetStatus("Restarting updated launcher...", StatusKind.Success);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetStatus($"Launcher update failed: {ex.Message}", StatusKind.Error);
            MessageBox.Show(
                this,
                $"The launcher could not update itself.\n\n{ex.Message}",
                "Launcher update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Select the Project Ascension AddOns folder",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_settings.AddOnsPath) && Directory.Exists(_settings.AddOnsPath))
        {
            dialog.InitialDirectory = _settings.AddOnsPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string? normalized = _gamePathService.NormalizeToAddOnsPath(dialog.FolderName);
        if (normalized is null)
        {
            MessageBox.Show(
                this,
                "The selected folder does not contain Interface\\AddOns. Select either the game folder or the AddOns folder.",
                "Invalid folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.AddOnsPath = normalized;
        _settings.GameExecutablePath = _gamePathService.FindGameExecutable(normalized) ?? string.Empty;
        await _settingsService.SaveAsync(_settings);

        UpdatePathDisplay();
        await RefreshAsync();
    }

    private async void ChannelRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        _settings.IncludeBeta = BetaRadio.IsChecked == true;
        await _settingsService.SaveAsync(_settings);
        await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        await CheckLauncherUpdateAsync();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_gamePathService.HasValidAddOnsPath(_settings.AddOnsPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _settings.AddOnsPath,
            UseShellExecute = true
        });
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_settings.GameExecutablePath))
        {
            MessageBox.Show(this, "The Ascension launcher executable could not be found.", "RetreatUI Launcher");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _settings.GameExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(_settings.GameExecutablePath)!,
            UseShellExecute = true
        });
    }

    private void UpdatePathDisplay()
    {
        bool valid = _gamePathService.HasValidAddOnsPath(_settings.AddOnsPath);
        PathText.Text = valid ? _settings.AddOnsPath : "Not selected";
        OpenFolderButton.IsEnabled = valid;
        LaunchButton.IsEnabled = File.Exists(_settings.GameExecutablePath);
        UpdateInstalledVersion();
    }

    private void UpdateInstalledVersion()
    {
        InstalledVersionText.Text = _gamePathService.HasValidAddOnsPath(_settings.AddOnsPath)
            ? _gamePathService.ReadInstalledVersion(_settings.AddOnsPath)
            : "Unknown";
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RefreshButton.IsEnabled = !busy;
        StableRadio.IsEnabled = !busy;
        BetaRadio.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy && _gamePathService.HasValidAddOnsPath(_settings.AddOnsPath);
        LaunchButton.IsEnabled = !busy && File.Exists(_settings.GameExecutablePath);
        LauncherUpdateButton.IsEnabled = !busy && _launcherUpdate is not null;

        if (busy)
        {
            UpdateButton.IsEnabled = false;
        }
        else if (_currentAddonAction != AddonAction.None)
        {
            UpdateButton.IsEnabled = _gamePathService.HasValidAddOnsPath(_settings.AddOnsPath);
        }
    }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        StatusDot.Fill = kind switch
        {
            StatusKind.Success => (Brush)FindResource("SuccessBrush"),
            StatusKind.Error => (Brush)FindResource("DangerBrush"),
            _ => (Brush)FindResource("MutedTextBrush")
        };
    }

    private static bool VersionsMatch(string installed, string latest)
    {
        return string.Equals(NormalizeVersion(installed), NormalizeVersion(latest), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPrereleaseLabel(string version)
    {
        return NormalizeVersion(version).Contains('-', StringComparison.Ordinal);
    }

    private static int CompareCoreVersions(string left, string right)
    {
        ParsedAddonVersion leftVersion = ParsedAddonVersion.Parse(left);
        ParsedAddonVersion rightVersion = ParsedAddonVersion.Parse(right);
        return leftVersion.Core.CompareTo(rightVersion.Core);
    }

    private static string NormalizeVersion(string version) => version.Trim().TrimStart('v', 'V');

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private readonly record struct ParsedAddonVersion(Version Core, string Suffix)
    {
        public static ParsedAddonVersion Parse(string value)
        {
            string normalized = NormalizeVersion(value);
            string[] split = normalized.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
            Version core = Version.TryParse(split[0], out Version? parsed)
                ? parsed
                : new Version(0, 0, 0);
            return new ParsedAddonVersion(core, split.Length > 1 ? split[1] : string.Empty);
        }
    }

    private enum AddonAction
    {
        None,
        Install,
        Update,
        SwitchToStable,
        SwitchToBeta
    }

    private enum StatusKind
    {
        Neutral,
        Success,
        Error
    }
}


