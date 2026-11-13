# Shipment Tracker for Windows

An independent, widget-style Windows desktop app for DHL shipment tracking.
It is **not** a Widgets-board provider and does not use Win+W.
No backend, scraping, shared API key, or Microsoft Store installation is needed.

See the [changelog](CHANGELOG.md) for changes.

## Requirements

- Windows 11 and .NET 10 SDK to build (the .NET Desktop Runtime 10 is sufficient for framework-dependent published output).
- Your own approved **Shipment Tracking - Unified (Pull)** API key.
- Confirm with DHL that your entitlement permits your intended use. DHL describes the initial
  250-call allowance as development-only; this app does not grant production authorization.
  Never share your developer login or put API keys in source code.

## Build, test and run

From the repository directory in PowerShell:

```powershell
dotnet build .\ShipmentTracker.slnx -c Release
dotnet test .\tests\ShipmentTracker.Tests\ShipmentTracker.Tests.csproj -c Release
dotnet run --project .\src\ShipmentTracker.Desktop\ShipmentTracker.Desktop.csproj
```

The WPF application uses the .NET SDK; no WinUI workload or Windows widget registration is required.
Tests use synthetic data and fake HTTP responses, never live shipments.

Create local framework-dependent output:

```powershell
dotnet publish .\src\ShipmentTracker.Desktop\ShipmentTracker.Desktop.csproj -c Release -o .\publish
```

Run `publish\ShipmentTracker.Desktop.exe`. Keep the published folder intact.
To distribute to coworkers, give them the published folder and require their **own** approved key
and .NET Desktop Runtime. There is no installer or automatic updater.

## Using the app

1. Open **Settings**, enter your key locally, and confirm you have approved DHL access.
   Leave authorization unchecked to keep live API calls disabled.
2. Add tracking numbers and optional private labels.
3. Use **Open on DHL** for the latest website status at any time. This opens the official site
   in your browser without an API call; it does not refresh the app's cached data.
4. Closing the window hides it to the system tray; use the tray menu's **Exit** to stop checking.
   Left-click the tray icon to reopen. Windows may put the icon in its overflow menu.
5. Settings include always-on-top, delivery alerts, optional Ctrl+Alt+D or Ctrl+Alt+T show/hide
   shortcuts, and opt-in start-at-sign-in. An unavailable shortcut is reported explicitly.
   Startup adds an entry under your Windows user's `Run` registry key; uncheck it to remove it.
   Enable startup only from the published executable at its permanent location.

## Request limits and delivery alerts

All attempted requests, including failures, share a persisted **rolling-24-hour** budget:

- At most **250 requests overall**, at most **10 per shipment**, and at least **five seconds between requests**.
- Per-shipment interval in whole minutes: `max(144, ceiling(1440 * active shipments / 250))`.
  One through 25 shipments use **2 hours 24 minutes**; 26 use **2 hours 30 minutes**;
  50 use **4 hours 48 minutes**. New shipments start with staggered checks.
- **Check when due** runs the next permitted check only. It cannot bypass the interval or either
  quota. Reopening the window does not force an early fetch.
- No catch-up bursts after sleep, and no immediate retries after errors.
- Removing/re-adding a shipment or changing the key does not reset recent quota accounting.
- Other apps/devices using your DHL account can consume quota this local app cannot see.
  DHL throttling pauses all checks. If no Retry-After is supplied, the app conservatively waits 24 hours.

Delivery alerts use the Windows tray-notification mechanism, not guaranteed persistent
Notification Center toasts. Windows settings may suppress them. The app must remain running,
signed in, awake and online; status detection can lag one interval, longer after downtime.
Only a newly observed transition to delivered triggers an alert, not an already-delivered
shipment added for the first time. Delivered shipments stop checking. Notification state is
saved before showing alerts to avoid duplicates on restart; a crash between these operations
can lose an alert. There is no exactly-once or instant-delivery guarantee.

The ten-fetch policy is separate from the account allowance:
[DHL quota guidance](https://support-developer.dhl.com/support/solutions/articles/47001248773-i-m-running-out-of-calls-how-much-quota-should-i-request-for-an-upgrade-).
Push is excluded from this version; it would need separate access, eligible services, and a
reachable HTTPS receiver.

## Data, safety and limitations

- The key, minimal shipment data, settings and quota ledger are encrypted together with
  Windows DPAPI for the current Windows user, at `%LOCALAPPDATA%\LocalShipmentTracker\state.dat`.
  The file is not portable to other users/machines. Only one instance can access it at a time.
- No application analytics, response archives, logs of tracking numbers, advertisements, or shared cloud storage.
  Labels and shipment status remain private to this local user.
- Display attribution: **Delivered by Deutsche Post DHL Group**. This is not an official DHL app.
- Shipment data is deleted on removal or by 30 days after its reported delivery instant.
  If a delivery time has no timezone, an earliest-possible UTC+14 bound is used solely for
  conservative retention; the displayed original timestamp is not converted or relabeled.
  If no usable delivery date is supplied, the delivered record is purged immediately.
  Expired records are purged before display on launch/resume. Deletion cannot run while the PC is off.
  Request hashes/timestamps (without tracking numbers) expire after 24 hours.
- Estimates, locations and events are optional. Original DHL date/time text is shown without inventing
  timezones. Multiple matches are explicit errors: use DHL's website instead of guessing.
- Network errors preserve prior unexpired data with a stale warning. Invalid/unapproved keys,
  not-found results and throttling are surfaced. A storage failure pauses automatic checks rather
  than resetting quota or silently discarding data.
- Only track shipments you are authorized to access. Acting for senders/recipients may require
  additional consent. Follow DHL's applicable data protection and retention requirements.

An isolated native smoke check is available:

```powershell
.\src\ShipmentTracker.Desktop\bin\Release\net10.0-windows\ShipmentTracker.Desktop.exe --smoke-test
```

It uses a temporary synthetic record, checks DPAPI persistence and native window/tray startup,
then exits and removes its temporary files. It does not configure startup, register a hotkey,
send an alert, open a tracking website, or contact DHL. It is not a substitute for checking your
notification settings, a locally configured key, or sign-in behavior.

To stop using the app, disable start-at-sign-in in Settings and exit from the tray. Delete the
published folder and, if desired, the specific `%LOCALAPPDATA%\LocalShipmentTracker` data folder
after exiting. Corrupt or undecryptable storage is reported, not automatically replaced; keep
your original data file when investigating an error.
