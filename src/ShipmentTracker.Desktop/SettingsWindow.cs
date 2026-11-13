using System;
using System.Threading.Tasks;
using System.Windows;
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
    private readonly ComboBox hotkey = new() { ItemsSource = new[] { "None", "Ctrl+Alt+D", "Ctrl+Alt+T" } };
    public string ApiKey => key.Password.Trim();
    public bool Authorized => authorized.IsChecked == true;
    public bool Notifications => notifications.IsChecked == true;
    public bool AlwaysOnTop => onTop.IsChecked == true;
    public bool Startup => startup.IsChecked == true;
    public string Hotkey => hotkey.SelectedItem as string ?? "None";

    public SettingsWindow(TrackerState state)
    {
        Title = "Tracker settings";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Your own DHL API key (stored with Windows user protection)", TextWrapping = TextWrapping.Wrap });
        key.Password = state.ApiKey;
        key.Margin = new Thickness(0, 8, 0, 12);
        key.MaxLength = 1024;
        panel.Children.Add(key);
        authorized.IsChecked = state.AuthorizedAccess;
        notifications.IsChecked = state.Notifications;
        onTop.IsChecked = state.AlwaysOnTop;
        startup.IsChecked = state.StartAtSignIn;
        foreach (var box in new[] { authorized, notifications, onTop, startup })
        {
            box.Margin = new Thickness(0, 6, 0, 6);
            panel.Children.Add(box);
        }
        panel.Children.Add(new TextBlock { Text = "Show/hide shortcut (Win+W is not used)", Margin = new Thickness(0, 12, 0, 6) });
        hotkey.SelectedItem = state.Hotkey;
        panel.Children.Add(hotkey);
        panel.Children.Add(new TextBlock
        {
            Text = "The initial 250/day DHL allowance is described as development-only. Confirm permission for ongoing use with DHL. Checks require this app to remain running; Windows may suppress tray notifications. Delivered data is removed no later than 30 days after delivery. No cloud service or shared key is used.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6)
        });
        panel.Children.Add(MainWindow.Button("Save", () =>
        {
            if (ApiKey.Contains('\r') || ApiKey.Contains('\n'))
                throw new InvalidOperationException("The API key must be on one line.");
            DialogResult = true;
            return Task.CompletedTask;
        }));
        Content = panel;
    }
}
