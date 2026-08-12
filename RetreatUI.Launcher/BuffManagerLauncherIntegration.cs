using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetreatUI.Launcher.Models;
using RetreatUI.Launcher.Services;

namespace RetreatUI.Launcher;

internal static class BuffManagerLauncherIntegration
{
    private const string AssetPrefix = "RetreatUI_BuffManager_v";
    private static readonly GitHubReleaseService ReleaseService = new();
    private static readonly BuffManagerInstallerService Installer = new();
    private static readonly GameProcessService GameProcess = new();

    private static MainWindow? _window;
    private static Button? _button;
    private static TextBlock? _pathText;
    private static bool _busy;

    public static void Attach(MainWindow window)
    {
        _window = window;
        window.Loaded += Window_Loaded;
    }

    private static async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_window is null) return;
        AddButton();
        HookRefreshTriggers();
        await RefreshAsync();
    }

    private static void AddButton()
    {
        if (_window is null || _button is not null) return;
        _pathText = _window.FindName("PathText") as TextBlock;
        Grid? pathGrid = FindAncestor<Grid>(_pathText);
        if (pathGrid is null) return;

        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });

        _button = new Button
        {
            Content = "BUFF MANAGER",
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 13.5,
            IsEnabled = false,
            ToolTip = "Install or update the optional RetreatUI Buff Manager addon separately."
        };
        if (_window.TryFindResource("SecondaryButtonStyle") is Style style) _button.Style = style;
        Grid.SetColumn(_button, 4);
        _button.Click += Button_Click;
        pathGrid.Children.Add(_button);
    }

    private static void HookRefreshTriggers()
    {
        if (_window is null) return;
        foreach (string name in new[] { "CoARadio", "TbcRadio", "StableRadio", "BetaRadio" })
        {
            if (_window.FindName(name) is RadioButton radio)
                radio.Checked += async (_, _) => await RefreshAsync();
        }

        if (_window.FindName("RefreshButton") is Button refresh)
            refresh.Click += async (_, _) => await RefreshAsync();

        if (_pathText is not null)
        {
            DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            descriptor?.AddValueChanged(_pathText, async (_, _) => await RefreshAsync());
        }
    }

    private static async Task RefreshAsync()
    {
        if (_window is null || _button is null || _busy) return;
        RadioButton? coa = _window.FindName("CoARadio") as RadioButton;
        bool isCoA = coa?.IsChecked == true;
        _button.Visibility = isCoA ? Visibility.Visible : Visibility.Collapsed;
        if (!isCoA) return;

        string? addOnsPath = GetAddOnsPath();
        if (string.IsNullOrWhiteSpace(addOnsPath) || !Directory.Exists(addOnsPath))
        {
            SetButton("BUFF MANAGER", false, "Choose the CoA AddOns folder first.");
            return;
        }

        try
        {
            bool includeBeta = (_window.FindName("BetaRadio") as RadioButton)?.IsChecked == true;
            GitHubRelease? release = await ReleaseService.GetLatestReleaseAsync(GameEdition.CoA, includeBeta);
            GitHubAsset? asset = FindAsset(release);
            if (asset is null)
            {
                SetButton("BUFF MANAGER NOT PUBLISHED", false, "No separate Buff Manager asset is published on this channel yet.");
                return;
            }

            string latest = AssetVersion(asset);
            string? installed = BuffManagerInstallerService.GetInstalledVersion(addOnsPath);
            if (installed is null)
            {
                SetButton("INSTALL BUFF MANAGER", true, $"Optional Buff Manager {latest} is available.");
            }
            else if (AddonVersion.Compare(installed, latest) < 0)
            {
                SetButton($"UPDATE BUFF MANAGER {latest}", true, $"Installed: {installed} · Available: {latest}");
            }
            else
            {
                SetButton($"BUFF MANAGER {installed}", false, "Buff Manager is up to date.");
            }
        }
        catch (Exception ex)
        {
            SetButton("BUFF MANAGER RETRY", true, $"Buff Manager check failed: {ex.Message}");
        }
    }

    private static async void Button_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null || _button is null || _busy) return;
        string? addOnsPath = GetAddOnsPath();
        if (string.IsNullOrWhiteSpace(addOnsPath) || !Directory.Exists(addOnsPath)) return;

        if (GameProcess.IsGameRunning(GameEdition.CoA))
        {
            MessageBox.Show(_window, "Close Conquest of Azeroth before installing Buff Manager.", "CoA is running", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _busy = true;
        _button.IsEnabled = false;
        _button.Content = "INSTALLING BUFF MANAGER...";
        string tempDirectory = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher", "BuffManagerDownloads");
        Directory.CreateDirectory(tempDirectory);
        string zipPath = Path.Combine(tempDirectory, $"BuffManager-{Guid.NewGuid():N}.zip");

        try
        {
            bool includeBeta = (_window.FindName("BetaRadio") as RadioButton)?.IsChecked == true;
            GitHubRelease? release = await ReleaseService.GetLatestReleaseAsync(GameEdition.CoA, includeBeta);
            GitHubAsset? asset = FindAsset(release) ?? throw new InvalidOperationException("No separate Buff Manager asset is available on this channel.");
            string version = AssetVersion(asset);

            await ReleaseService.DownloadAssetAsync(asset, zipPath);
            Progress<string> status = new(message => _button.ToolTip = message);
            await Installer.InstallAsync(zipPath, addOnsPath, version, status);

            MessageBox.Show(_window, $"RetreatUI Buff Manager {version} was installed successfully.", "Buff Manager installed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(_window, ex.Message, "Buff Manager installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            _busy = false;
            await RefreshAsync();
        }
    }

    private static GitHubAsset? FindAsset(GitHubRelease? release) =>
        release?.Assets.FirstOrDefault(asset =>
            asset.Name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)
            && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private static string AssetVersion(GitHubAsset asset)
    {
        string fileName = Path.GetFileNameWithoutExtension(asset.Name);
        return fileName.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)
            ? fileName[AssetPrefix.Length..].Trim().TrimStart('v', 'V')
            : fileName;
    }

    private static string? GetAddOnsPath()
    {
        if (_pathText is null) return null;
        string value = _pathText.Text?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) || value.Equals("Not selected", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void SetButton(string content, bool enabled, string tooltip)
    {
        if (_button is null) return;
        _button.Content = content;
        _button.IsEnabled = enabled;
        _button.ToolTip = tooltip;
    }
}
