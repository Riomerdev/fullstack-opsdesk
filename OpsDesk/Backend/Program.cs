var builder = WebApplication.CreateBuilder(args);
var path = builder.Configuration["DATA_FILE"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "opsdesk.json");
builder.Services.AddSingleton(new JsonStateStore<OpsDeskState>(path));
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

var transitions = new Dictionary<string, string[]>
{
    ["new"] = ["in_progress"],
    ["in_progress"] = ["resolved"],
    ["resolved"] = ["new"]
};
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "opsdesk-api" }));
app.MapGet("/api/tickets", async (string? status, string? priority, JsonStateStore<OpsDeskState> store, CancellationToken ct) =>
    await store.ReadAsync(state => state.Tickets
        .Where(ticket => string.IsNullOrWhiteSpace(status) || ticket.Status == status)
        .Where(ticket => string.IsNullOrWhiteSpace(priority) || ticket.Priority == priority)
        .OrderBy(ticket => ticket.DueAt).ToArray(), ct));
app.MapPost("/api/tickets", async (CreateTicket request, JsonStateStore<OpsDeskState> store, CancellationToken ct) =>
{
    var priority = request.Priority?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Requester) ||
        string.IsNullOrWhiteSpace(request.Category) || priority is not ("critical" or "high" or "normal"))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["ticket"] = ["Title, requester, category and a valid priority are required."] });
    var now = DateTimeOffset.UtcNow;
    var slaMinutes = priority switch { "critical" => 60, "high" => 240, _ => 1440 };
    var ticket = new Ticket(Guid.NewGuid(), request.Title.Trim(), request.Requester.Trim(), request.Category.Trim(),
        priority, "new", 1, now, now.AddMinutes(slaMinutes), [new TicketEvent("new", now)]);
    await store.UpdateAsync(state => { state.Tickets.Add(ticket); return true; }, ct);
    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
});
app.MapPost("/api/tickets/{id:guid}/status", async (
    Guid id, ChangeTicketStatus request, JsonStateStore<OpsDeskState> store, CancellationToken ct) =>
{
    var result = await store.UpdateAsync(state =>
    {
        var index = state.Tickets.FindIndex(item => item.Id == id);
        if (index < 0) return (Ticket: (Ticket?)null, Error: "Ticket not found.");
        var ticket = state.Tickets[index];
        if (ticket.Version != request.ExpectedVersion) return (Ticket: ticket, Error: "Version conflict; refresh and retry.");
        if (!transitions.TryGetValue(ticket.Status, out var next) || !next.Contains(request.Status))
            return (Ticket: ticket, Error: "Status transition is not allowed.");
        state.Tickets[index] = ticket with { Status = request.Status, Version = ticket.Version + 1,
            Events = [.. ticket.Events, new TicketEvent(request.Status, DateTimeOffset.UtcNow)] };
        return (Ticket: (Ticket?)state.Tickets[index], Error: (string?)null);
    }, ct);
    if (result.Ticket is null) return Results.NotFound(new { error = result.Error });
    if (result.Error is not null) return Results.Conflict(new { error = result.Error, ticket = result.Ticket });
    return Results.Ok(result.Ticket);
});
app.MapGet("/api/metrics/latest", async (JsonStateStore<OpsDeskState> store, CancellationToken ct) =>
    await store.ReadAsync(state => state.Metrics.LastOrDefault(), ct));
app.MapPost("/api/metrics", async (SlaSnapshot metric, JsonStateStore<OpsDeskState> store, CancellationToken ct) =>
{
    if (metric.Open < 0 || metric.Overdue < 0 || metric.Overdue > metric.Open)
        return Results.BadRequest(new { error = "Metric counts are inconsistent." });
    await store.UpdateAsync(state => { state.Metrics.Add(metric); if (state.Metrics.Count > 100) state.Metrics.RemoveAt(0); return true; }, ct);
    return Results.Accepted("/api/metrics/latest", metric);
});
app.MapFallbackToFile("index.html");
app.Run();

public sealed class OpsDeskState
{
    public List<Ticket> Tickets { get; set; } = [];
    public List<SlaSnapshot> Metrics { get; set; } = [];
}
public sealed record CreateTicket(string Title, string Requester, string Category, string Priority);
public sealed record ChangeTicketStatus(string Status, int ExpectedVersion);
public sealed record Ticket(Guid Id, string Title, string Requester, string Category, string Priority, string Status,
    int Version, DateTimeOffset CreatedAt, DateTimeOffset DueAt, List<TicketEvent> Events);
public sealed record TicketEvent(string Status, DateTimeOffset At);
public sealed record SlaSnapshot(DateTimeOffset GeneratedAt, int Open, int Overdue, Dictionary<string, int> ByPriority);