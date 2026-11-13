using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ShipmentTracker.Core;

public sealed class DhlTrackingClient(HttpClient http, TimeProvider? timeProvider = null) : ITrackingClient
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<TrackingUpdate> FetchAsync(string trackingNumber, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api-eu.dhl.com/track/shipments?trackingNumber=" + Uri.EscapeDataString(trackingNumber) + "&language=en");
        request.Headers.Add("DHL-API-Key", apiKey);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TrackingException("DHL denied access. Check your key, activation and approved entitlement in Settings.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new TrackingException("Shipment not found. Check the number or open DHL's website.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var until = retry?.Date ?? clock.GetUtcNow().Add(retry?.Delta ?? TimeSpan.FromHours(24));
            throw new TrackingException("DHL throttled requests. Automatic checks are paused until " + until.ToLocalTime().ToString("g"), until);
        }
        if (!response.IsSuccessStatusCode)
            throw new TrackingException($"DHL returned HTTP {(int)response.StatusCode}. Try again at the next permitted check.");
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return Parse(document.RootElement);
        }
        catch (JsonException)
        {
            throw new TrackingException("DHL returned an unreadable tracking response.");
        }
    }

    public static TrackingUpdate Parse(JsonElement root)
    {
        var shipments = Property(root, "shipments");
        if (shipments.ValueKind != JsonValueKind.Array || shipments.GetArrayLength() == 0)
            throw new TrackingException("DHL returned no matching shipments.");
        if (shipments.GetArrayLength() != 1 || Text(root, "nextUrl").Length > 0)
            throw new TrackingException("This number has multiple matches. Open DHL's website to identify the correct shipment.");
        var shipment = shipments[0];
        var status = Property(shipment, "status");
        var code = Text(status, "statusCode");
        if (code.Length == 0)
            throw new TrackingException("DHL returned no shipment status. Open DHL's website for details.");
        var title = Text(status, "status");
        if (title.Length == 0) title = Text(status, "description");
        if (title.Length == 0) title = code;
        var eta = Text(shipment, "estimatedTimeOfDelivery");
        if (eta.Length == 0)
        {
            var frame = Property(shipment, "estimatedDeliveryTimeFrame");
            eta = string.Join(" - ", new[] { Text(frame, "estimatedFrom"), Text(frame, "estimatedThrough") }.Where(s => s.Length > 0));
        }
        if (eta.Length == 0) eta = Text(shipment, "estimatedTimeOfDeliveryRemark");
        // The current status carries its own event and location; do not assume event-array ordering.
        var address = Property(Property(status, "location"), "address");
        var location = string.Join(", ", new[] { Text(address, "addressLocality"), Text(address, "countryCode") }.Where(s => s.Length > 0));
        var eventTime = Text(status, "timestamp");
        DateTimeOffset? delivered = null;
        if (code.Equals("delivered", StringComparison.OrdinalIgnoreCase) && HasExplicitOffset(eventTime) &&
            DateTimeOffset.TryParse(eventTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            delivered = timestamp;
        else if (code.Equals("delivered", StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParse(eventTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            // An unzoned value could be as early as UTC+14. Use that bound only for retention, not display.
            delivered = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(14));
        return new(code, title, eta, Text(status, "description"), location, eventTime, delivered);
    }

    private static bool HasExplicitOffset(string text) =>
        text.EndsWith('Z') || (text.Length > 10 && (text.LastIndexOf('+') > 10 || text.LastIndexOf('-') > 10));

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string Text(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
