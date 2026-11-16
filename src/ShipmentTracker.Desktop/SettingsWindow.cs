using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ShipmentTracker.Core;

namespace ShipmentTracker.Desktop;

internal sealed class SettingsWindow : Window
{
    private readonly PasswordBox key = new();
    private readonly CheckBox authorized = new() { Content = "I have approved DHL access for this use." };
    private readonly CheckBox notifications = new() { Content = "Delivery notifications" };
    private readonly CheckBox onTop = new() { Content = "Always on top" };
    private readonly CheckBox startup = new() { Content = "Start in tray when I sign in (changes Windows startup)" };
    private readonly ComboBox hotkey = new() { ItemsSource = new[] { "None", "Win+Alt+P", "Ctrl+Alt+D", "Ctrl+Alt+T" } };
    private readonly CheckBox dailySchedule = new();
    private readonly TextBox dailyTime = new() { MaxLength = 5 };
    public string ApiKey => key.Password.Trim();
    public bool Authorized => authorized.IsChecked == true;
    public bool Notifications => notifications.IsChecked == true;
    public bool AlwaysOnTop => onTop.IsChecked == true;
    public bool Startup => startup.IsChecked == true;
    public string Hotkey => hotkey.SelectedItem as string ?? "None";
    public TimeOnly? DailyCheckTime
    {
        get
        {
            if (dailySchedule.IsChecked != true) return null;
            if (!TimeOnly.TryParseExact(dailyTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new InvalidOperationException("Enter the daily check time in 24-hour HH:mm format, for example 08:00 or 14:30.");
            return time;
        }
    }

    public SettingsWindow(TrackerState state)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        SourceInitialized += (_, _) =>
        {
            Icon = Owner?.Icon;
            AppAppearance.StyleCaption(this);
        };
        Title = "Tracker settings";
        Width = 500;
        Height = Math.Min(730, SystemParameters.WorkArea.Height - 32);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel();
        layout.SetResourceReference(BackgroundProperty, "CanvasBrush");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 6, 24, 20) };
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
        panel.Children.Add(new TextBlock { Text = "Make it yours", FontSize = 26, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(MainWindow.Muted("Appearance follows your Windows app theme."));
        var access = new StackPanel();
        access.Children.Add(Heading("DHL access"));
        access.Children.Add(MainWindow.Muted("Use your own approved Shipment Tracking - Unified key. It stays encrypted on this PC."));
        access.Children.Add(new TextBlock { Text = "API key", Margin = new Thickness(0, 14, 0, 0) });
        key.SetValue(AutomationProperties.NameProperty, "Your own DHL API key");
        key.Password = state.ApiKey;
        key.Margin = new Thickness(0, 8, 0, 12);
        key.MaxLength = 1024;
        access.Children.Add(key);
        authorized.IsChecked = state.AuthorizedAccess;
        notifications.IsChecked = state.Notifications;
        onTop.IsChecked = state.AlwaysOnTop;
        startup.IsChecked = state.StartAtSignIn;
        authorized.Content = new TextBlock { Text = "I have approved DHL access for this use.", TextWrapping = TextWrapping.Wrap };
        access.Children.Add(authorized);
        var accessSurface = MainWindow.Surface(access);
        accessSurface.Margin = new Thickness(0, 20, 0, 0);
        panel.Children.Add(accessSurface);
        var schedule = new StackPanel();
        schedule.Children.Add(Heading("Check schedule"));
        dailySchedule.Content = new TextBlock { Text = "Align checks to a preferred daily time", TextWrapping = TextWrapping.Wrap };
        dailySchedule.IsChecked = state.DailyCheckTime.HasValue;
        dailySchedule.Margin = new Thickness(0, 8, 0, 0);
        schedule.Children.Add(dailySchedule);
        schedule.Children.Add(new TextBlock { Text = "Daily target (local time, HH:mm)", Margin = new Thickness(0, 12, 0, 6) });
        dailyTime.SetValue(AutomationProperties.NameProperty, "Preferred daily check time in 24-hour HH:mm format");
        dailyTime.Text = state.DailyCheckTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "08:00";
        dailyTime.IsEnabled = dailySchedule.IsChecked == true;
        dailySchedule.Checked += (_, _) => dailyTime.IsEnabled = true;
        dailySchedule.Unchecked += (_, _) => dailyTime.IsEnabled = false;
        schedule.Children.Add(dailyTime);
        schedule.Children.Add(MainWindow.Muted("Minimum spacing: 2h30m, longer for larger shipment counts. A daily target normally gives nine checks, skipping the check too close to the next day's target. All shipments are staggered. Uses your Windows time zone."));
        schedule.Children.Add(MainWindow.Muted("Enabling or changing the target waits until its next occurrence. Quotas, sleep and throttling can delay it. The skipped check creates a longer gap; DHL does not specify a tolerance for its even-distribution expectation."));
        var scheduleSurface = MainWindow.Surface(schedule);
        scheduleSurface.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(scheduleSurface);
        var preferences = new StackPanel();
        preferences.Children.Add(Heading("Desktop preferences"));
        foreach (var box in new[] { notifications, onTop, startup })
        {
            box.Content = new TextBlock { Text = box.Content.ToString(), TextWrapping = TextWrapping.Wrap };
            box.Margin = new Thickness(0, 8, 0, 0);
            preferences.Children.Add(box);
        }
        preferences.Children.Add(new TextBlock { Text = "Show/hide shortcut", Margin = new Thickness(0, 16, 0, 6) });
        hotkey.SetValue(AutomationProperties.NameProperty, "Show or hide shortcut");
        hotkey.SelectedItem = state.Hotkey;
        preferences.Children.Add(hotkey);
        preferences.Children.Add(MainWindow.Muted("Shortcuts toggle show/hide while the app runs in the tray. Win+W is not used. Windows-key combinations may be reserved or unavailable. Windows startup changes only when you save."));
        var preferencesSurface = MainWindow.Surface(preferences);
        preferencesSurface.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(preferencesSurface);
        var limits = MainWindow.Muted("Up to 10 checks per shipment in any rolling 24 hours; 250 requests overall. Initial DHL access is described as development-only: confirm permission for ongoing use. Checks require the app to remain running; Windows may suppress tray alerts. Delivered data is removed no later than 30 days after delivery.");
        limits.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(limits);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 10, 8, 0) };
        footer.Children.Add(cancel);
        var save = MainWindow.Button("Save", () =>
        {
            if (ApiKey.Contains('\r') || ApiKey.Contains('\n'))
                throw new InvalidOperationException("The API key must be on one line.");
            _ = DailyCheckTime;
            DialogResult = true;
            return Task.CompletedTask;
        });
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        footer.Children.Add(save);
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold };
}
