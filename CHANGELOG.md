# Changelog

## Unreleased

### Added

- Optional **Win+Alt+P** global shortcut to toggle the tracker window, with explicit
  conflict reporting and existing shortcut choices preserved.
- Optional persisted daily check target in Windows local time, staggered across shipments,
  skipping checks that would be too close to the next target while retaining all quota guards.
- Validation of daily target input and handling of missing/repeated daylight-saving
  times. Enabling or changing the target waits until its next occurrence.
- Compact / Comfortable tracker layout toggle with a persisted selection; switching
  layouts preserves pending input and does not trigger a tracking request.
- Windows-following light/dark styling, high-contrast system colors, rounded shipment
  cards, refreshed settings, and a custom parcel icon for the window and system tray.
- Tests covering preferred daily schedules, rolling budgets, delayed checks, restarts,
  daylight-saving changes, and layout persistence without resetting quota.

### Changed

- Increased baseline check spacing from 2h24m to 2h30m. A preferred daily target
  normally gives nine checks per cycle, with a four-hour gap before the next target.
  Larger shipment counts can increase spacing.
- **Open on DHL** now requests automatic tracking submission, avoiding an extra Track
  click when DHL's website permits it. Cookie-consent or security prompts may still appear.
- Extended the isolated native smoke check to verify encrypted layout/daily-target
  persistence and both layout sizes.

### Limitations

- The preferred daily target is optional and disabled by default; it cannot bypass
  minimum spacing, rolling quotas, or throttling. Sleep/offline time can delay a check.
- An anchored daily pattern has a longer boundary gap; DHL's even-distribution
  expectation has no published tolerance, so numeric guards alone do not guarantee approval.
- Global shortcuts require the app to be running. Windows-key combinations may be
  reserved or unavailable; existing shortcut choices are not changed automatically.

## Initial implementation

### Added

- Widget-style Windows desktop app using DHL's Unified Tracking Pull API, with
  shipment labels, status, ETA, latest event, and links to official DHL tracking.
- System-tray access, optional global shortcuts, always-on-top, and opt-in
  start-at-sign-in.
- Per-user API keys and local state protected with Windows DPAPI.
- Persisted rolling-24-hour limits of 250 attempted requests overall and ten per
  shipment, with five-second global spacing and quota-aware polling intervals.
- Delivery-transition alerts, stopped polling for delivered shipments, and
  delivery-data retention cleanup.
- Explicit authorization, network, throttling, ambiguous-result, and storage errors.
- Synthetic automated tests and an isolated native startup/encryption smoke test.
- Build, publish, setup, privacy, and operating-limit documentation.

### Limitations

- This is a standalone desktop app, not a Win+W Widgets-board provider.
- Each user needs their own approved DHL API key; Push is not included.
- Live DHL requests, actual notification delivery, and sign-in startup require
  local verification.
