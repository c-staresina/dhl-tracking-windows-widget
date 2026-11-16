using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBox number = new() { MinWidth = 160, MaxLength = 100 };
    private readonly TextBox label = new() { MinWidth = 160, MaxLength = 100 };
    private readonly Expander addShipment = new() { Header = "Add a shipment" };
    private readonly Button layoutToggle;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon parcelIcon;
    private WindowsIntegration? integration;
    private bool exiting;
    private bool initialized;
    private bool faulted;
    private bool compactLayout;

    public MainWindow(TrackerEngine engine, bool smoke)
    {
        this.engine = engine;
        this.smoke = smoke;
        SetResourceReference(StyleProperty, typeof(Window));
        SourceInitialized += (_, _) => AppAppearance.StyleCaption(this);
        Title = "Shipment Tracker";
        Width = 480;
        Height = Math.Min(780, SystemParameters.WorkArea.Height - 32);
        MinWidth = 420;
        MinHeight = 380;
        parcelIcon = ParcelIcon.Create();
        Icon = ParcelIcon.WindowImage(parcelIcon);
        error.SetResourceReference(StyleProperty, "ErrorText");
        summary.SetResourceReference(TextBlock.LineHeightProperty, "SummaryLineHeight");
        var panel = new StackPanel();
        panel.SetResourceReference(MarginProperty, "PageMargin");
        var header = new DockPanel();
        header.SetResourceReference(MarginProperty, "HeaderMargin");
        layoutToggle = Button("Compact", () => RunAsync(ToggleLayoutAsync));
        layoutToggle.Margin = new Thickness(8, 0, 0, 0);
        layoutToggle.Padding = new Thickness(9, 6, 9, 6);
        layoutToggle.FontSize = 12;
        layoutToggle.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(layoutToggle, Dock.Right);
        header.Children.Add(layoutToggle);
        var mark = new Image { Source = Icon, Margin = new Thickness(0, 0, 12, 0) };
        mark.SetResourceReference(WidthProperty, "HeaderIconSize");
        mark.SetResourceReference(HeightProperty, "HeaderIconSize");
        header.Children.Add(mark);
        var title = new StackPanel();
        var titleText = new TextBlock { Text = "Shipment Tracker", FontWeight = FontWeights.SemiBold };
        titleText.SetResourceReference(FontSizeProperty, "WindowTitleSize");
        title.Children.Add(titleText);
        title.Children.Add(Muted("Your DHL deliveries, at a glance"));
        header.Children.Add(title);
        panel.Children.Add(header);
        panel.Children.Add(Surface(summary));
        var actions = new WrapPanel();
        actions.Children.Add(Button("Settings", () => RunAsync(SettingsAsync)));
        var check = Button("Check when due", () => RunAsync(CheckAsync));
        check.ToolTip = "Check the next eligible shipment. The preferred daily schedule, daily limits and minimum intervals still apply.";
        actions.Children.Add(check);
        actions.Children.Add(Button("Hide to tray", () => { Hide(); return Task.CompletedTask; }));
        panel.Children.Add(actions);
        var addPanel = new StackPanel();
        addPanel.Children.Add(new TextBlock { Text = "Tracking number", Margin = new Thickness(0, 14, 0, 6) });
        number.SetValue(AutomationProperties.NameProperty, "Tracking number");
        addPanel.Children.Add(number);
        addPanel.Children.Add(new TextBlock { Text = "Label (optional)", Margin = new Thickness(0, 12, 0, 6) });
        label.SetValue(AutomationProperties.NameProperty, "Shipment label");
        addPanel.Children.Add(label);
        var add = Button("Add shipment", () => RunAsync(AddAsync));
        add.SetResourceReference(StyleProperty, "PrimaryButton");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 14, 0, 0);
        addPanel.Children.Add(add);
        addShipment.Content = addPanel;
        var addSurface = Surface(addShipment);
        addSurface.SetResourceReference(MarginProperty, "SectionMargin");
        panel.Children.Add(addSurface);
        panel.Children.Add(error);
        var shipmentsHeading = new TextBlock { Text = "Your shipments", FontWeight = FontWeights.SemiBold };
        shipmentsHeading.SetResourceReference(FontSizeProperty, "SectionTitleSize");
        shipmentsHeading.SetResourceReference(MarginProperty, "SectionMargin");
        panel.Children.Add(shipmentsHeading);
        panel.Children.Add(cards);
        var attribution = Muted(Attribution);
        attribution.Margin = new Thickness(0, 22, 0, 0);
        panel.Children.Add(attribution);
        panel.Children.Add(Muted("Independent app. Closing hides to tray; use the tray menu to exit.\nChecks require this app to be running and your PC awake and online."));
        var scroller = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroller.SetResourceReference(BackgroundProperty, "CanvasBrush");
        Content = scroller;
        tray = new Forms.NotifyIcon
        {
            Icon = parcelIcon,
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
        addShipment.IsExpanded = state.Shipments.Count == 0;
        ApplyLayout(state.CompactLayout);
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
            var originalWidth = Width;
            await ToggleLayoutAsync();
            if (compactLayout || Width <= originalWidth || (await engine.SnapshotAsync()).CompactLayout)
                throw new InvalidOperationException("Comfortable layout smoke test failed.");
            await ToggleLayoutAsync();
            if (!compactLayout || Width >= originalWidth + 1 || !(await engine.SnapshotAsync()).CompactLayout)
                throw new InvalidOperationException("Compact layout smoke test failed.");
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

    private async Task ToggleLayoutAsync()
    {
        var compact = !compactLayout;
        await engine.ChangeAsync(state => state.CompactLayout = compact);
        ApplyLayout(compact);
        await RenderAsync();
    }

    private void ApplyLayout(bool compact)
    {
        compactLayout = compact;
        Resources["BodyFontSize"] = compact ? 13d : 14d;
        Resources["WindowTitleSize"] = compact ? 21d : 26d;
        Resources["SectionTitleSize"] = compact ? 16d : 18d;
        Resources["SummaryLineHeight"] = compact ? 20d : 23d;
        Resources["HeaderIconSize"] = compact ? 32d : 42d;
        Resources["ButtonHeight"] = compact ? 32d : 38d;
        Resources["InputHeight"] = compact ? 34d : 40d;
        Resources["PageMargin"] = new Thickness(compact ? 14 : 24);
        Resources["HeaderMargin"] = new Thickness(0, 0, 0, compact ? 12 : 22);
        Resources["SectionMargin"] = new Thickness(0, compact ? 12 : 20, 0, 0);
        Resources["SurfacePadding"] = new Thickness(compact ? 12 : 18);
        Resources["ButtonPadding"] = compact ? new Thickness(10, 6, 10, 6) : new Thickness(14, 9, 14, 9);
        Resources["InputPadding"] = compact ? new Thickness(8, 6, 8, 6) : new Thickness(10, 9, 10, 9);
        Resources["ButtonMargin"] = compact ? new Thickness(0, 6, 6, 0) : new Thickness(0, 10, 8, 0);
        MinWidth = compact ? 360 : 420;
        Width = compact ? 400 : 480;
        Height = Math.Min(compact ? 620 : 780, SystemParameters.WorkArea.Height - 32);
        layoutToggle.Content = compact ? "Comfortable" : "Compact";
        layoutToggle.ToolTip = compact ? "Current layout: Compact. Switch to Comfortable." : "Current layout: Comfortable. Switch to Compact.";
        layoutToggle.SetValue(AutomationProperties.NameProperty, compact ? "Switch to comfortable layout" : "Switch to compact layout");
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
            state.Shipments.Add(new() { TrackingNumber = tracking, Label = displayLabel, AddedAt = DateTimeOffset.UtcNow });
        });
        number.Clear();
        label.Clear();
        addShipment.IsExpanded = false;
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        var state = await engine.SnapshotAsync();
        var now = DateTimeOffset.UtcNow;
        var active = state.Shipments.Count(s => !s.IsDelivered);
        var interval = RefreshPolicy.Interval(active);
        summary.Text = $"{active} active  /  Minimum spacing: {(int)interval.TotalHours}h {interval.Minutes:00}m\n" +
            (state.DailyCheckTime is { } preferred ? $"Daily target: {preferred:HH:mm} local (staggered)\n" : "") +
            $"{state.Attempts.Count} of 250 requests used in the last 24h\n" +
            (!state.AuthorizedAccess || state.ApiKey.Length == 0 ? "Live checks disabled. Add your key and confirm approved access in Settings." : "Background checks enabled while this app runs.") +
            (state.ServiceMessage.Length > 0 ? "\n" + state.ServiceMessage : "");
        cards.Children.Clear();
        if (state.Shipments.Count == 0)
        {
            var empty = new StackPanel();
            empty.Children.Add(new TextBlock { Text = "No shipments yet", FontSize = 16, FontWeight = FontWeights.SemiBold });
            empty.Children.Add(Muted("Add a tracking number above to see its latest status here. Each shipment is checked at most ten times per day."));
            var emptySurface = Surface(empty);
            emptySurface.Margin = new Thickness(0, 12, 0, 0);
            cards.Children.Add(emptySurface);
        }
        foreach (var shipment in state.Shipments)
        {
            var card = new StackPanel();
            var badgeText = new TextBlock
            {
                Text = shipment.IsDelivered ? "Delivered" : shipment.HasBaseline ? "Tracking" : "Pending",
                FontSize = 12, FontWeight = FontWeights.SemiBold
            };
            if (shipment.IsDelivered) badgeText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            var badge = new Border { Child = badgeText, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
            badge.SetResourceReference(Border.BackgroundProperty, shipment.IsDelivered ? "SuccessSurfaceBrush" : "ControlBrush");
            card.Children.Add(badge);
            card.Children.Add(new TextBlock
            {
                Text = shipment.Label.Length > 0 ? shipment.Label : shipment.TrackingNumber,
                FontSize = compactLayout ? 16 : 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, compactLayout ? 8 : 12, 0, 0)
            });
            if (shipment.Label.Length > 0) card.Children.Add(Muted(shipment.TrackingNumber));
            var status = Text(shipment.Status);
            status.Margin = new Thickness(0, 10, 0, 0);
            card.Children.Add(status);
            var buttons = new WrapPanel();
            card.Children.Add(buttons);
            var eta = Muted("ESTIMATED DELIVERY");
            eta.FontSize = 11;
            eta.Margin = new Thickness(0, compactLayout ? 10 : 16, 0, 4);
            card.Children.Add(eta);
            card.Children.Add(new TextBlock
            {
                Text = shipment.EstimatedDelivery.Length > 0 ? shipment.EstimatedDelivery : "Not provided",
                FontWeight = FontWeights.SemiBold
            });
            if (shipment.LatestEvent.Length > 0) card.Children.Add(Text(shipment.LatestEvent));
            if (shipment.Location.Length > 0) card.Children.Add(Text(shipment.Location));
            if (shipment.EventTime.Length > 0) card.Children.Add(Muted("DHL event time: " + shipment.EventTime));
            var lastCheck = Muted("Last successful check: " + (shipment.LastSuccess?.ToLocalTime().ToString("g") ?? "Never"));
            lastCheck.Margin = new Thickness(0, compactLayout ? 8 : 14, 0, 0);
            card.Children.Add(lastCheck);
            card.Children.Add(Muted(shipment.IsDelivered ? "Delivered - automatic checks stopped." :
                "Next permitted check: " + RefreshPolicy.NextAllowed(state, shipment, now).ToLocalTime().ToString("g")));
            if (shipment.Error.Length > 0)
            {
                var warning = Text("Check failed; displayed data may be stale.\n" + shipment.Error);
                warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
                var warningSurface = new Border { Child = warning, Padding = new Thickness(10), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 0) };
                warningSurface.SetResourceReference(Border.BackgroundProperty, "ErrorSurfaceBrush");
                card.Children.Add(warningSurface);
            }
            buttons.Children.Add(Button("Open on DHL", () =>
            {
                Process.Start(new ProcessStartInfo("https://www.dhl.com/global-en/home/tracking.html?submit=1&tracking-id=" +
                    Uri.EscapeDataString(shipment.TrackingNumber)) { UseShellExecute = true });
                return Task.CompletedTask;
            }));
            buttons.Children.Add(Button("Remove", () => RunAsync(async () =>
            {
                await engine.ChangeAsync(s => s.Shipments.RemoveAll(x => x.BudgetId == shipment.BudgetId));
                await RenderAsync();
            })));
            var surface = Surface(card);
            surface.Margin = new Thickness(0, 12, 0, 0);
            cards.Children.Add(surface);
        }
    }

    private async Task SettingsAsync()
    {
        if (integration is null) throw new InvalidOperationException("App initialization did not complete.");
        var state = await engine.SnapshotAsync();
        var dialog = new SettingsWindow(state) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var previousHotkey = state.Hotkey;
        var dailyCheckTime = dialog.DailyCheckTime;
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
                if (s.DailyCheckTime != dailyCheckTime)
                {
                    s.DailyCheckTime = dailyCheckTime;
                    s.DailyScheduleChangedAt = dailyCheckTime.HasValue ? DateTimeOffset.UtcNow : null;
                }
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

    internal static TextBlock Muted(string text)
    {
        var block = Text(text);
        block.FontSize = 12;
        block.SetResourceReference(StyleProperty, "MutedText");
        return block;
    }

    internal static Border Surface(UIElement child)
    {
        var surface = new Border { Child = child };
        surface.SetResourceReference(StyleProperty, "Surface");
        return surface;
    }

    internal static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text };
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
        parcelIcon.Dispose();
        Close();
        Application.Current.Shutdown(Environment.ExitCode);
    }
}
