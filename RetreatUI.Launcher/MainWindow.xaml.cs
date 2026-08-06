using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RetreatUI.Launcher.Models;
using RetreatUI.Launcher.Services;

namespace RetreatUI.Launcher;

public partial class MainWindow : Window
{
    private const string SupportUrl = "https://ko-fi.com/retreatui";

    private readonly SettingsService _settingsService = new();
    private readonly GitHubReleaseService _releaseService = new();
    private readonly LauncherUpdateService _launcherUpdateService = new();
    private readonly GamePathService _gamePathService = new();
    private readonly AddonInstallerService _installerService = new();
    private readonly GameProcessService _gameProcessService = new();

    private LauncherSettings _settings = new();
    private GitHubRelease? _latestRelease;
    private LauncherUpdate? _launcherUpdate;
    private GameEdition _selectedEdition = GameEdition.CoA;
    private AddonAction _currentAddonAction = AddonAction.None;
    private bool _isBusy;
    private bool _isInitialized;
    private bool _automaticLauncherUpdateInProgress;
    private bool _automaticAddonUpdateInProgress;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private string ProductName => _selectedEdition == GameEdition.CoA
        ? "Conquest of Azeroth"
        : "The Burning Crusade";

    private string ProductShortName => _selectedEdition == GameEdition.CoA ? "CoA" : "TBC";

    private string CurrentAddOnsPath
    {
        get => _selectedEdition == GameEdition.CoA
            ? _settings.CoAAddOnsPath
            : _settings.TbcAddOnsPath;
        set
        {
            if (_selectedEdition == GameEdition.CoA)
            {
                _settings.CoAAddOnsPath = value;
            }
            else
            {
                _settings.TbcAddOnsPath = value;
            }
        }
    }

    private string CurrentGameExecutablePath
    {
        get => _selectedEdition == GameEdition.CoA
            ? _settings.CoAGameExecutablePath
            : _settings.TbcGameExecutablePath;
        set
        {
            if (_selectedEdition == GameEdition.CoA)
            {
                _settings.CoAGameExecutablePath = value;
            }
            else
            {
                _settings.TbcGameExecutablePath = value;
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LauncherVersionText.Text = $"Launcher v{_launcherUpdateService.CurrentVersion}";
        _settings = await _settingsService.LoadAsync();
        MigrateLegacySettings();

        _selectedEdition = Enum.TryParse(
            _settings.SelectedEdition,
            ignoreCase: true,
            out GameEdition savedEdition)
            ? savedEdition
            : GameEdition.CoA;

        EnsureDetectedPaths(GameEdition.CoA);
        EnsureDetectedPaths(GameEdition.Tbc);

        CoARadio.IsChecked = _selectedEdition == GameEdition.CoA;
        TbcRadio.IsChecked = _selectedEdition == GameEdition.Tbc;
        BetaRadio.IsChecked = _settings.IncludeBeta;
        StableRadio.IsChecked = !_settings.IncludeBeta;
        _isInitialized = true;

        ApplyEditionVisuals();
        UpdatePathDisplay();
        await _settingsService.SaveAsync(_settings);

        await CheckLauncherUpdateAsync();
        if (TryInstallLauncherUpdateAutomatically())
        {
            return;
        }

        await RefreshAsync();
        TryInstallAddonUpdateAutomatically();
    }

    private void MigrateLegacySettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.CoAAddOnsPath)
            && !string.IsNullOrWhiteSpace(_settings.AddOnsPath))
        {
            _settings.CoAAddOnsPath = _settings.AddOnsPath;
        }

