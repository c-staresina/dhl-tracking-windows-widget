# DHL Tracking Windows Widget

A small, always-on-top Windows 11 desktop widget for checking a DHL shipment.
Press **Win+C** to show or hide it when Windows permits the shortcut.

## Run

1. Install the .NET 8 SDK on Windows.
2. Obtain a DHL Tracking API key from the [DHL Developer Portal](https://developer.dhl.com/).
3. From this directory, run:

   ```powershell
   dotnet run --project .\DhlTrackingWidget\DhlTrackingWidget.csproj
   ```

4. Enter a tracking number and API key, then select **Check status**. The key
   can also be supplied through the `DHL_API_KEY` environment variable.

The key is only held in the running app and sent to DHL's official tracking
endpoint; it is not saved to disk. Select **Open DHL tracking page** to view the
shipment on DHL's website.

Windows reserves some system shortcuts, and installed Copilot versions may
claim **Win+C**. If the shortcut cannot be registered, the app remains usable
as a normal window; the operating system's shortcut cannot be overridden by
this app.
