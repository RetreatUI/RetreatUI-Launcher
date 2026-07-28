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
    private readonly GamePathService _gamePathService = new();
    private readonly AddonInstallerService _installerService = new();

    private LauncherSettings _settings = new();
    private GitHubRelease? _latestRelease;
    private bool _isBusy;
    private bool _isInitialized;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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
    }

    private async Task RefreshAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
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

            string installed = InstalledVersionText.Text;
            if (installed == "Not installed")
            {
                UpdateButton.Content = "INSTALL RETREATUI";
                UpdateButton.IsEnabled = validPath;
                SetStatus(validPath ? "RetreatUI is ready to install." : "Choose your AddOns folder.", StatusKind.Neutral);
            }
            else if (VersionsMatch(installed, latestVersion))
            {
                UpdateButton.Content = "RETREATUI IS UP TO DATE";
                UpdateButton.IsEnabled = false;
                SetStatus($"RetreatUI {installed} is up to date.", StatusKind.Success);
            }
            else
            {
                UpdateButton.Content = $"UPDATE TO {latestVersion.ToUpperInvariant()}";
                UpdateButton.IsEnabled = validPath;
                SetStatus(validPath ? $"RetreatUI {latestVersion} is available." : "Choose your AddOns folder.", StatusKind.Neutral);
            }
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

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_latestRelease is null)
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

        GitHubAsset? asset = GitHubReleaseService.FindRetreatUiAsset(_latestRelease);
        if (asset is null)
        {
            MessageBox.Show(this, "The selected release has no RetreatUI ZIP asset.", "RetreatUI Launcher");
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
                installationStatus);

            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "The update could not be installed.");
            }

            UpdateInstalledVersion();
            UpdateButton.Content = "RETREATUI IS UP TO DATE";
            UpdateButton.IsEnabled = false;
            SetStatus($"RetreatUI {InstalledVersionText.Text} installed successfully.", StatusKind.Success);

            MessageBox.Show(
                this,
                "RetreatUI was updated successfully. You can now launch Project Ascension.",
                "Update complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"Update failed: {ex.Message}", StatusKind.Error);
            MessageBox.Show(
                this,
                $"RetreatUI could not be updated.\n\n{ex.Message}\n\nYour previous addon version was restored when possible.",
                "Update failed",
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

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

    private enum StatusKind
    {
        Neutral,
        Success,
        Error
    }
}
