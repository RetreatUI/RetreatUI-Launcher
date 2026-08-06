using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RetreatUI.Launcher;

public partial class MainWindow
{
    private const string EditionBadgeTag = "RetreatUI.EditionBadge";

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLauncherVisualLoaded),
            handledEventsToo: true);
    }

    private static void OnLauncherVisualLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
        {
            return;
        }

        window.ApplyLauncherVisualPolish();
    }

    private void ApplyLauncherVisualPolish()
    {
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

        HideBannerArtwork(this);
        ConfigureEditionCard(CoARadio, "COA", "21 CUSTOM CLASSES", Color.FromRgb(198, 137, 74));
        ConfigureEditionCard(TbcRadio, "TBC", "CLASSIC CLIENT", Color.FromRgb(213, 155, 73));

        CoARadio.Checked -= EditionCard_Checked;
        TbcRadio.Checked -= EditionCard_Checked;
        CoARadio.Checked += EditionCard_Checked;
        TbcRadio.Checked += EditionCard_Checked;

        QueueEditionCardRefresh();
    }

    private void EditionCard_Checked(object sender, RoutedEventArgs e)
    {
        QueueEditionCardRefresh();
    }

    private void QueueEditionCardRefresh()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                MakeEditionCardSharp(CoARadio);
                MakeEditionCardSharp(TbcRadio);
                HideBannerArtwork(this);
            }));
    }

    private static void MakeEditionCardSharp(RadioButton radio)
    {
        radio.UseLayoutRounding = true;
        radio.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(radio, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(radio, TextRenderingMode.ClearType);

        radio.ApplyTemplate();
        if (radio.Template.FindName("EditionCard", radio) is Border card)
        {
            // The previous checked-state DropShadowEffect rendered the complete
            // card, including its text, into a blurred bitmap. Keep selection
            // crisp by using a whole-pixel border and no container effect.
            card.Effect = null;
            card.CacheMode = null;
            card.UseLayoutRounding = true;
            card.SnapsToDevicePixels = true;
            card.BorderThickness = radio.IsChecked == true
                ? new Thickness(2)
                : new Thickness(1);
        }

        ApplySharpTextRendering(radio);
    }

    private static void ConfigureEditionCard(
        RadioButton radio,
        string editionCode,
        string caption,
        Color accentColor)
    {
        if (radio.Content is not Grid contentGrid)
        {
            return;
        }

        bool alreadyAdded = contentGrid.Children
            .OfType<FrameworkElement>()
            .Any(element => Equals(element.Tag, EditionBadgeTag));
        if (alreadyAdded)
        {
            return;
        }

        SolidColorBrush accent = new(accentColor);
        SolidColorBrush border = new(Color.FromArgb(150, accentColor.R, accentColor.G, accentColor.B));
        SolidColorBrush panel = new(Color.FromArgb(214, 8, 17, 24));
        SolidColorBrush captionBrush = new(Color.FromRgb(151, 164, 176));

        Border badge = new()
        {
            Tag = EditionBadgeTag,
            Width = 124,
            Height = 82,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 72, 0),
            Background = panel,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        Grid badgeLayout = new();
        badgeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        badgeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border accentBar = new()
        {
            Background = accent,
            CornerRadius = new CornerRadius(5, 0, 0, 5)
        };
        badgeLayout.Children.Add(accentBar);

        StackPanel text = new()
        {
            GridColumn = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock code = new()
        {
            Text = editionCode,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            SnapsToDevicePixels = true
        };
        TextOptions.SetTextFormattingMode(code, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(code, TextRenderingMode.ClearType);

        TextBlock detail = new()
        {
            Text = caption,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = captionBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            SnapsToDevicePixels = true
        };
        TextOptions.SetTextFormattingMode(detail, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(detail, TextRenderingMode.ClearType);

        text.Children.Add(code);
        text.Children.Add(detail);
        badgeLayout.Children.Add(text);
        badge.Child = badgeLayout;

        Panel.SetZIndex(badge, 20);
        contentGrid.Children.Add(badge);
    }

    private static void HideBannerArtwork(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is Image image)
            {
                string source = image.Source?.ToString() ?? string.Empty;
                if (source.Contains("ProjectAscensionWatermark", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("TbcWatermark", StringComparison.OrdinalIgnoreCase))
                {
                    image.Visibility = Visibility.Collapsed;
                }
            }

            HideBannerArtwork(child);
        }
    }

    private static void ApplySharpTextRendering(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock text)
            {
                text.SnapsToDevicePixels = true;
                text.UseLayoutRounding = true;
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(text, TextRenderingMode.ClearType);
            }

            ApplySharpTextRendering(child);
        }
    }
}
