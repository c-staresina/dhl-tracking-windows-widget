using System.Security.Cryptography;
using System.Text;

namespace ShipmentTracker.Core;

public sealed class TrackerState
{
    public int Version { get; set; } = 1;
    public string ApiKey { get; set; } = "";
    public bool AuthorizedAccess { get; set; }
    public bool Notifications { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool StartAtSignIn { get; set; }
    public string Hotkey { get; set; } = "None";
    public List<Shipment> Shipments { get; set; } = [];
    public List<RequestAttempt> Attempts { get; set; } = [];
    public DateTimeOffset? BlockedUntil { get; set; }
    public string ServiceMessage { get; set; } = "";
}

public sealed class Shipment
{
    public string TrackingNumber { get; set; } = "";
    public string Label { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string Status { get; set; } = "Not checked";
    public string EstimatedDelivery { get; set; } = "";
    public string LatestEvent { get; set; } = "";
    public string Location { get; set; } = "";
    public string EventTime { get; set; } = "";
    public DateTimeOffset? LastSuccess { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string Error { get; set; } = "";
    public bool HasBaseline { get; set; }
    public bool IsDelivered => StatusCode.Equals("delivered", StringComparison.OrdinalIgnoreCase);
    public string BudgetId => TrackingIdentity(TrackingNumber);

    public static string TrackingIdentity(string number) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(number.Trim().ToUpperInvariant())));
}

public sealed record RequestAttempt(string ShipmentId, DateTimeOffset At);

public sealed record TrackingUpdate(
    string StatusCode, string Status, string EstimatedDelivery,
    string LatestEvent, string Location, string EventTime, DateTimeOffset? DeliveredAt);

public sealed class TrackingException(string message, DateTimeOffset? retryAfter = null) : Exception(message)
{
    public DateTimeOffset? RetryAfter { get; } = retryAfter;
}

public interface ITrackerStore
{
    Task SaveAsync(TrackerState state, CancellationToken cancellationToken);
}

public interface ITrackingClient
{
    Task<TrackingUpdate> FetchAsync(string trackingNumber, string apiKey, CancellationToken cancellationToken);
}
