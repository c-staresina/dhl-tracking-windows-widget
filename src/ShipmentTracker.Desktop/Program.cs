using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using ShipmentTracker.Core;

namespace ShipmentTracker.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var smoke = args.Contains("--smoke-test");
            var directory = smoke ? Path.Combine(Path.GetTempPath(), "ShipmentTracker-Smoke-" + Guid.NewGuid().ToString("N"))
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalShipmentTracker");
            using var store = new EncryptedStore(directory);
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
            var state = store.Load();
            if (smoke)
            {
                state.Shipments.Add(new() { TrackingNumber = "SYNTHETIC-SMOKE", Label = "Synthetic smoke fixture" });
                state.CompactLayout = true;
                state.DailyCheckTime = new TimeOnly(8, 0);
                state.DailyScheduleChangedAt = DateTimeOffset.UtcNow;
                store.SaveAsync(state, default).GetAwaiter().GetResult();
                state = store.Load();
                if (state.Shipments.Count != 1 || !state.CompactLayout ||
                    state.DailyCheckTime != new TimeOnly(8, 0) || state.DailyScheduleChangedAt is null)
                    throw new InvalidDataException("Encrypted persistence smoke test failed.");
            }
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var appearance = new AppAppearance(application);
            var window = new MainWindow(new TrackerEngine(state, store, new DhlTrackingClient(http)), smoke);
            application.MainWindow = window;
            window.Show();
            if (args.Contains("--tray") && !smoke) window.Hide();
            application.Run();
            if (smoke)
            {
                store.Dispose();
                File.Delete(Path.Combine(directory, "state.dat"));
                File.Delete(Path.Combine(directory, "instance.lock"));
                Directory.Delete(directory);
            }
            return Environment.ExitCode;
        }
        catch (Exception exception)
        {
            if (args.Contains("--smoke-test"))
                Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
            else
                MessageBox.Show("Shipment Tracker could not start. Another instance may be running, or local storage could not be read. " +
                    "Your saved file has not been reset.\n\n" + exception.Message,
                    "Shipment Tracker - startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
