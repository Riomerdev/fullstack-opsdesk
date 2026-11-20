using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Operations")
    ?? throw new InvalidOperationException("ConnectionStrings:Operations is required.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddOpenApi();
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/health", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    await using var connection = await dataSource.OpenConnectionAsync(ct);
    await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: ct));
    return Results.Ok(new { status = "ok", database = "connected" });
});

app.MapGet("/api/orders", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    await using var connection = await dataSource.OpenConnectionAsync(ct);
    var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition("""
        SELECT id AS "Id", reference AS "Reference", supplier AS "Supplier", region AS "Region",
               status AS "Status", due_at AS "DueAt", created_at AS "CreatedAt"
        FROM orders ORDER BY due_at, reference LIMIT 200
        """, cancellationToken: ct));
    return Results.Ok(rows);
});

app.MapPost("/api/orders", async Task<IResult> (CreateOrderRequest request, NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    var reference = request.Reference?.Trim().ToUpperInvariant() ?? "";
    var supplier = request.Supplier?.Trim() ?? "";
    var region = request.Region?.Trim().ToUpperInvariant() ?? "";
    if (reference.Length is < 3 or > 40 || supplier.Length is < 2 or > 120
        || region is not ("BR-SC" or "US-EAST" or "EU-WEST")
        || request.DueAt.Offset != TimeSpan.Zero)
        return Results.ValidationProblem(new Dictionary<string, string[]> {
            ["order"] = ["Referencia, proveedor, región o fecha UTC inválidos."]
        });

    await using var connection = await dataSource.OpenConnectionAsync(ct);
    try
    {
        var row = await connection.QuerySingleAsync<OrderRow>(new CommandDefinition("""
            INSERT INTO orders (reference, supplier, region, status, due_at)
            VALUES (@Reference, @Supplier, @Region, 'OPEN', @DueAt)
            RETURNING id AS "Id", reference AS "Reference", supplier AS "Supplier",
                      region AS "Region", status AS "Status", due_at AS "DueAt", created_at AS "CreatedAt"
            """, new { Reference = reference, Supplier = supplier, Region = region, request.DueAt },
            cancellationToken: ct));
        return Results.Created("/api/orders/" + row.Id, row);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { error = "La referencia ya existe." });
    }
});

app.MapGet("/api/metrics/latest", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    await using var connection = await dataSource.OpenConnectionAsync(ct);
    var row = await connection.QuerySingleOrDefaultAsync<MetricsRow>(new CommandDefinition("""
        SELECT calculated_at AS "CalculatedAt", open_orders AS "OpenOrders",
               late_orders AS "LateOrders", due_soon_orders AS "DueSoonOrders",
               healthy_orders AS "HealthyOrders"
        FROM metric_snapshots ORDER BY calculated_at DESC LIMIT 1
        """, cancellationToken: ct));
    if (row is null) return Results.NoContent();
    return Results.Ok(row);
});

app.MapPost("/internal/metrics", async Task<IResult> (MetricsRequest request, HttpRequest http,
    IConfiguration configuration, NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    var expected = configuration["METRICS_WRITE_TOKEN"] ?? "";
    var supplied = http.Headers["Authorization"].ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    if (expected.Length < 16 || supplied.Length != expected.Length
        || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        return Results.Unauthorized();
    if (request.OpenOrders < 0 || request.LateOrders < 0 || request.DueSoonOrders < 0
        || request.HealthyOrders < 0 || request.LateOrders + request.HealthyOrders != request.OpenOrders)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["metrics"] = ["Los contadores no son coherentes."] });

    await using var connection = await dataSource.OpenConnectionAsync(ct);
    await connection.ExecuteAsync(new CommandDefinition("""
        INSERT INTO metric_snapshots (open_orders, late_orders, due_soon_orders, healthy_orders)
        VALUES (@OpenOrders, @LateOrders, @DueSoonOrders, @HealthyOrders)
        """, request, cancellationToken: ct));
    return Results.Accepted("/api/metrics/latest");
});

app.Run();

public sealed record CreateOrderRequest(string? Reference, string? Supplier, string? Region, DateTimeOffset DueAt);
public sealed record OrderRow(Guid Id, string Reference, string Supplier, string Region, string Status,
    DateTimeOffset DueAt, DateTimeOffset CreatedAt);
public sealed record MetricsRequest(int OpenOrders, int LateOrders, int DueSoonOrders, int HealthyOrders);
public sealed record MetricsRow(DateTimeOffset CalculatedAt, int OpenOrders, int LateOrders,
    int DueSoonOrders, int HealthyOrders);
