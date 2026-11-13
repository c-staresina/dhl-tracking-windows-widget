using System.Net;
using System.Text.Json;
using ShipmentTracker.Core;
using Xunit;

namespace ShipmentTracker.Tests;

public sealed class TrackingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 144)]
    [InlineData(25, 144)]
    [InlineData(26, 150)]
    [InlineData(50, 288)]
    public void IntervalRespectsShipmentAndAccountBudgets(int count, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), RefreshPolicy.Interval(count));

    [Fact]
    public void BothLedgersAndGlobalSpacingApply()
    {
        var shipment = new Shipment { TrackingNumber = "SYNTHETIC" };
        var state = new TrackerState { Shipments = [shipment] };
        state.Attempts.Add(new(shipment.BudgetId, Now));
        Assert.Equal(Now.AddMinutes(144), RefreshPolicy.NextAllowed(state, shipment, Now));
        var other = new Shipment { TrackingNumber = "OTHER" };
        Assert.Equal(Now.AddSeconds(5), RefreshPolicy.NextAllowed(state, other, Now));
        state.Attempts = Enumerable.Range(0, 10).Select(i => new RequestAttempt(shipment.BudgetId, Now.AddMinutes(-20 + i))).ToList();
        Assert.True(RefreshPolicy.NextAllowed(state, shipment, Now) > Now.AddHours(23));
        state.Attempts = Enumerable.Range(0, 250).Select(i => new RequestAttempt("OTHER", Now.AddMinutes(-10))).ToList();
        Assert.Equal(Now.AddMinutes(-10).AddHours(24).AddSeconds(1), RefreshPolicy.NextAllowed(state, shipment, Now));
    }

    [Fact]
    public async Task ConcurrentChecksAndRestoredLedgerCannotDuplicateRequests()
    {
        var state = ReadyState();
        var store = new MemoryStore();
        var client = new FakeClient();
        var engine = new TrackerEngine(state, store, client, new FixedClock());
        await Task.WhenAll(engine.CheckNextAsync(), engine.CheckNextAsync());
        Assert.Equal(1, client.Calls);
        Assert.NotNull(store.Saved);
        var restored = new TrackerEngine(store.Saved!, store, client, new FixedClock());
        await restored.CheckNextAsync();
        Assert.Equal(1, client.Calls);
        Assert.Single((await restored.SnapshotAsync()).Attempts);
    }

    [Fact]
    public async Task FailedAttemptAndRemovalDoNotRefundQuota()
    {
        var store = new MemoryStore();
        var client = new FakeClient { Failure = new HttpRequestException("synthetic network failure") };
        var engine = new TrackerEngine(ReadyState(), store, client, new FixedClock());
        await engine.CheckNextAsync();
        Assert.Contains("Network", (await engine.SnapshotAsync()).Shipments[0].Error);
        await engine.ChangeAsync(s =>
        {
            s.Shipments.Clear();
            s.Shipments.Add(new() { TrackingNumber = "SYNTHETIC" });
        });
        await engine.CheckNextAsync();
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task QuotaIsCommittedBeforeCallingNetworkAndStorageFailureStopsDispatch()
    {
        var client = new FakeClient();
        var store = new MemoryStore { Fail = true };
        var engine = new TrackerEngine(ReadyState(), store, client, new FixedClock());
        await Assert.ThrowsAsync<IOException>(() => engine.CheckNextAsync());
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task DeliveryNotificationRequiresBaselineAndIsNotRepeatedAfterRestart()
    {
        var state = ReadyState();
        state.Shipments[0].HasBaseline = true;
        var client = new FakeClient { Update = Delivered() };
        var store = new MemoryStore();
        var engine = new TrackerEngine(state, store, client, new FixedClock());
        Assert.NotNull(await engine.CheckNextAsync());
        var restored = new TrackerEngine(store.Saved!, store, client, new FixedClock());
        Assert.Null(await restored.CheckNextAsync());
        Assert.Equal(1, client.Calls);
        var imported = new TrackerEngine(ReadyState(), new MemoryStore(), new FakeClient { Update = Delivered() }, new FixedClock());
        Assert.Null(await imported.CheckNextAsync());
    }

    [Fact]
    public async Task ExpiredDataIsRemovedBeforeSnapshotButBudgetIsRetained()
    {
        var state = ReadyState();
        state.Shipments[0].StatusCode = "delivered";
        state.Shipments[0].DeliveredAt = Now.AddDays(-30);
        state.Attempts.Add(new(state.Shipments[0].BudgetId, Now.AddHours(-1)));
        var store = new MemoryStore();
        var engine = new TrackerEngine(state, store, new FakeClient(), new FixedClock());
        var snapshot = await engine.SnapshotAsync();
        Assert.Empty(snapshot.Shipments);
        Assert.Single(snapshot.Attempts);
        Assert.Empty(store.Saved!.Shipments);
    }

    [Fact]
    public void OptionalFieldsAndFutureStatusesAreNotInvented()
    {
        using var document = JsonDocument.Parse("""{"shipments":[{"status":{"statusCode":"future-status","timestamp":"2026-11-13"}}]}""");
        var update = DhlTrackingClient.Parse(document.RootElement);
        Assert.Equal("future-status", update.Status);
        Assert.Equal("", update.EstimatedDelivery);
        Assert.Equal("2026-11-13", update.EventTime);
        Assert.Null(update.DeliveredAt);
    }

    [Theory]
    [InlineData("""{"shipments":[]}""")]
    [InlineData("""{"shipments":[{},{}]}""")]
    [InlineData("""{"shipments":[{"status":{"statusCode":"transit"}}],"nextUrl":"/next"}""")]
    [InlineData("""{"shipments":[{}]}""")]
    public void AmbiguousOrIncompleteResponsesAreExplicitErrors(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<TrackingException>(() => DhlTrackingClient.Parse(document.RootElement));
    }

    [Fact]
    public void UnzonedDeliveryIsPreservedForDisplayWithConservativeRetentionBound()
    {
        using var document = JsonDocument.Parse("""{"shipments":[{"status":{"statusCode":"delivered","timestamp":"2026-11-13T10:00:00"}}]}""");
        var update = DhlTrackingClient.Parse(document.RootElement);
        Assert.Equal("2026-11-13T10:00:00", update.EventTime);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.FromHours(14)), update.DeliveredAt);
    }

    [Fact]
    public async Task ThrottlingPausesAllShipmentsAndDoesNotTriggerImmediateRetry()
    {
        var state = ReadyState();
        state.Shipments.Add(new() { TrackingNumber = "SECOND" });
        var client = new FakeClient { Failure = new TrackingException("throttled", Now.AddHours(2)) };
        var engine = new TrackerEngine(state, new MemoryStore(), client, new FixedClock());
        await engine.CheckNextAsync();
        await engine.CheckNextAsync();
        Assert.Equal(1, client.Calls);
        Assert.Equal(Now.AddHours(2), (await engine.SnapshotAsync()).BlockedUntil);
    }

    [Theory]
    [InlineData(401, "denied")]
    [InlineData(403, "denied")]
    [InlineData(404, "not found")]
    [InlineData(503, "HTTP 503")]
    public async Task HttpErrorsDoNotBecomeSuccessfulEmptyStatuses(int code, string message)
    {
        using var http = new HttpClient(new StubHandler(new HttpResponseMessage((HttpStatusCode)code)));
        var client = new DhlTrackingClient(http, new FixedClock());
        var exception = await Assert.ThrowsAsync<TrackingException>(() => client.FetchAsync("SYNTHETIC", "synthetic-key", default));
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public async Task Http429HonorsRetryAfter()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new(TimeSpan.FromHours(6));
        using var http = new HttpClient(new StubHandler(response));
        var exception = await Assert.ThrowsAsync<TrackingException>(() =>
            new DhlTrackingClient(http, new FixedClock()).FetchAsync("SYNTHETIC", "synthetic-key", default));
        Assert.Equal(Now.AddHours(6), exception.RetryAfter);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(26, 250)]
    [InlineData(50, 250)]
    public async Task WholeDayScheduleMeetsExactBudgetsAndSpacing(int shipments, int expectedCalls)
    {
        var state = ReadyState();
        state.Shipments = Enumerable.Range(0, shipments)
            .Select(i => new Shipment { TrackingNumber = "SYNTHETIC-" + i }).ToList();
        var clock = new MovingClock { Now = Now };
        var store = new MemoryStore();
        var client = new FakeClient();
        var engine = new TrackerEngine(state, store, client, clock);
        var audit = new List<RequestAttempt>();
        while (clock.Now < Now.AddHours(24))
        {
            await engine.CheckNextAsync();
            var snapshot = await engine.SnapshotAsync();
            if (snapshot.Attempts.Count > audit.Count) audit.Add(snapshot.Attempts[^1]);
            var next = snapshot.Shipments.Min(s => RefreshPolicy.NextAllowed(snapshot, s, clock.Now));
            Assert.True(next > clock.Now);
            clock.Now = next;
        }
        Assert.Equal(expectedCalls, client.Calls);
        Assert.True(audit.Count <= 250);
        foreach (var group in audit.GroupBy(a => a.ShipmentId))
        {
            Assert.True(group.Count() <= 10);
            var checks = group.ToArray();
            for (var i = 1; i < checks.Length; i++)
                Assert.True(checks[i].At - checks[i - 1].At >= RefreshPolicy.Interval(shipments));
        }
        for (var i = 1; i < audit.Count; i++)
            Assert.True(audit[i].At - audit[i - 1].At >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisabledAuthorizationNeverContactsDhl()
    {
        var state = ReadyState();
        state.AuthorizedAccess = false;
        var client = new FakeClient();
        var engine = new TrackerEngine(state, new MemoryStore(), client, new FixedClock());
        await engine.CheckNextAsync();
        Assert.Equal(0, client.Calls);
        Assert.Empty((await engine.SnapshotAsync()).Attempts);
    }

    private static TrackerState ReadyState() => new()
    {
        ApiKey = "synthetic-key", AuthorizedAccess = true,
        Shipments = [new() { TrackingNumber = "SYNTHETIC" }]
    };

    private static TrackingUpdate Delivered() => new("delivered", "Delivered", "", "", "", Now.ToString("O"), Now);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MovingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeClient : ITrackingClient
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public TrackingUpdate Update { get; init; } = new("transit", "In transit", "", "", "", "", null);
        public Task<TrackingUpdate> FetchAsync(string trackingNumber, string apiKey, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is null ? Task.FromResult(Update) : Task.FromException<TrackingUpdate>(Failure);
        }
    }

    private sealed class MemoryStore : ITrackerStore
    {
        public TrackerState? Saved { get; private set; }
        public bool Fail { get; init; }
        public Task SaveAsync(TrackerState state, CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("Synthetic storage failure");
            Saved = JsonSerializer.Deserialize<TrackerState>(JsonSerializer.Serialize(state));
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
