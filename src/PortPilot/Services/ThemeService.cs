using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PortPilot.Services;

public sealed class ThemeService : IDisposable
{
    private string current = "system";
    public ThemeService() => SystemEvents.UserPreferenceChanged += Changed;
    private void Changed(object sender, UserPreferenceChangedEventArgs e) { if (current == "system") Application.Current.Dispatcher.BeginInvoke(() => Apply(current)); }
    public void Apply(string theme)
    {
        current = theme;
        var dark = theme == "dark";
        if (theme == "system")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        var colors = new Dictionary<string, string>
        {
            ["BackgroundBrush"] = dark ? "#1C1C1E" : "#F5F5F7", ["SurfaceBrush"] = dark ? "#2C2C2E" : "#FFFFFF",
            ["SidebarBrush"] = dark ? "#232325" : "#EEEFF2", ["TextBrush"] = dark ? "#F5F5F7" : "#1D1D1F",
            ["SecondaryBrush"] = dark ? "#98989D" : "#6E6E73", ["BorderBrush"] = dark ? "#424246" : "#E5E5EA",
            ["HoverBrush"] = dark ? "#37373B" : "#F0F1F4", ["SelectionBrush"] = dark ? "#183A60" : "#E5F0FF",
            ["AccentBrush"] = dark ? "#409CFF" : "#007AFF", ["SuccessBrush"] = dark ? "#63D994" : "#248A54",
            ["DangerBrush"] = dark ? "#FF6961" : "#D73935", ["WarningBrush"] = dark ? "#FFB54A" : "#AF6A0A"
        };
        foreach (var (key, color) in colors) Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= Changed;
}
