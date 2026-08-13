using System.Windows.Controls;
using System.Windows.Media;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher;

public partial class MainWindow
{
    private Button? _buffManagerButton;
    private GitHubRelease? _latestBuffManagerRelease;
    private GitHubAsset? _latestBuffManagerAsset;
    private string _latestBuffManagerVersion = string.Empty;
    private int _buffManagerRefreshGeneration;

    private void InitializeBuffManagerControls()
    {
        if (_buffManagerButton is not null || RefreshButton.Parent is not Grid actionGrid) return;

        // Existing layout: [main action][gap][Refresh]. Insert Buff Manager and
        // another gap without changing the RetreatUI core action button.
        actionGrid.ColumnDefinitions.Insert(2, new ColumnDefinition { Width = new GridLength(160) });
        actionGrid.ColumnDefinitions.Insert(3, new ColumnDefinition { Width = new GridLength(14) });
        Grid.SetColumn(RefreshButton, 4);

        _buffManagerButton = new Button
        {
            Content = "BUFF MANAGER",
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 14,
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Visibility = Visibility.Collapsed,
            IsEnabled = false,
            ToolTip = "Install or update the optional RetreatUI Buff Manager independently from RetreatUI."
        };
        Grid.SetColumn(_buffManagerButton, 2);
        _buffManagerButton.Click += BuffManagerButton_Click;
        actionGrid.Children.Add(_buffManagerButton);

        CoARadio.Checked += BuffManagerEditionOrChannelChanged;
        TbcRadio.Checked += BuffManagerEditionOrChannelChanged;
        StableRadio.Checked += BuffManagerEditionOrChannelChanged;
        BetaRadio.Checked += BuffManagerEditionOrChannelChanged;
        RefreshButton.Click += BuffManagerRefreshButton_Click;

        UpdateBuffManagerVisibility();
    }

    private async void BuffManagerEditionOrChannelChanged(object sender, RoutedEventArgs e)
    {
        UpdateBuffManagerVisibility();
        if (_selectedEdition == GameEdition.CoA)
            await RefreshBuffManagerStateAsync();
    }

