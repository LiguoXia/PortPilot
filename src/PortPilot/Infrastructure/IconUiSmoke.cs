using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortPilot.Services;
using PortPilot.Views;

namespace PortPilot.Infrastructure;

internal static class IconUiSmoke
{
    public static void Verify(MainWindow window, ThemeService themes, string folder, List<string> results)
    {
        foreach (var theme in new[] { "light", "dark" })
        {
            themes.Apply(theme); window.UpdateLayout();
            var visibleIcons = Descendants(window).OfType<Icon>().Where(i => i.IsVisible).ToArray();
            if (visibleIcons.Length < 15) throw new InvalidOperationException("Missing shell icons.");
            if (visibleIcons.Where(i => i.Kind == IconKind.ChevronDown).Any(i =>
                ((SolidColorBrush)i.Foreground).Color != ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color))
                throw new InvalidOperationException("Search dropdown icon lost theme contrast.");
            foreach (var icon in visibleIcons)
            {
                icon.InvalidateVisual(); window.UpdateLayout();
                var before = Pixels(Render(icon, 1));
                // Force an unavailable font on actual shell icons; paths must render identically.
                TextElement.SetFontFamily(icon, new FontFamily("PortPilot Deliberately Missing Icon Font"));
                icon.InvalidateVisual(); window.UpdateLayout();
                var after = Pixels(Render(icon, 1));
                icon.ClearValue(TextElement.FontFamilyProperty);
                if (!before.SequenceEqual(after) || !after.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0))
                    throw new InvalidOperationException($"Blank/font-dependent {icon.Kind} icon in {theme} theme: {icon.ActualWidth}x{icon.ActualHeight}, before alpha {before.Where((_, i) => i % 4 == 3).Sum(a => (int)a)}, after alpha {after.Where((_, i) => i % 4 == 3).Sum(a => (int)a)}, equal {before.SequenceEqual(after)}.");
            }
            var selected = Descendants(window.DashboardNav).OfType<Icon>().Single();
            var unselected = Descendants(window.PortsNav).OfType<Icon>().Single();
            if (((SolidColorBrush)selected.Foreground).Color != ((SolidColorBrush)window.DashboardNav.Foreground).Color
                || ((SolidColorBrush)unselected.Foreground).Color != ((SolidColorBrush)window.PortsNav.Foreground).Color)
                throw new InvalidOperationException("Navigation icon colors did not inherit theme/selection colors.");

            var atlas = new StackPanel { Background = (Brush)Application.Current.FindResource("SurfaceBrush"), Width = 320 };
            TextElement.SetForeground(atlas, (Brush)Application.Current.FindResource("TextBrush"));
            foreach (var kind in Enum.GetValues<IconKind>())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 40 };
                row.Children.Add(new TextBlock { Text = kind.ToString(), Width = 130, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
                foreach (var size in new[] { 9, 16, 32 }) row.Children.Add(new Icon { Kind = kind, Width = size, Height = size, Margin = new Thickness(8, 0, 8, 0) });
                atlas.Children.Add(row);
            }
            atlas.Measure(new Size(320, double.PositiveInfinity)); atlas.Arrange(new Rect(atlas.DesiredSize)); atlas.UpdateLayout();
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            {
                foreach (var icon in Descendants(atlas).OfType<Icon>())
                    if (!Pixels(Render(icon, scale)).Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0))
                        throw new InvalidOperationException($"Empty {icon.Kind} at {icon.Width} DIP/{scale} scale.");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(atlas, scale)));
                using var file = File.Create(Path.Combine(folder, $"icons-{theme}-{scale * 100:0}.png")); encoder.Save(file);
            }
        }
        themes.Apply("light");
        results.Add("Icons: actual shell raster unchanged with missing font; navigation inherits theme/selection colors; all 15 vector icons render at 9/16/32 DIP and 100/125/150/175/200% in light/dark: PASS");
    }

    private static RenderTargetBitmap Render(FrameworkElement element, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // Capture the icon's drawing without its parent's alignment/clip (e.g. a right-aligned chevron).
        var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (element is Icon) drawing.DrawDrawing(VisualTreeHelper.GetDrawing(element));
            else drawing.DrawRectangle(new VisualBrush(element) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
        }
        bitmap.Render(visual); return bitmap;
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0); return pixels;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
