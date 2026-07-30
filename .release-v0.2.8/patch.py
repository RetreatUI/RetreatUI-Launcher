from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


def write(relative: str, content: str) -> None:
    (ROOT / relative).write_text(content, encoding="utf-8", newline="\n")


def replace_once(relative: str, old: str, new: str, label: str) -> None:
    content = read(relative)
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one {label} in {relative}, found {count}.")
    write(relative, content.replace(old, new, 1))


xaml_path = "RetreatUI.Launcher/MainWindow.xaml"
xaml = read(xaml_path)
footer = '''
        <Grid Grid.Row="8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="10"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="10"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <Ellipse x:Name="StatusDot"
                         Width="9"
                         Height="9"
                         Fill="{StaticResource MutedTextBrush}"
                         Margin="0,0,8,0"/>
                <TextBlock x:Name="StatusText"
                           Text="Starting launcher..."
                           Foreground="{StaticResource MutedTextBrush}"
                           VerticalAlignment="Center"/>
            </StackPanel>
            <Button x:Name="SupportButton"
                    Grid.Column="1"
                    Content="Support RetreatUI"
                    Style="{StaticResource SecondaryButtonStyle}"
                    ToolTip="Support RetreatUI development on Ko-fi"
                    Click="SupportButton_Click"/>
            <Button x:Name="OpenFolderButton"
                    Grid.Column="3"
                    Content="Open AddOns Folder"
                    Style="{StaticResource SecondaryButtonStyle}"
                    IsEnabled="False"
                    Click="OpenFolderButton_Click"/>
            <Button x:Name="LaunchButton"
                    Grid.Column="5"
                    Content="Launch Ascension"
                    Style="{StaticResource SecondaryButtonStyle}"
                    IsEnabled="False"
                    Click="LaunchButton_Click"/>
        </Grid>
    </Grid>
</Window>
'''
xaml, replacements = re.subn(
    r'\n        <Grid Grid.Row="8">.*?\n        </Grid>\n    </Grid>\n</Window>\s*$',
    footer,
    xaml,
    count=1,
    flags=re.S,
)
if replacements != 1:
    raise RuntimeError(f"Expected one launcher footer, replaced {replacements}.")
write(xaml_path, xaml)

window_path = "RetreatUI.Launcher/MainWindow.xaml.cs"
replace_once(
    window_path,
    '''public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();''',
    '''public partial class MainWindow : Window
{
    private const string SupportUrl = "https://ko-fi.com/retreatui";

    private readonly SettingsService _settingsService = new();''',
    "class field header",
)
replace_once(
    window_path,
    '''    private bool _isBusy;
    private bool _isInitialized;''',
    '''    private bool _isBusy;
    private bool _isInitialized;
    private bool _automaticLauncherUpdateInProgress;
    private bool _automaticAddonUpdateInProgress;''',
    "automatic update fields",
)
replace_once(
    window_path,
    '''        UpdatePathDisplay();
        await RefreshAsync();
        await CheckLauncherUpdateAsync();''',
    '''        UpdatePathDisplay();
        await CheckLauncherUpdateAsync();
        if (TryInstallLauncherUpdateAutomatically())
        {
            return;
        }

        await RefreshAsync();
        TryInstallAddonUpdateAutomatically();''',
    "startup update order",
)
replace_once(
    window_path,
    '''            SetStatus(status, StatusKind.Success);
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);''',
    '''            SetStatus(status, StatusKind.Success);
            if (!_automaticAddonUpdateInProgress)
            {
                MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }''',
    "automatic addon completion dialog suppression",
)
replace_once(
    window_path,
    '''        finally
        {
            TryDelete(zipPath);
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private async Task CheckLauncherUpdateAsync()''',
    '''        finally
        {
            TryDelete(zipPath);
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _automaticAddonUpdateInProgress = false;
        }
    }

    private async Task CheckLauncherUpdateAsync()''',
    "automatic addon state cleanup",
)
replace_once(
    window_path,
    '''        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Install RetreatUI Launcher {_launcherUpdate.Version} now? The launcher will restart automatically.",
            "Update RetreatUI Launcher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }''',
    '''        if (!_automaticLauncherUpdateInProgress)
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
        }''',
    "manual launcher confirmation guard",
)
replace_once(
    window_path,
    '''            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)''',
    '''            DownloadProgress.Visibility = Visibility.Collapsed;
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

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)''',
    "automatic launcher failure fallback",
)
replace_once(
    window_path,
    '''    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        await CheckLauncherUpdateAsync();
    }''',
    '''    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckLauncherUpdateAsync();
        if (TryInstallLauncherUpdateAutomatically())
        {
            return;
        }

        await RefreshAsync();
        TryInstallAddonUpdateAutomatically();
    }''',
    "refresh update order",
)
replace_once(
    window_path,
    '''    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)''',
    '''    private void SupportButton_Click(object sender, RoutedEventArgs e)
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
                $"The support page could not be opened.\\n\\n{ex.Message}",
                "Support RetreatUI",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)''',
    "Ko-fi support handler",
)
helpers = '''    private bool TryInstallLauncherUpdateAutomatically()
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
            || !_gamePathService.HasValidAddOnsPath(_settings.AddOnsPath))
        {
            return;
        }

        string latestVersion = NormalizeVersion(_latestRelease.TagName);
        if (_installerService.IsGameRunning())
        {
            SetStatus(
                $"RetreatUI {latestVersion} is ready. Close Project Ascension and press Refresh to install it automatically.",
                StatusKind.Neutral);
            return;
        }

        _automaticAddonUpdateInProgress = true;
        UpdateButton_Click(UpdateButton, new RoutedEventArgs());
    }

'''
replace_once(
    window_path,
    "    private async Task CheckLauncherUpdateAsync()",
    helpers + "    private async Task CheckLauncherUpdateAsync()",
    "automatic update helpers",
)

