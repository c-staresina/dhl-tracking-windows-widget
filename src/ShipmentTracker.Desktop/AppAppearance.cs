using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace ShipmentTracker.Desktop;

internal sealed class AppAppearance : IDisposable
{
    private readonly Application application;
    private bool disposed;

    public AppAppearance(Application application)
    {
        this.application = application;
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/ShipmentTracker.Desktop;component/Theme.xaml", UriKind.Relative)
        });
        FollowWindows();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
    }

    private void FollowWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        ApplyPalette(key?.GetValue("AppsUseLightTheme") is int value && value == 0, SystemParameters.HighContrast);
    }

    internal void ApplyPalette(bool dark, bool highContrast)
    {
        Set("CanvasBrush", highContrast ? SystemColors.WindowColor : Color(dark ? "#17191D" : "#F3F5F7"));
        Set("SurfaceBrush", highContrast ? SystemColors.WindowColor : Color(dark ? "#22252B" : "#FFFFFF"));
        Set("ControlBrush", highContrast ? SystemColors.ControlColor : Color(dark ? "#2D3139" : "#F6F7F9"));
        Set("OutlineBrush", highContrast ? SystemColors.WindowTextColor : Color(dark ? "#4A515E" : "#CCD2DA"));
        Set("InkBrush", highContrast ? SystemColors.WindowTextColor : Color(dark ? "#F3F4F7" : "#1C2533"));
        Set("MutedBrush", highContrast ? SystemColors.WindowTextColor : Color(dark ? "#B7C0CD" : "#586577"));
        Set("AccentBrush", highContrast ? SystemColors.HighlightColor : Color("#FFCC00"));
        Set("AccentInkBrush", highContrast ? SystemColors.HighlightTextColor : Color("#242015"));
        Set("FocusBrush", highContrast ? SystemColors.HighlightColor : Color(dark ? "#8DBAFF" : "#205EB3"));
        Set("SuccessBrush", highContrast ? SystemColors.WindowTextColor : Color(dark ? "#89D9AD" : "#17663B"));
        Set("SuccessSurfaceBrush", highContrast ? SystemColors.WindowColor : Color(dark ? "#203A2E" : "#E8F5ED"));
        Set("ErrorBrush", highContrast ? SystemColors.WindowTextColor : Color(dark ? "#FFABA7" : "#A02020"));
        Set("ErrorSurfaceBrush", highContrast ? SystemColors.WindowColor : Color(dark ? "#42292D" : "#FFF0EE"));
        foreach (Window window in application.Windows) StyleCaption(window);
    }

    internal static void StyleCaption(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var brush = (SolidColorBrush)window.FindResource("CanvasBrush");
        var dark = !SystemParameters.HighContrast && brush.Color.R < 128 ? 1 : 0;
        var result = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (result != 0)
            System.Diagnostics.Trace.TraceWarning("Windows could not apply the title-bar theme (HRESULT {0:X8}).", result);
    }

    private static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);

    private void Set(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        application.Resources[key] = brush;
    }

    private void Refresh()
    {
        if (disposed || application.Dispatcher.HasShutdownStarted) return;
        application.Dispatcher.InvokeAsync(() => { if (!disposed) FollowWindows(); });
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Refresh();
    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Refresh();
    }

    public void Dispose()
    {
        disposed = true;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
