using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PortPilot.Views;

public enum IconKind { Port, Home, Process, Network, Star, History, Settings, Command, ChevronDown, Refresh, Minimize, Maximize, Restore, Close, Search }

/// <summary>Decorative, font-independent icons drawn in a 24 × 24 coordinate space.</summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(IconKind), typeof(Icon),
        new FrameworkPropertyMetadata(IconKind.Port, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static readonly IReadOnlyDictionary<IconKind, Geometry> Shapes = new Dictionary<IconKind, string>
    {
        [IconKind.Port] = "M 7,3 L 17,3 17,13 C 17,16 15,18 12,18 C 9,18 7,16 7,13 Z M 10,7 L 10,11 M 14,7 L 14,11 M 12,18 L 12,22",
        [IconKind.Home] = "M 3,11 L 12,3 21,11 M 5,10 L 5,21 10,21 10,15 14,15 14,21 19,21 19,10",
        [IconKind.Process] = "M 7,7 L 17,7 17,17 7,17 Z M 10,10 L 14,10 14,14 10,14 Z M 9,3 L 9,7 M 15,3 L 15,7 M 9,17 L 9,21 M 15,17 L 15,21 M 3,9 L 7,9 M 3,15 L 7,15 M 17,9 L 21,9 M 17,15 L 21,15",
        [IconKind.Network] = "M 8,3 L 16,3 16,9 8,9 Z M 12,9 L 12,13 M 5,16 L 5,13 19,13 19,16 M 2,16 L 8,16 8,21 2,21 Z M 16,16 L 22,16 22,21 16,21 Z",
        [IconKind.Star] = "M 12,2 L 15,8.5 22,9.5 17,14.5 18.2,21.5 12,18.2 5.8,21.5 7,14.5 2,9.5 9,8.5 Z",
        [IconKind.History] = "M 3,11 A 9,9 0 1 1 5,18 M 3,5 L 3,11 9,11 M 12,7 L 12,12 16,14",
        [IconKind.Settings] = "M 9,3 L 15,3 15.7,6 18,7.4 21,6.8 23,11.2 20.7,13.3 20.3,16 21.5,18.6 17.5,21.5 15,19.8 12,20 9.5,22 5.5,19.2 6.4,16.3 5.5,13.7 2.5,12.5 3.5,7.7 6.6,7.4 8.5,5.5 Z M 15.5,12 A 3.5,3.5 0 1 1 8.5,12 A 3.5,3.5 0 1 1 15.5,12",
        [IconKind.Command] = "M 4,6 L 10,12 4,18 M 13,18 L 21,18",
        [IconKind.ChevronDown] = "M 5,9 L 12,16 19,9",
        [IconKind.Refresh] = "M 20,10 A 8,8 0 1 0 20,15 M 20,4 L 20,10 14,10",
        [IconKind.Minimize] = "M 5,12 L 19,12",
        [IconKind.Maximize] = "M 5,5 L 19,5 19,19 5,19 Z",
        [IconKind.Restore] = "M 5,8 L 16,8 16,19 5,19 Z M 8,8 L 8,5 19,5 19,16 16,16",
        [IconKind.Close] = "M 5,5 L 19,19 M 19,5 L 5,19",
        [IconKind.Search] = "M 17,10 A 7,7 0 1 1 3,10 A 7,7 0 1 1 17,10 M 15,15 L 21,21"
    }.ToDictionary(pair => pair.Key, pair => { var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry; });

    static Icon()
    {
        WidthProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(16d));
        HeightProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(16d));
        VerticalAlignmentProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(VerticalAlignment.Center));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0 || !Shapes.TryGetValue(Kind, out var geometry)) return;
        var pen = new Pen(Foreground, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawing.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        drawing.DrawGeometry(null, pen, geometry);
        drawing.Pop(); drawing.Pop();
    }
}
