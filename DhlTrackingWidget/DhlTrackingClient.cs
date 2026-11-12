using System.Net.Http.Headers;
using System.Text.Json;

namespace DhlTrackingWidget;

internal sealed record ShipmentStatus(string Description, string? Timestamp, string? Location);

internal sealed class DhlTrackingClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<ShipmentStatus> GetStatusAsync(
        string trackingNumber,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"https://api-eu.dhl.com/track/shipments?trackingNumber={Uri.EscapeDataString(trackingNumber)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("DHL-API-Key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var shipment = document.RootElement
            .GetProperty("shipments")
            .EnumerateArray()
            .FirstOrDefault();

        if (shipment.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("DHL did not return a shipment for this tracking number.");
        }

        var status = shipment.TryGetProperty("status", out var statusElement)
            ? statusElement
            : default;
        var description = GetString(status, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            description = "Status unavailable";
        }

        var timestamp = GetString(status, "timestamp");
        var location = status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty("location", out var locationElement)
            && locationElement.TryGetProperty("address", out var addressElement)
            ? GetString(addressElement, "addressLocality")
            : null;

        return new ShipmentStatus(description, timestamp, location);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
