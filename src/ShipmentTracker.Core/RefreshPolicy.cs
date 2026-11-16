namespace ShipmentTracker.Core;

public static class RefreshPolicy
{
    public static TimeSpan Interval(int activeShipments) =>
        TimeSpan.FromMinutes(Math.Max(150, Math.Ceiling(1440d * Math.Max(1, activeShipments) / 250)));

    public static DateTimeOffset NextAllowed(TrackerState state, Shipment shipment, DateTimeOffset now, TimeZoneInfo? timeZone = null)
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
        if (state.DailyCheckTime is { } preferred)
        {
            var configured = state.DailyScheduleChangedAt
                ?? throw new InvalidOperationException("The preferred daily schedule has no start time. Save it again in Settings.");
            var index = state.Shipments.Where(s => !s.IsDelivered).ToList().FindIndex(s => s.BudgetId == shipment.BudgetId);
            if (index < 0) throw new InvalidOperationException("Cannot schedule a shipment that is not active.");
            var stagger = TimeSpan.FromSeconds(index * 5d);
            var zone = timeZone ?? TimeZoneInfo.Local;
            var start = Max(configured, shipment.AddedAt ?? configured);
            var first = DailyAnchor(start, preferred, TimeSpan.Zero, zone) + stagger;
            next = Max(next, first);
            var anchor = DailyAnchor(next, preferred, stagger, zone);
            // Leave enough room before the next daily target; never squeeze in an extra check.
            if (anchor > next && anchor - next < Interval(active)) next = anchor;
        }
        return next;
    }

    private static DateTimeOffset DailyAnchor(DateTimeOffset earliest, TimeOnly time, TimeSpan stagger, TimeZoneInfo zone)
    {
        var date = TimeZoneInfo.ConvertTime(earliest - stagger, zone).Date;
        var anchor = LocalInstant(date + time.ToTimeSpan(), zone) + stagger;
        if (anchor < earliest) anchor = LocalInstant(date.AddDays(1) + time.ToTimeSpan(), zone) + stagger;
        return anchor;
    }

    private static DateTimeOffset LocalInstant(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A missing local time moves forward to the first valid minute; a repeated time runs only once.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local))
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Min());
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    public static bool PurgeExpired(TrackerState state, DateTimeOffset now)
    {
        var removed = state.Shipments.RemoveAll(s => s.DeliveredAt is { } delivered && delivered.AddDays(30) <= now);
        var attempts = state.Attempts.RemoveAll(a => a.At <= now.AddHours(-24));
        return removed > 0 || attempts > 0;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
