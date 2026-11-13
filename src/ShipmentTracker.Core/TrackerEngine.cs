using System.Text.Json;

namespace ShipmentTracker.Core;

public sealed class TrackerEngine(TrackerState state, ITrackerStore store, ITrackingClient client, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<TrackerState> SnapshotAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (RefreshPolicy.PurgeExpired(state, clock.GetUtcNow()))
                await store.SaveAsync(state, CancellationToken.None);
            return JsonSerializer.Deserialize<TrackerState>(JsonSerializer.Serialize(state))!;
        }
        finally { gate.Release(); }
    }

    public async Task ChangeAsync(Action<TrackerState> change)
    {
        await gate.WaitAsync();
        try
        {
            var before = JsonSerializer.Serialize(state);
            try
            {
                change(state);
                await store.SaveAsync(state, CancellationToken.None);
            }
            catch
            {
                state = JsonSerializer.Deserialize<TrackerState>(before)!;
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<string?> CheckNextAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            if (RefreshPolicy.PurgeExpired(state, now))
                await store.SaveAsync(state, cancellationToken);
            if (!state.AuthorizedAccess || string.IsNullOrWhiteSpace(state.ApiKey)) return null;
            var shipment = state.Shipments.Where(s => !s.IsDelivered)
                .OrderBy(s => state.Attempts.Where(a => a.ShipmentId == s.BudgetId).Select(a => a.At).DefaultIfEmpty(DateTimeOffset.MinValue).Max())
                .FirstOrDefault(s => RefreshPolicy.NextAllowed(state, s, now) <= now);
            if (shipment is null) return null;
            state.Attempts.Add(new(shipment.BudgetId, now));
            // Commit the attempt before sending so crashes and failures cannot refund quota.
            await store.SaveAsync(state, cancellationToken);
            try
            {
                var update = await client.FetchAsync(shipment.TrackingNumber, state.ApiKey, cancellationToken);
                var notify = shipment.HasBaseline && !shipment.IsDelivered &&
                    update.StatusCode.Equals("delivered", StringComparison.OrdinalIgnoreCase);
                shipment.StatusCode = update.StatusCode;
                shipment.Status = update.Status;
                shipment.EstimatedDelivery = update.EstimatedDelivery;
                shipment.LatestEvent = update.LatestEvent;
                shipment.Location = update.Location;
                shipment.EventTime = update.EventTime;
                shipment.LastSuccess = clock.GetUtcNow();
                if (shipment.IsDelivered)
                {
                    // Without a zoned delivery instant, purge conservatively rather than invent a time zone.
                    shipment.DeliveredAt = update.DeliveredAt ?? now.AddDays(-30);
                }
                shipment.HasBaseline = true;
                shipment.Error = "";
                state.ServiceMessage = "";
                RefreshPolicy.PurgeExpired(state, clock.GetUtcNow());
                if (!state.Shipments.Contains(shipment)) notify = false;
                await store.SaveAsync(state, CancellationToken.None);
                return notify && state.Notifications ? "A tracked shipment was delivered. Open Shipment Tracker for details." : null;
            }
            catch (TrackingException exception)
            {
                shipment.Error = exception.Message;
                state.ServiceMessage = exception.Message;
                if (exception.RetryAfter is { } until) state.BlockedUntil = until;
                await store.SaveAsync(state, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                shipment.Error = "Network connection failed. Cached data may be stale; the attempt counts toward quota.";
                await store.SaveAsync(state, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                shipment.Error = "The check timed out or was cancelled. The attempt counts toward quota.";
                await store.SaveAsync(state, CancellationToken.None);
            }
            return null;
        }
        finally { gate.Release(); }
    }
}
