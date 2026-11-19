var builder = WebApplication.CreateBuilder(args);
var path = builder.Configuration["DATA_FILE"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "supply-chain.json");
builder.Services.AddSingleton(new JsonStateStore<SupplyState>(path));
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
var transitions = new Dictionary<string, string[]>
{
    ["planned"] = ["picked_up", "delayed", "cancelled"],
    ["picked_up"] = ["in_transit", "delayed", "exception"],
    ["in_transit"] = ["out_for_delivery", "delayed", "exception"],
    ["delayed"] = ["in_transit", "out_for_delivery", "exception"],
    ["out_for_delivery"] = ["delivered", "exception"],
    ["exception"] = ["in_transit", "delayed", "delivered"],
    ["delivered"] = [],
    ["cancelled"] = []
};
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "supply-chain-api" }));
app.MapGet("/api/shipments", async (string? status, JsonStateStore<SupplyState> store, CancellationToken ct) =>
    await store.ReadAsync(state => state.Shipments
        .Where(item => string.IsNullOrWhiteSpace(status) || item.Status == status)
        .OrderBy(item => item.PlannedArrival).ToArray(), ct));
app.MapPost("/api/shipments", async (CreateShipment request, JsonStateStore<SupplyState> store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.OrderReference) || string.IsNullOrWhiteSpace(request.Carrier) ||
        string.IsNullOrWhiteSpace(request.Origin) || string.IsNullOrWhiteSpace(request.Destination) ||
        request.PlannedArrival.Offset != TimeSpan.Zero || request.PlannedArrival <= DateTimeOffset.UtcNow)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["shipment"] = ["Order, carrier, route and a future UTC ETA are required."] });
    var now = DateTimeOffset.UtcNow;
    var shipment = new Shipment(Guid.NewGuid(), request.OrderReference.Trim(), request.Carrier.Trim(),
        request.Origin.Trim(), request.Destination.Trim(), request.PlannedArrival, "planned", 1, now,
        [new ShipmentEvent("planned", request.Origin.Trim(), "Shipment created", now)]);
    await store.UpdateAsync(state => { state.Shipments.Add(shipment); return true; }, ct);
    return Results.Created($"/api/shipments/{shipment.Id}", shipment);
});
app.MapPost("/api/shipments/{id:guid}/events", async (
    Guid id, AddShipmentEvent request, JsonStateStore<SupplyState> store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Location) || string.IsNullOrWhiteSpace(request.Status) ||
        request.OccurredAt.Offset != TimeSpan.Zero || request.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
        return Results.BadRequest(new { error = "Location, status and a valid UTC event time are required." });
    var result = await store.UpdateAsync(state =>
    {
        var index = state.Shipments.FindIndex(item => item.Id == id);
        if (index < 0) return (Shipment: (Shipment?)null, Error: "Shipment not found.");
        var shipment = state.Shipments[index];
        if (shipment.Version != request.ExpectedVersion) return (Shipment: shipment, Error: "Version conflict; reload before retrying.");
        if (!transitions.TryGetValue(shipment.Status, out var next) || !next.Contains(request.Status))
            return (Shipment: shipment, Error: "Shipment status transition is not allowed.");
        var updated = shipment with { Status = request.Status, Version = shipment.Version + 1,
            Events = [.. shipment.Events, new ShipmentEvent(request.Status, request.Location.Trim(),
                request.Note?.Trim() ?? "", request.OccurredAt)] };
        state.Shipments[index] = updated;
        return (Shipment: (Shipment?)updated, Error: (string?)null);
    }, ct);
    if (result.Shipment is null) return Results.NotFound(new { error = result.Error });
    if (result.Error is not null) return Results.Conflict(new { error = result.Error, shipment = result.Shipment });
    return Results.Ok(result.Shipment);
});
app.MapGet("/api/eta-scores", async (JsonStateStore<SupplyState> store, CancellationToken ct) =>
    await store.ReadAsync(state => state.EtaScores.Values.OrderByDescending(score => score.EvaluatedAt).ToArray(), ct));
app.MapPost("/api/eta-scores", async (List<EtaScore> scores, JsonStateStore<SupplyState> store, CancellationToken ct) =>
{
    if (scores.Any(score => score.RiskScore is < 0 or > 100 || score.EstimatedDelayMinutes < 0 ||
        score.Confidence is < 0 or > 100 || !double.IsFinite(score.Confidence)))
        return Results.BadRequest(new { error = "ETA score fields are outside their allowed range." });
    var saved = await store.UpdateAsync(state =>
    {
        var knownIds = state.Shipments.Select(shipment => shipment.Id.ToString()).ToHashSet();
        var accepted = scores.Where(score => knownIds.Contains(score.ShipmentId)).ToArray();
        foreach (var score in accepted) state.EtaScores[score.ShipmentId] = score;
        return accepted.Length;
    }, ct);
    return Results.Accepted("/api/eta-scores", new { saved });
});
app.MapFallbackToFile("index.html");
app.Run();

public sealed class SupplyState
{
    public List<Shipment> Shipments { get; set; } = [];
    public Dictionary<string, EtaScore> EtaScores { get; set; } = [];
}
public sealed record CreateShipment(string OrderReference, string Carrier, string Origin, string Destination, DateTimeOffset PlannedArrival);
public sealed record AddShipmentEvent(string Status, string Location, string? Note, DateTimeOffset OccurredAt, int ExpectedVersion);
public sealed record Shipment(Guid Id, string OrderReference, string Carrier, string Origin, string Destination,
    DateTimeOffset PlannedArrival, string Status, int Version, DateTimeOffset CreatedAt, List<ShipmentEvent> Events);
public sealed record ShipmentEvent(string Status, string Location, string Note, DateTimeOffset OccurredAt);
public sealed record EtaScore(string ShipmentId, int RiskScore, int EstimatedDelayMinutes, double Confidence,
    string[] Reasons, DateTimeOffset EvaluatedAt);