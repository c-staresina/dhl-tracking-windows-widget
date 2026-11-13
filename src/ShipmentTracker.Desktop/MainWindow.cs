using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ShipmentTracker.Core;
using Forms = System.Windows.Forms;

namespace ShipmentTracker.Desktop;

internal sealed class MainWindow : Window
{
    private const string Attribution = "Delivered by Deutsche Post DHL Group";
    private readonly TrackerEngine engine;
    private readonly bool smoke;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly StackPanel cards = new();
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed };
    private readonly TextBox number = new() { MinWidth = 160, MaxLength = 100 };
    private readonly TextBox label = new() { MinWidth = 160, MaxLength = 100 };
    private readonly Forms.NotifyIcon tray;
    private WindowsIntegration? integration;
    private bool exiting;
    private bool initialized;
    private bool faulted;

    public MainWindow(TrackerEngine engine, bool smoke)
    {
        this.engine = engine;
        this.smoke = smoke;
        Title = "Shipment Tracker";
        Width = 440;
        Height = 620;
        MinWidth = 360;
        MinHeight = 380;
        Background = Brushes.WhiteSmoke;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Shipment Tracker", FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Independent desktop tracker", Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(summary);
        var actions = new WrapPanel();
        actions.Children.Add(Button("Settings", () => RunAsync(SettingsAsync)));
        actions.Children.Add(Button("Check when due", () => RunAsync(CheckAsync)));
        actions.Children.Add(Button("Hide to tray", () => { Hide(); return Task.CompletedTask; }));
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "Tracking number", Margin = new Thickness(0, 12, 0, 4) });
        number.SetValue(AutomationProperties.NameProperty, "Tracking number");
        panel.Children.Add(number);
        panel.Children.Add(new TextBlock { Text = "Label (optional)", Margin = new Thickness(0, 8, 0, 4) });
        label.SetValue(AutomationProperties.NameProperty, "Shipment label");
        panel.Children.Add(label);
        panel.Children.Add(Button("Add shipment", () => RunAsync(AddAsync)));
        panel.Children.Add(error);
        panel.Children.Add(cards);
        panel.Children.Add(new TextBlock { Text = Attribution, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "Closing hides to tray. Use the tray menu to exit.\nChecks and alerts require this app to be running and your PC awake and online.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.DimGray
        });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Shipment Tracker",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        tray.ContextMenuStrip.Items.Add("Show tracker", null, (_, _) => Dispatcher.Invoke(ShowTracker));
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Exit));
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowTracker); };
        tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowTracker);
        Loaded += async (_, _) => await RunAsync(InitializeAsync);
        Closing += OnClosing;
        timer.Tick += async (_, _) =>
        {
            if (!initialized || faulted || !await operation.WaitAsync(0)) return;
            try { await CheckAsync(); }
            catch (Exception exception) { ShowFailure(exception); }
            finally { operation.Release(); }
        };
    }

    private async Task InitializeAsync()
    {
        integration = new(this, () => { if (IsVisible) Hide(); else ShowTracker(); });
        var state = await engine.SnapshotAsync();
        Topmost = state.AlwaysOnTop;
        if (!smoke)
        {
            try { integration.SetHotkey(state.Hotkey); }
            catch (Win32Exception exception) { ShowFailure(exception); }
        }
        await RenderAsync();
        initialized = true;
        if (smoke)
        {
            await CheckAsync();
            if (cards.Children.Count != 1) throw new InvalidOperationException("Synthetic shipment card did not render.");
            var closer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            closer.Tick += (_, _) => { closer.Stop(); Exit(); };
            closer.Start();
        }
        else timer.Start();
    }

    private async Task CheckAsync()
    {
        var notification = await engine.CheckNextAsync();
        await RenderAsync();
        if (notification is not null)
            tray.ShowBalloonTip(10000, "Shipment delivered", notification + "\n" + Attribution, Forms.ToolTipIcon.Info);
    }

    private async Task AddAsync()
    {
        var tracking = number.Text.Trim();
        if (tracking.Length is < 1 or > 100 || tracking.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("Enter a tracking number containing letters, digits or hyphens (up to 100 characters).");
        var displayLabel = label.Text.Trim();
        await engine.ChangeAsync(state =>
        {
            if (state.Shipments.Any(s => s.BudgetId == Shipment.TrackingIdentity(tracking)))
                throw new InvalidOperationException("That shipment is already being tracked.");
            state.Shipments.Add(new() { TrackingNumber = tracking, Label = displayLabel });
        });
        number.Clear();
        label.Clear();
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        var state = await engine.SnapshotAsync();
        var now = DateTimeOffset.UtcNow;
        var active = state.Shipments.Count(s => !s.IsDelivered);
        var interval = RefreshPolicy.Interval(active);
        summary.Text = $"Active: {active}  |  Interval per shipment: {interval.TotalHours:0.##} hours\n" +
            $"Attempts in last 24h: {state.Attempts.Count}/250. At most 10 per shipment.\n" +
            (!state.AuthorizedAccess || state.ApiKey.Length == 0 ? "Live checks disabled. Configure your own key and authorized access in Settings." : "Background checks enabled while this app runs.") +
            (state.ServiceMessage.Length > 0 ? "\n" + state.ServiceMessage : "");
        cards.Children.Clear();
        foreach (var shipment in state.Shipments)
        {
            var card = new StackPanel { Margin = new Thickness(10) };
            card.Children.Add(new TextBlock { Text = shipment.Label.Length > 0 ? shipment.Label : shipment.TrackingNumber, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            if (shipment.Label.Length > 0) card.Children.Add(Text(shipment.TrackingNumber));
            card.Children.Add(Text(shipment.Status));
            card.Children.Add(Text("Estimated delivery: " + (shipment.EstimatedDelivery.Length > 0 ? shipment.EstimatedDelivery : "Not provided")));
            if (shipment.LatestEvent.Length > 0) card.Children.Add(Text(shipment.LatestEvent));
            if (shipment.Location.Length > 0) card.Children.Add(Text(shipment.Location));
            if (shipment.EventTime.Length > 0) card.Children.Add(Text("DHL event time: " + shipment.EventTime));
            card.Children.Add(Text("Last successful check: " + (shipment.LastSuccess?.ToLocalTime().ToString("g") ?? "Never")));
            card.Children.Add(Text(shipment.IsDelivered ? "Delivered - automatic checks stopped." :
                "Next permitted check: " + RefreshPolicy.NextAllowed(state, shipment, now).ToLocalTime().ToString("g")));
            if (shipment.Error.Length > 0)
                card.Children.Add(new TextBlock { Text = "Check failed; displayed data may be stale.\n" + shipment.Error, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed });
            var buttons = new WrapPanel();
            buttons.Children.Add(Button("Open on DHL", () =>
            {
                Process.Start(new ProcessStartInfo("https://www.dhl.com/global-en/home/tracking.html?tracking-id=" +
                    Uri.EscapeDataString(shipment.TrackingNumber)) { UseShellExecute = true });
                return Task.CompletedTask;
            }));
            buttons.Children.Add(Button("Remove", () => RunAsync(async () =>
            {
                await engine.ChangeAsync(s => s.Shipments.RemoveAll(x => x.BudgetId == shipment.BudgetId));
                await RenderAsync();
            })));
            card.Children.Add(buttons);
            cards.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 12, 0, 0), Child = card });
        }
    }

    private async Task SettingsAsync()
    {
        if (integration is null) throw new InvalidOperationException("App initialization did not complete.");
        var state = await engine.SnapshotAsync();
        var dialog = new SettingsWindow(state) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var previousHotkey = state.Hotkey;
        var startupChanged = state.StartAtSignIn != dialog.Startup;
        try
        {
            integration.SetHotkey(dialog.Hotkey);
            if (startupChanged) WindowsIntegration.SetStartup(dialog.Startup);
            await engine.ChangeAsync(s =>
            {
                s.ApiKey = dialog.ApiKey;
                s.AuthorizedAccess = dialog.Authorized;
                s.Notifications = dialog.Notifications;
                s.AlwaysOnTop = dialog.AlwaysOnTop;
                s.StartAtSignIn = dialog.Startup;
                s.Hotkey = dialog.Hotkey;
            });
        }
        catch
        {
            integration.SetHotkey(previousHotkey);
            if (startupChanged) WindowsIntegration.SetStartup(state.StartAtSignIn);
            throw;
        }
        Topmost = dialog.AlwaysOnTop;
        await RenderAsync();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (!await operation.WaitAsync(0))
        {
            error.Text = "Another operation is in progress. Please try again when it completes.";
            return;
        }
        try { error.Text = ""; await action(); }
        catch (Exception exception) { ShowFailure(exception); }
        finally { operation.Release(); }
    }

    private void ShowFailure(Exception exception)
    {
        var message = exception is InvalidOperationException or Win32Exception ? exception.Message :
            "The operation failed (" + exception.GetType().Name + "). Checks are paused to protect your saved data. Exit and restart after resolving local storage or access issues.";
        error.Text = message;
        if (exception is not (InvalidOperationException or Win32Exception))
        {
            faulted = true;
            timer.Stop();
        }
        if (smoke)
        {
            Console.Error.WriteLine(message);
            Environment.ExitCode = 1;
            Exit();
        }
        else MessageBox.Show(this, message, "Shipment Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    internal static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(8, 5, 8, 5) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception exception)
            {
                MessageBox.Show(exception is InvalidOperationException ? exception.Message :
                    "The action failed (" + exception.GetType().Name + "). No success has been assumed.",
                    "Shipment Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        return button;
    }

    private void ShowTracker()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exiting) { e.Cancel = true; Hide(); }
    }

    private void Exit()
    {
        if (operation.CurrentCount == 0 && !smoke)
        {
            error.Text = "A check is in progress. Please exit after it completes (at most 30 seconds).";
            ShowTracker();
            return;
        }
        exiting = true;
        timer.Stop();
        integration?.Dispose();
        tray.Visible = false;
        tray.Dispose();
        Close();
        Application.Current.Shutdown(Environment.ExitCode);
    }
}