project_path = "RetreatUI.Launcher/RetreatUI.Launcher.csproj"
project = read(project_path)
project = project.replace("<Version>0.2.7</Version>", "<Version>0.2.8</Version>")
project = project.replace("<FileVersion>0.2.7.0</FileVersion>", "<FileVersion>0.2.8.0</FileVersion>")
project = project.replace("<AssemblyVersion>0.2.7.0</AssemblyVersion>", "<AssemblyVersion>0.2.8.0</AssemblyVersion>")
if "<Version>0.2.8</Version>" not in project:
    raise RuntimeError("Launcher project version was not updated to 0.2.8.")
write(project_path, project)

launcher_update_path = "RetreatUI.Launcher/Services/LauncherUpdateService.cs"
launcher_update = read(launcher_update_path).replace('?? "0.2.7";', '?? "0.2.8";')
if '?? "0.2.8";' not in launcher_update:
    raise RuntimeError("Launcher update fallback version was not updated.")
write(launcher_update_path, launcher_update)

release_service_path = "RetreatUI.Launcher/Services/GitHubReleaseService.cs"
release_service = read(release_service_path).replace(
    'new ProductInfoHeaderValue("RetreatUI-Launcher", "0.2.5")',
    'new ProductInfoHeaderValue("RetreatUI-Launcher", "0.2.8")',
)
if 'new ProductInfoHeaderValue("RetreatUI-Launcher", "0.2.8")' not in release_service:
    raise RuntimeError("Addon release user agent was not updated.")
write(release_service_path, release_service)

readme_path = "README.md"
readme = read(readme_path)
readme = readme.replace("`0.2.6`", "`0.2.8`", 1)
readme = readme.replace("launcher-v0.2.6", "launcher-v0.2.8")
feature_anchor = "- One-click RetreatUI installation and updating"
feature_replacement = "\n".join(
    [
        feature_anchor,
        "- Automatic RetreatUI updates on the selected channel when Project Ascension is closed",
        "- A discreet Support RetreatUI button linking to the official Ko-fi page",
    ]
)
if "official Ko-fi page" not in readme:
    if feature_anchor not in readme:
        raise RuntimeError("README feature anchor was not found.")
    readme = readme.replace(feature_anchor, feature_replacement, 1)
write(readme_path, readme)

print("RetreatUI Launcher v0.2.8 source patch applied successfully.")
