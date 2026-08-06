using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RetreatUI.Launcher;

internal static class LauncherTheme
{
    private const string ThemeMarker = "RetreatUI.DarkLogoTheme";
    private const string CoALogoPath = "Assets/ProjectAscensionWatermark.png";
    private const string TbcLogoPath = "Assets/TbcWatermark.png";

    public static void Apply(Window window)
    {
        ApplyWindowBackground(window);
        AddEditionWatermark(window, "CoARadio", CoALogoPath, 245, new Thickness(0, -4, 2, -8));
        AddEditionWatermark(window, "TbcRadio", TbcLogoPath, 285, new Thickness(0, -6, -2, -6));
        AddDetailWatermark(window);
        StyleHeadings(window);
    }

    private static void ApplyWindowBackground(Window window)
    {
        window.Background = FindBrush(window, "BackgroundBrush", Brushes.Black);

        if (window.Content is not Grid root || Equals(root.Tag, ThemeMarker))
        {
            return;
        }

        root.Tag = ThemeMarker;
        root.Background = FindBrush(window, "LauncherBackgroundBrush", Brushes.Black);
        root.ClipToBounds = true;

        Rectangle glow = new()
        {
            Fill = FindBrush(window, "HeaderGlowBrush", Brushes.Transparent),
            IsHitTestVisible = false,
            Opacity = 0.9
        };
        Panel.SetZIndex(glow, -100);
        root.Children.Insert(0, glow);
    }

    private static void AddEditionWatermark(
        Window window,
        string radioName,
        string assetPath,
        double width,
        Thickness margin)
    {
        if (window.FindName(radioName) is not RadioButton radio
            || Equals(radio.Tag, ThemeMarker)
            || radio.Content is not UIElement originalContent)
        {
            return;
        }

        radio.Content = null;

        Grid card = new()
        {
            ClipToBounds = true
        };

        Image logo = new()
        {
            Source = LoadImage(assetPath),
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Opacity = 0.78,
            Margin = margin,
            IsHitTestVisible = false
        };

        Rectangle fade = new()
        {
            Fill = FindBrush(window, "CardFadeBrush", Brushes.Transparent),
            IsHitTestVisible = false
        };

        card.Children.Add(logo);
        card.Children.Add(fade);
        card.Children.Add(originalContent);
        radio.Content = card;
        radio.Tag = ThemeMarker;
    }

    private static void AddDetailWatermark(Window window)
    {
        if (window.FindName("EditionTitleText") is not FrameworkElement title
            || window.FindName("CoARadio") is not RadioButton coaRadio
            || window.FindName("TbcRadio") is not RadioButton tbcRadio)
        {
            return;
        }

        Grid? detailGrid = FindAncestorGrid(title, minimumRows: 7);
        if (detailGrid is null || Equals(detailGrid.Tag, ThemeMarker))
        {
            return;
        }

        detailGrid.Tag = ThemeMarker;

        Image watermark = new()
        {
            Width = 410,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Uniform,
            Opacity = 0.34,
            Margin = new Thickness(0, 4, 22, 0),
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(watermark, Math.Max(1, detailGrid.RowDefinitions.Count));
        Panel.SetZIndex(watermark, -20);

        Rectangle veil = new()
        {
            Fill = new LinearGradientBrush(
                Color.FromArgb(250, 18, 24, 32),
                Color.FromArgb(110, 18, 24, 32),
                0),
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(veil, Math.Max(1, detailGrid.RowDefinitions.Count));
        Panel.SetZIndex(veil, -10);

        void UpdateWatermark()
        {
            watermark.Source = LoadImage(tbcRadio.IsChecked == true ? TbcLogoPath : CoALogoPath);
            watermark.Width = tbcRadio.IsChecked == true ? 430 : 350;
        }

        coaRadio.Checked += (_, _) => UpdateWatermark();
        tbcRadio.Checked += (_, _) => UpdateWatermark();
        UpdateWatermark();

        detailGrid.Children.Insert(0, watermark);
        detailGrid.Children.Insert(1, veil);
    }

    private static void StyleHeadings(DependencyObject root)
    {
        Brush titleBrush = FindBrush(root, "TitleBrush", Brushes.Goldenrod);
        foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(root))
        {
            if (textBlock.Text is "RETREATUI LAUNCHER")
            {
                textBlock.Foreground = titleBrush;
                textBlock.FontFamily = new FontFamily("Georgia");
                textBlock.FontSize = 27;
            }
            else if (textBlock.Text is "CONQUEST OF AZEROTH" or "THE BURNING CRUSADE")
            {
                textBlock.FontFamily = new FontFamily("Georgia");
            }
        }
    }

    private static Grid? FindAncestorGrid(DependencyObject start, int minimumRows)
    {
        DependencyObject? current = start;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is Grid grid && grid.RowDefinitions.Count >= minimumRows)
            {
                return grid;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static ImageSource LoadImage(string assetPath) =>
        new BitmapImage(new Uri($"pack://application:,,,/{assetPath}", UriKind.Absolute));

    private static Brush FindBrush(DependencyObject owner, string key, Brush fallback)
    {
        if (owner is FrameworkElement element && element.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return Application.Current.TryFindResource(key) as Brush ?? fallback;
    }
}
