# Changelog

## Unreleased

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
