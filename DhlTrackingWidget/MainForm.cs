using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DhlTrackingWidget;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModWin = 0x0008;
    private const uint VkC = 0x43;

    private readonly DhlTrackingClient trackingClient = new();
    private readonly TextBox trackingNumberInput = new();
    private readonly TextBox apiKeyInput = new();
    private readonly Button trackButton = new();
    private readonly Label statusLabel = new();
    private readonly Label detailsLabel = new();
    private readonly Label shortcutLabel = new();
    private readonly LinkLabel trackingLink = new();
    private CancellationTokenSource? activeRequest;

    public MainForm()
    {
        Text = "DHL Tracking";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;
        ClientSize = new Size(390, 325);
        Font = new Font("Segoe UI", 9F);
        KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape)
            {
                Hide();
            }
        };

        var title = new Label
        {
            Text = "DHL Tracking",
            Font = new Font("Segoe UI Semibold", 18F),
            AutoSize = true,
            Location = new Point(20, 16)
        };
        Controls.Add(title);

        Controls.Add(new Label
        {
            Text = "Tracking number",
            AutoSize = true,
            Location = new Point(20, 63)
        });
        trackingNumberInput.Location = new Point(20, 84);
        trackingNumberInput.Size = new Size(350, 27);
        trackingNumberInput.PlaceholderText = "Enter a DHL tracking number";
        trackingNumberInput.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                _ = TrackAsync();
            }
        };
        Controls.Add(trackingNumberInput);

        Controls.Add(new Label
        {
            Text = "DHL API key",
            AutoSize = true,
            Location = new Point(20, 121)
        });
        apiKeyInput.Location = new Point(20, 142);
        apiKeyInput.Size = new Size(350, 27);
        apiKeyInput.UseSystemPasswordChar = true;
        apiKeyInput.Text = Environment.GetEnvironmentVariable("DHL_API_KEY") ?? string.Empty;
        apiKeyInput.PlaceholderText = "Get a key from the DHL Developer Portal";
        Controls.Add(apiKeyInput);

        trackButton.Text = "Check status";
        trackButton.Location = new Point(20, 180);
        trackButton.Size = new Size(110, 32);
        trackButton.Click += async (_, _) => await TrackAsync();
        Controls.Add(trackButton);

        statusLabel.Text = "Enter a tracking number and your DHL API key.";
        statusLabel.Location = new Point(20, 224);
        statusLabel.Size = new Size(350, 22);
        statusLabel.Font = new Font("Segoe UI Semibold", 10F);
        Controls.Add(statusLabel);

        detailsLabel.Location = new Point(20, 249);
        detailsLabel.Size = new Size(350, 22);
        Controls.Add(detailsLabel);

        trackingLink.Text = "Open DHL tracking page";
        trackingLink.Location = new Point(20, 277);
        trackingLink.AutoSize = true;
        trackingLink.LinkClicked += (_, _) => OpenTrackingPage();
        Controls.Add(trackingLink);

        shortcutLabel.Text = "Win+C toggles this widget when available.";
        shortcutLabel.Size = new Size(350, 20);
        shortcutLabel.ForeColor = SystemColors.GrayText;
        shortcutLabel.Location = new Point(20, 300);
        Controls.Add(shortcutLabel);

        Shown += (_, _) => RegisterShortcut();
        FormClosed += (_, _) =>
        {
            activeRequest?.Cancel();
            activeRequest = null;
            UnregisterHotKey(Handle, HotkeyId);
        };
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            if (Visible)
            {
                Hide();
            }
            else
            {
                Show();
                Activate();
                trackingNumberInput.Focus();
            }

            return;
        }

        base.WndProc(ref message);
    }

    private void RegisterShortcut()
    {
        if (!RegisterHotKey(Handle, HotkeyId, ModWin, VkC))
        {
            shortcutLabel.Text = "Win+C is reserved by Windows; use this window to check tracking.";
        }
    }

    private async Task TrackAsync()
    {
        var trackingNumber = trackingNumberInput.Text.Trim();
        var apiKey = apiKeyInput.Text.Trim();
        if (trackingNumber.Length == 0 || trackingNumber.Length > 100)
        {
            statusLabel.Text = "Enter a tracking number (up to 100 characters).";
            detailsLabel.Text = string.Empty;
            return;
        }

        if (apiKey.Length == 0)
        {
            statusLabel.Text = "Enter your DHL API key to check shipment status.";
            detailsLabel.Text = string.Empty;
            return;
        }

        activeRequest?.Cancel();
        var requestCancellation = new CancellationTokenSource();
        activeRequest = requestCancellation;
        trackButton.Enabled = false;
        statusLabel.Text = "Checking shipment…";
        detailsLabel.Text = string.Empty;

        try
        {
            var shipment = await trackingClient.GetStatusAsync(
                trackingNumber,
                apiKey,
                requestCancellation.Token);
            if (!ReferenceEquals(activeRequest, requestCancellation))
            {
                return;
            }

            statusLabel.Text = shipment.Description;
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(shipment.Location))
            {
                details.Add(shipment.Location);
            }

            if (!string.IsNullOrWhiteSpace(shipment.Timestamp)
                && DateTimeOffset.TryParse(shipment.Timestamp, out var timestamp))
            {
                details.Add(timestamp.ToLocalTime().ToString("g"));
            }

            detailsLabel.Text = string.Join(" · ", details);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(activeRequest, requestCancellation))
            {
                statusLabel.Text = "DHL did not respond in time. Try again.";
            }
        }
        catch (HttpRequestException exception)
        {
            if (ReferenceEquals(activeRequest, requestCancellation))
            {
                statusLabel.Text = exception.StatusCode is { } code
                    ? $"DHL request failed ({(int)code}). Check your API key and try again."
                    : "Could not reach DHL. Check your connection and try again.";
            }
        }
        catch (InvalidOperationException exception)
        {
            if (ReferenceEquals(activeRequest, requestCancellation))
            {
                statusLabel.Text = exception.Message;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            if (ReferenceEquals(activeRequest, requestCancellation))
            {
                statusLabel.Text = "DHL returned an unexpected response.";
            }
        }
        finally
        {
            if (ReferenceEquals(activeRequest, requestCancellation))
            {
                activeRequest = null;
                trackButton.Enabled = true;
            }

            requestCancellation.Dispose();
        }
    }

    private void OpenTrackingPage()
    {
        var number = trackingNumberInput.Text.Trim();
        var url = number.Length == 0
            ? "https://www.dhl.com/global-en/home/tracking.html"
            : $"https://www.dhl.com/global-en/home/tracking.html?tracking-id={Uri.EscapeDataString(number)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