    private async void BuffManagerRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEdition == GameEdition.CoA)
            await RefreshBuffManagerStateAsync();
    }

    private void UpdateBuffManagerVisibility()
    {
        if (_buffManagerButton is null) return;
        _buffManagerButton.Visibility = _selectedEdition == GameEdition.CoA
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task RefreshBuffManagerStateAsync()
    {
        Button? button = _buffManagerButton;
        if (button is null) return;

        int generation = ++_buffManagerRefreshGeneration;
        UpdateBuffManagerVisibility();
        if (_selectedEdition != GameEdition.CoA) return;

        bool validPath = _gamePathService.HasValidAddOnsPath(CurrentAddOnsPath);
        if (!validPath)
        {
            button.Content = "BUFF MANAGER";
            button.IsEnabled = false;
            button.ToolTip = "Choose your CoA AddOns folder first.";
            return;
        }

        string coreVersion = _gamePathService.ReadInstalledVersion(CurrentAddOnsPath);
        if (coreVersion is "Not installed" or "Unknown")
        {
            button.Content = "BUFF MANAGER REQUIRES COA";
            button.IsEnabled = false;
            button.ToolTip = "Install RetreatUI for CoA first. Buff Manager depends on RetreatUI.";
            return;
        }

        try
        {
            GitHubRelease? release = await _releaseService.GetLatestReleaseAsync(GameEdition.CoA, _settings.IncludeBeta);
            if (generation != _buffManagerRefreshGeneration || _selectedEdition != GameEdition.CoA) return;

            _latestBuffManagerRelease = release;
            _latestBuffManagerAsset = release is null ? null : GitHubReleaseService.FindBuffManagerAsset(release);
            _latestBuffManagerVersion = release is null ? string.Empty : GitHubReleaseService.GetBuffManagerAssetVersion(release);

            if (_latestBuffManagerAsset is null || string.IsNullOrWhiteSpace(_latestBuffManagerVersion))
            {
                button.Content = "BUFF MANAGER NOT PUBLISHED";
                button.IsEnabled = false;
                button.ToolTip = "No Buff Manager package exists on the selected channel yet.";
                return;
            }

            string installed = _gamePathService.ReadInstalledBuffManagerVersion(CurrentAddOnsPath);
            if (installed == "Not installed")
            {
                button.Content = "INSTALL BUFF MANAGER";
                button.IsEnabled = !_isBusy;
                button.ToolTip = $"Install RetreatUI Buff Manager {_latestBuffManagerVersion}.";
                return;
            }

            if (installed == "Unknown")
            {
                button.Content = "REINSTALL BUFF MANAGER";
                button.IsEnabled = !_isBusy;
                button.ToolTip = "The Buff Manager folder exists but its version could not be read.";
                return;
            }

            int comparison = AddonVersion.Compare(installed, _latestBuffManagerVersion);
            if (comparison == 0)
            {
                button.Content = "BUFF MANAGER IS UP TO DATE";
                button.IsEnabled = false;
                button.ToolTip = $"RetreatUI Buff Manager {installed} is installed.";
            }
            else if (comparison > 0)
            {
                button.Content = "BUFF MANAGER IS NEWER";
                button.IsEnabled = false;
                button.ToolTip = $"Installed Buff Manager {installed} is newer than {_latestBuffManagerVersion}.";
            }
            else
            {
                button.Content = $"UPDATE BUFF MANAGER {_latestBuffManagerVersion.ToUpperInvariant()}";
                button.IsEnabled = !_isBusy;
                button.ToolTip = $"Update RetreatUI Buff Manager from {installed} to {_latestBuffManagerVersion}.";
            }
        }
        catch
        {
            if (generation != _buffManagerRefreshGeneration) return;
            button.Content = "BUFF MANAGER UNAVAILABLE";
            button.IsEnabled = false;
            button.ToolTip = "The Buff Manager release check could not be completed.";
        }
    }

    private async void BuffManagerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _selectedEdition != GameEdition.CoA || _buffManagerButton is null) return;

        if (!_gamePathService.HasValidAddOnsPath(CurrentAddOnsPath))
        {
            MessageBox.Show(this, "Choose the Conquest of Azeroth Interface\\AddOns folder first.",
                "RetreatUI Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string coreVersion = _gamePathService.ReadInstalledVersion(CurrentAddOnsPath);
        if (coreVersion is "Not installed" or "Unknown")
        {
            MessageBox.Show(this, "Install RetreatUI for CoA before installing Buff Manager.",
                "RetreatUI Buff Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_latestBuffManagerAsset is null || string.IsNullOrWhiteSpace(_latestBuffManagerVersion))
        {
            await RefreshBuffManagerStateAsync();
            if (_latestBuffManagerAsset is null || string.IsNullOrWhiteSpace(_latestBuffManagerVersion))
            {
                MessageBox.Show(this, "No Buff Manager package is available on the selected channel.",
                    "RetreatUI Buff Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        if (_gameProcessService.IsGameRunning(GameEdition.CoA))
        {
            MessageBox.Show(this, "Close Conquest of Azeroth before installing or updating Buff Manager.",
                "Conquest of Azeroth is running", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string installed = _gamePathService.ReadInstalledBuffManagerVersion(CurrentAddOnsPath);
        if (VersionsMatch(installed, _latestBuffManagerVersion))
        {
            await RefreshBuffManagerStateAsync();
            return;
        }

        string tempDirectory = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher");
        Directory.CreateDirectory(tempDirectory);
        string zipPath = Path.Combine(tempDirectory, $"BuffManager-{Guid.NewGuid():N}.zip");
        GitHubAsset asset = _latestBuffManagerAsset;

        SetBusy(true);
        _buffManagerButton.IsEnabled = false;
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
                _latestBuffManagerVersion,
                installationStatus);

            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "Buff Manager could not be installed.");

            string newVersion = _gamePathService.ReadInstalledBuffManagerVersion(CurrentAddOnsPath);
            SetStatus($"RetreatUI Buff Manager {newVersion} installed successfully.", StatusKind.Success);
            MessageBox.Show(this,
                $"RetreatUI Buff Manager {newVersion} was installed successfully.\n\nIt remains a separate optional WoW addon and is updated independently from RetreatUI.",
                "Buff Manager installed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"Buff Manager update failed: {ex.Message}", StatusKind.Error);
            MessageBox.Show(this, ex.Message, "RetreatUI Buff Manager installation failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TryDelete(zipPath);
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            await RefreshBuffManagerStateAsync();
        }
    }
}