        if (string.IsNullOrWhiteSpace(_settings.CoAGameExecutablePath)
            && !string.IsNullOrWhiteSpace(_settings.GameExecutablePath))
        {
            _settings.CoAGameExecutablePath = _settings.GameExecutablePath;
        }
    }

    private void EnsureDetectedPaths(GameEdition edition)
    {
        string addOnsPath = edition == GameEdition.CoA
            ? _settings.CoAAddOnsPath
            : _settings.TbcAddOnsPath;

        if (string.IsNullOrWhiteSpace(addOnsPath)
            || !_gamePathService.HasValidAddOnsPath(addOnsPath))
        {
            addOnsPath = _gamePathService.AutoDetectAddOnsPath(edition) ?? string.Empty;
            if (edition == GameEdition.CoA)
            {
                _settings.CoAAddOnsPath = addOnsPath;
            }
            else
            {
                _settings.TbcAddOnsPath = addOnsPath;
            }
        }

        if (string.IsNullOrWhiteSpace(addOnsPath))
        {
            return;
        }

        string executablePath = edition == GameEdition.CoA
            ? _settings.CoAGameExecutablePath
            : _settings.TbcGameExecutablePath;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            executablePath = _gamePathService.FindGameExecutable(addOnsPath, edition) ?? string.Empty;
            if (edition == GameEdition.CoA)
            {
                _settings.CoAGameExecutablePath = executablePath;
            }
            else
            {
                _settings.TbcGameExecutablePath = executablePath;
            }
        }
    }

    private async Task RefreshAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        _currentAddonAction = AddonAction.None;
        SetStatus($"Checking {ProductShortName} for updates...", StatusKind.Neutral);

        try
        {
            UpdateInstalledVersion();
            _latestRelease = await _releaseService.GetLatestReleaseAsync(
                _selectedEdition,
                _settings.IncludeBeta);

            if (_latestRelease is null)
            {
                LatestVersionText.Text = _selectedEdition == GameEdition.Tbc
                    ? "Not published"
                    : "Unavailable";
                ReleaseNotesText.Text = _selectedEdition == GameEdition.Tbc
                    ? "No TBC package has been published on the selected channel yet. " +
                      "The launcher will automatically detect releases containing a " +
                      "RetreatUI_TBC_v<version>.zip asset without ever falling back to a CoA package."
                    : "No compatible CoA release asset was found.";
                UpdateButton.Content = _selectedEdition == GameEdition.Tbc
                    ? "TBC BUILD NOT PUBLISHED"
                    : "NO RELEASE FOUND";
                UpdateButton.IsEnabled = false;
                SetStatus(
                    _selectedEdition == GameEdition.Tbc
                        ? "TBC is configured, but no compatible package is published yet."
                        : "No compatible CoA release found.",
                    _selectedEdition == GameEdition.Tbc ? StatusKind.Neutral : StatusKind.Error);
                UpdateProductControls();
                return;
            }

            string latestVersion = GitHubReleaseService.GetAssetVersion(_latestRelease, _selectedEdition);
            LatestVersionText.Text = latestVersion;
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_latestRelease.Body)
                ? $"No {ProductShortName} release notes were provided."
                : _latestRelease.Body;

            bool validPath = _gamePathService.HasValidAddOnsPath(CurrentAddOnsPath);
            UpdateProductControls();
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
                $"INSTALL RETREATUI FOR {ProductShortName.ToUpperInvariant()}",
                validPath,
                validPath
                    ? $"RetreatUI for {ProductShortName} is ready to install."
                    : $"Choose your {ProductShortName} AddOns folder.");
            return;
        }

        if (VersionsMatch(installed, latestVersion))
        {
            SetAddonAction(
                AddonAction.None,
                $"{ProductShortName.ToUpperInvariant()} IS UP TO DATE",
                false,
                $"RetreatUI {ProductShortName} {installed} is up to date.",
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
                    validPath ? $"Stable {ProductShortName} {latestVersion} is available." : "Choose your AddOns folder.");
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
                validPath ? $"Beta {ProductShortName} {latestVersion} is available." : "Choose your AddOns folder.");
            return;
        }

        if (fullComparison == 0)
        {
            SetAddonAction(
                AddonAction.None,
                $"{ProductShortName.ToUpperInvariant()} IS UP TO DATE",
                false,
                $"RetreatUI {ProductShortName} {installed} is up to date.",
                StatusKind.Success);
            return;
        }

        if (fullComparison > 0)
        {
            SetAddonAction(
                AddonAction.None,
                "INSTALLED VERSION IS NEWER",
                false,
                $"RetreatUI {ProductShortName} {installed} is newer than the latest selected-channel release.",
                StatusKind.Neutral);
            return;
        }

        SetAddonAction(
            AddonAction.Update,
            $"UPDATE {ProductShortName.ToUpperInvariant()} TO {latestVersion.ToUpperInvariant()}",
            validPath,
            validPath ? $"RetreatUI {ProductShortName} {latestVersion} is available." : "Choose your AddOns folder.");
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

        if (!_gamePathService.HasValidAddOnsPath(CurrentAddOnsPath))
        {
            MessageBox.Show(
                this,
                $"Choose the {ProductName} Interface\\AddOns folder first.",
                "RetreatUI Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_gameProcessService.IsGameRunning(_selectedEdition))
        {
            MessageBox.Show(
                this,
                $"Close {ProductName} before updating RetreatUI.",
                $"{ProductName} is running",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string latestVersion = GitHubReleaseService.GetAssetVersion(_latestRelease, _selectedEdition);
        if (_currentAddonAction is AddonAction.SwitchToStable or AddonAction.SwitchToBeta)
        {
            string channel = _currentAddonAction == AddonAction.SwitchToStable ? "Stable" : "Beta";
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"This will replace the currently installed {ProductShortName} build with {channel} " +
                $"{latestVersion}. Your current addon folders will be backed up first.",
                $"Switch {ProductShortName} to {channel}?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        GitHubAsset? asset = GitHubReleaseService.FindRetreatUiAsset(_latestRelease, _selectedEdition);
        if (asset is null)
        {
            MessageBox.Show(
                this,
                $"The selected release has no compatible {ProductShortName} ZIP asset.",
                "RetreatUI Launcher");
            return;
        }

        AddonAction completedAction = _currentAddonAction;
        if (_currentAddonAction == AddonAction.Update
            && AddonVersion.Compare(latestVersion, InstalledVersionText.Text) <= 0)
        {
            _currentAddonAction = AddonAction.None;
            UpdateButton.Content = "INSTALLED VERSION IS NEWER";
            UpdateButton.IsEnabled = false;
            SetStatus(
                $"RetreatUI {ProductShortName} {InstalledVersionText.Text} is newer than {latestVersion}. Downgrade blocked.",
                StatusKind.Neutral);
            return;
        }

        string tempDirectory = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher");
        Directory.CreateDirectory(tempDirectory);
        string zipPath = Path.Combine(tempDirectory, $"{ProductShortName}-{Guid.NewGuid():N}.zip");

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
                CurrentAddOnsPath,
                latestVersion,
                installationStatus);

            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "The update could not be installed.");
            }

            UpdateInstalledVersion();
            _currentAddonAction = AddonAction.None;
            UpdateButton.Content = $"{ProductShortName.ToUpperInvariant()} IS UP TO DATE";
            UpdateButton.IsEnabled = false;

            (string title, string message, string status) = completedAction switch
            {
                AddonAction.Install => (
                    "Installation complete",
                    $"RetreatUI for {ProductName} was installed successfully.",
                    $"RetreatUI {ProductShortName} {InstalledVersionText.Text} installed successfully."),
                AddonAction.SwitchToStable => (
                    "Channel change complete",
                    $"RetreatUI {ProductShortName} was switched to Stable successfully.",
                    $"Stable {ProductShortName} {InstalledVersionText.Text} installed successfully."),
                AddonAction.SwitchToBeta => (
                    "Channel change complete",
                    $"RetreatUI {ProductShortName} was switched to Beta successfully.",
                    $"Beta {ProductShortName} {InstalledVersionText.Text} installed successfully."),
                _ => (
                    "Update complete",
                    $"RetreatUI {ProductShortName} was updated successfully.",
                    $"RetreatUI {ProductShortName} {InstalledVersionText.Text} updated successfully.")
            };

            SetStatus(status, StatusKind.Success);
            if (!_automaticAddonUpdateInProgress)
            {
                MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            UpdateInstalledVersion();
            SetStatus($"Update failed: {ex.Message}", StatusKind.Error);
            MessageBox.Show(
                this,
                ex.Message,
                $"RetreatUI {ProductShortName} installation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TryDelete(zipPath);
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _automaticAddonUpdateInProgress = false;
        }
    }

    private bool TryInstallLauncherUpdateAutomatically()
    {
        if (_launcherUpdate is null
            || _isBusy
            || !LauncherUpdateService.IsNewerVersion(
                _launcherUpdate.Version,
                _launcherUpdateService.CurrentVersion))
        {
            return false;
        }

        _automaticLauncherUpdateInProgress = true;
        LauncherUpdateButton_Click(LauncherUpdateButton, new RoutedEventArgs());
        return true;
    }

    private void TryInstallAddonUpdateAutomatically()
    {
        if (_currentAddonAction != AddonAction.Update
            || _latestRelease is null
            || _isBusy
            || !_gamePathService.HasValidAddOnsPath(CurrentAddOnsPath))
        {
            return;
        }

        string latestVersion = GitHubReleaseService.GetAssetVersion(_latestRelease, _selectedEdition);
        if (_gameProcessService.IsGameRunning(_selectedEdition))
        {
            SetStatus(
                $"RetreatUI {ProductShortName} {latestVersion} is ready. Close {ProductName} and press Refresh to install it automatically.",
                StatusKind.Neutral);
            return;
        }

        _automaticAddonUpdateInProgress = true;
        UpdateButton_Click(UpdateButton, new RoutedEventArgs());
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

        if (!_automaticLauncherUpdateInProgress)
        {
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

            bool wasAutomatic = _automaticLauncherUpdateInProgress;
            _automaticLauncherUpdateInProgress = false;
            if (wasAutomatic)
            {
                await RefreshAsync();
                TryInstallAddonUpdateAutomatically();
            }
        }
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = $"Select the {ProductName} AddOns folder",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(CurrentAddOnsPath) && Directory.Exists(CurrentAddOnsPath))
        {
            dialog.InitialDirectory = CurrentAddOnsPath;
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

        CurrentAddOnsPath = normalized;
        CurrentGameExecutablePath = _gamePathService.FindGameExecutable(normalized, _selectedEdition) ?? string.Empty;
        await _settingsService.SaveAsync(_settings);

        UpdatePathDisplay();
        await RefreshAsync();
    }

    private async void EditionRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        GameEdition nextEdition = TbcRadio.IsChecked == true ? GameEdition.Tbc : GameEdition.CoA;
        if (_selectedEdition == nextEdition)
        {
            return;
        }

        _selectedEdition = nextEdition;
        _settings.SelectedEdition = _selectedEdition.ToString();
        EnsureDetectedPaths(_selectedEdition);
        await _settingsService.SaveAsync(_settings);

        _latestRelease = null;
        _currentAddonAction = AddonAction.None;
        ApplyEditionVisuals();
        UpdatePathDisplay();
        await RefreshAsync();
        TryInstallAddonUpdateAutomatically();
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
        EnsureDetectedPaths(_selectedEdition);
        await _settingsService.SaveAsync(_settings);
        UpdatePathDisplay();

        await CheckLauncherUpdateAsync();
        if (TryInstallLauncherUpdateAutomatically())
        {
            return;
        }

        await RefreshAsync();
        TryInstallAddonUpdateAutomatically();
    }

    private void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SupportUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"The support page could not be opened.\n\n{ex.Message}",
                "Support RetreatUI",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_gamePathService.HasValidAddOnsPath(CurrentAddOnsPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = CurrentAddOnsPath,
            UseShellExecute = true
        });
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(CurrentGameExecutablePath))
        {
            MessageBox.Show(
                this,
                $"The {ProductName} executable could not be found. Choose the AddOns folder again to refresh detection.",
                "RetreatUI Launcher");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = CurrentGameExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(CurrentGameExecutablePath)!,
            UseShellExecute = true
        });
    }

    private void ApplyEditionVisuals()
    {
        if (_selectedEdition == GameEdition.CoA)
        {
            EditionTitleText.Text = "Conquest of Azeroth";
            EditionSubtitleText.Text = "Project Ascension installation";
            PathLabelText.Text = "PROJECT ASCENSION ADDONS FOLDER";
            ReleaseHeadingText.Text = "LATEST COA CHANGES";
            LaunchButton.Content = "Launch CoA";
        }
        else
        {
            EditionTitleText.Text = "The Burning Crusade";
            EditionSubtitleText.Text = "Classic / Anniversary installation";
            PathLabelText.Text = "WOW CLASSIC / TBC ADDONS FOLDER";
            ReleaseHeadingText.Text = "LATEST TBC CHANGES";
            LaunchButton.Content = "Launch TBC";
        }

        LatestVersionText.Text = "Checking...";
        ReleaseNotesText.Text = $"Checking {ProductShortName} releases...";
    }

    private void UpdatePathDisplay()
    {
        bool valid = _gamePathService.HasValidAddOnsPath(CurrentAddOnsPath);
        PathText.Text = valid ? CurrentAddOnsPath : "Not selected";
        UpdateProductControls();
        UpdateInstalledVersion();
    }

    private void UpdateProductControls()
    {
        bool valid = _gamePathService.HasValidAddOnsPath(CurrentAddOnsPath);
        OpenFolderButton.IsEnabled = !_isBusy && valid;
        LaunchButton.IsEnabled = !_isBusy && File.Exists(CurrentGameExecutablePath);
    }

    private void UpdateInstalledVersion()
    {
        InstalledVersionText.Text = _gamePathService.HasValidAddOnsPath(CurrentAddOnsPath)
            ? _gamePathService.ReadInstalledVersion(CurrentAddOnsPath)
            : "Unknown";
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RefreshButton.IsEnabled = !busy;
        StableRadio.IsEnabled = !busy;
        BetaRadio.IsEnabled = !busy;
        CoARadio.IsEnabled = !busy;
        TbcRadio.IsEnabled = !busy;
        SupportButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy && _gamePathService.HasValidAddOnsPath(CurrentAddOnsPath);
        LaunchButton.IsEnabled = !busy && File.Exists(CurrentGameExecutablePath);
        LauncherUpdateButton.IsEnabled = !busy && _launcherUpdate is not null;

        if (busy)
        {
            UpdateButton.IsEnabled = false;
        }
        else if (_currentAddonAction != AddonAction.None)
        {
            UpdateButton.IsEnabled = _gamePathService.HasValidAddOnsPath(CurrentAddOnsPath);
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
