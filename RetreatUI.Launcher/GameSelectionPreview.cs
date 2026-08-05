using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RetreatUI.Launcher;

public partial class MainWindow
{
    private bool _gameSelectorInjected;
    private bool _tbcPreviewMode;
    private RadioButton? _coaGameRadio;
    private RadioButton? _tbcGameRadio;
    private Border? _tbcWorkInProgressPanel;
    private DispatcherTimer? _gameSelectorStateTimer;
    private RowDefinition? _tbcPanelSpacingRow;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_gameSelectorInjected)
        {
            return;
        }

        _gameSelectorInjected = true;
        InjectGameSelector();
        StartGameSelectorStateTimer();
    }

    private void InjectGameSelector()
    {
        if (Content is not Grid root || root.RowDefinitions.Count < 9)
        {
            return;
        }

        Width = Math.Max(Width, 1020);
        Height = Math.Max(Height, 860);
        MinWidth = Math.Max(MinWidth, 920);
        MinHeight = Math.Max(MinHeight, 760);

        foreach (UIElement child in root.Children)
        {
            int row = Grid.GetRow(child);
            if (row >= 2)
            {
                Grid.SetRow(child, row + 4);
            }
        }

        root.RowDefinitions.Insert(2, new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Insert(3, new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Insert(4, new RowDefinition { Height = GridLength.Auto });
        _tbcPanelSpacingRow = new RowDefinition { Height = new GridLength(0) };
        root.RowDefinitions.Insert(5, _tbcPanelSpacingRow);

        Border selector = BuildGameSelector();
        Grid.SetRow(selector, 2);
        root.Children.Add(selector);

        _tbcWorkInProgressPanel = BuildTbcWorkInProgressPanel();
        Grid.SetRow(_tbcWorkInProgressPanel, 4);
        root.Children.Add(_tbcWorkInProgressPanel);
    }

    private Border BuildGameSelector()
    {
        Border panel = new()
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18)
        };

        Grid layout = new();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Child = layout;

        StackPanel description = new() { VerticalAlignment = VerticalAlignment.Center };
        description.Children.Add(new TextBlock
        {
            Text = "GAME VERSION",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("MutedTextBrush")
        });
        description.Children.Add(new TextBlock
        {
            Text = "Choose the RetreatUI product you want to manage.",
            Margin = new Thickness(0, 5, 18, 0),
            Foreground = (Brush)FindResource("TextBrush")
        });
        layout.Children.Add(description);

        StackPanel choices = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(choices, 1);
        layout.Children.Add(choices);

        _coaGameRadio = BuildGameRadio("Conquest of Azeroth", "AVAILABLE NOW");
        _coaGameRadio.GroupName = "GameVersion";
        _coaGameRadio.IsChecked = true;
        _coaGameRadio.Checked += GameRadio_Checked;
        choices.Children.Add(_coaGameRadio);

        _tbcGameRadio = BuildGameRadio("TBC Anniversary", "WORK IN PROGRESS");
        _tbcGameRadio.GroupName = "GameVersion";
        _tbcGameRadio.Margin = new Thickness(10, 0, 0, 0);
        _tbcGameRadio.Checked += GameRadio_Checked;
        choices.Children.Add(_tbcGameRadio);

        return panel;
    }

    private RadioButton BuildGameRadio(string title, string status)
    {
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = status,
            FontSize = 10,
            Foreground = status == "WORK IN PROGRESS"
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedTextBrush")
        });

        return new RadioButton
        {
            Content = content,
            Style = (Style)FindResource("ChannelRadioStyle")
        };
    }

    private Border BuildTbcWorkInProgressPanel()
    {
        Border panel = new()
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromRgb(23, 19, 13)),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18)
        };

        Grid layout = new();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.Child = layout;

        Border badge = new()
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = new SolidColorBrush(Color.FromRgb(42, 33, 20)),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "TBC",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AccentBrush")
            }
        };
        layout.Children.Add(badge);

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 2);
        text.Children.Add(new TextBlock
        {
            Text = "TBC CLASSIC ANNIVERSARY — WORK IN PROGRESS",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        text.Children.Add(new TextBlock
        {
            Text = "The first RetreatUI TBC build is in development, starting with Feral Druid DPS. Installation will be enabled when the first test build is ready.",
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextBrush")
        });
        layout.Children.Add(text);

        return panel;
    }

    private async void GameRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_gameSelectorInjected || _isBusy)
        {
            return;
        }

        _tbcPreviewMode = ReferenceEquals(sender, _tbcGameRadio);
        if (_tbcPreviewMode)
        {
            ApplyTbcPreview();
            return;
        }

        ApplyCoaMode();
        await RefreshAsync();
        TryInstallAddonUpdateAutomatically();
    }

    private void ApplyTbcPreview()
    {
        if (_tbcWorkInProgressPanel is null)
        {
            return;
        }

        _tbcWorkInProgressPanel.Visibility = Visibility.Visible;
        if (_tbcPanelSpacingRow is not null)
        {
            _tbcPanelSpacingRow.Height = new GridLength(16);
        }

        StableRadio.IsEnabled = false;
        BetaRadio.IsEnabled = false;
        InstalledVersionText.Text = "Coming soon";
        LatestVersionText.Text = "In development";
        PathText.Text = "TBC Anniversary installation support will be enabled with the first test build.";
        ReleaseNotesText.Text =
            "RetreatUI for TBC Classic Anniversary is now in active development.\n\n" +
            "The first supported class HUD will be Feral Druid DPS, using the same centered resource bar, Main row, Utility row and dark RetreatUI layout as the Conquest of Azeroth version.\n\n" +
            "No TBC addon package is being distributed yet.";

        UpdateButton.Content = "TBC SUPPORT COMING SOON";
        UpdateButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        OpenFolderButton.IsEnabled = false;
        LaunchButton.Content = "TBC Anniversary — Coming Soon";
        LaunchButton.IsEnabled = false;
        SetChooseFolderEnabled(false);
        SetStatus("TBC Anniversary support is in active development.", StatusKind.Neutral);
    }

    private void ApplyCoaMode()
    {
        if (_tbcWorkInProgressPanel is not null)
        {
            _tbcWorkInProgressPanel.Visibility = Visibility.Collapsed;
        }
        if (_tbcPanelSpacingRow is not null)
        {
            _tbcPanelSpacingRow.Height = new GridLength(0);
        }

        StableRadio.IsEnabled = !_isBusy;
        BetaRadio.IsEnabled = !_isBusy;
        RefreshButton.IsEnabled = !_isBusy;
        LaunchButton.Content = "Launch Ascension";
        SetChooseFolderEnabled(!_isBusy);
        UpdatePathDisplay();
    }

    private void StartGameSelectorStateTimer()
    {
        _gameSelectorStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _gameSelectorStateTimer.Tick += (_, _) =>
        {
            if (_coaGameRadio is not null)
            {
                _coaGameRadio.IsEnabled = !_isBusy;
            }
            if (_tbcGameRadio is not null)
            {
                _tbcGameRadio.IsEnabled = !_isBusy;
            }

            if (_tbcPreviewMode && !_isBusy)
            {
                ApplyTbcPreview();
            }
        };
        _gameSelectorStateTimer.Start();
    }

    private void SetChooseFolderEnabled(bool enabled)
    {
        if (Content is not DependencyObject root)
        {
            return;
        }

        Button? chooseFolder = FindButtonByContent(root, "Choose Folder");
        if (chooseFolder is not null)
        {
            chooseFolder.IsEnabled = enabled;
        }
    }

    private static Button? FindButtonByContent(DependencyObject root, string content)
    {
        int children = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < children; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button
                && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            {
                return button;
            }

            Button? nested = FindButtonByContent(child, content);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
