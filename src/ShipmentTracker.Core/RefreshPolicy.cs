namespace ShipmentTracker.Core;

public static class RefreshPolicy
{
    public static TimeSpan Interval(int activeShipments) =>
        TimeSpan.FromMinutes(Math.Max(144, Math.Ceiling(1440d * Math.Max(1, activeShipments) / 250)));

    public static DateTimeOffset NextAllowed(TrackerState state, Shipment shipment, DateTimeOffset now)
    {
        var next = now;
        var recent = state.Attempts.Where(a => a.At > now.AddHours(-24)).OrderBy(a => a.At).ToList();
        var own = recent.Where(a => a.ShipmentId == shipment.BudgetId).ToList();
        var active = state.Shipments.Count(s => !s.IsDelivered);
        if (own.Count > 0)
            next = Max(next, own[^1].At + Interval(active));
        if (own.Count >= 10)
            next = Max(next, own[own.Count - 10].At.AddHours(24).AddSeconds(1));
        if (recent.Count >= 250)
            next = Max(next, recent[recent.Count - 250].At.AddHours(24).AddSeconds(1));
        if (state.Attempts.Count > 0)
            next = Max(next, state.Attempts.Max(a => a.At).AddSeconds(5));
        if (state.BlockedUntil is { } blocked)
            next = Max(next, blocked);
        return next;
    }

    public static bool PurgeExpired(TrackerState state, DateTimeOffset now)
    {
        var removed = state.Shipments.RemoveAll(s => s.DeliveredAt is { } delivered && delivered.AddDays(30) <= now);
        var attempts = state.Attempts.RemoveAll(a => a.At <= now.AddHours(-24));
        return removed > 0 || attempts > 0;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
