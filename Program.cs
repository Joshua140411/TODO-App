using System.Net;
using System.Text.Json.Serialization;
using FocusFlow.Core;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
// Safe default: local single-user app, not an authenticated network service.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://localhost:5080");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 1_048_576);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_048_576);
var configuredDirectory = builder.Configuration["TodoData:Directory"];
var dataDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusFlow")
    : Path.GetFullPath(configuredDirectory);
builder.Services.AddSingleton(new JsonStore(dataDirectory));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TodoService>();
var app = builder.Build();
app.Logger.LogInformation("FocusFlow data directory: {Directory}", dataDirectory);
var recovery = app.Services.GetRequiredService<JsonStore>().RecoveryNotice;
if (recovery is not null) app.Logger.LogWarning("{Recovery}", recovery);

app.Use(async (context, next) =>
{
    // Host validation also prevents DNS rebinding against this unauthenticated local app.
    var host = context.Request.Host.Host;
    if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        && !(IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)))
    {
        context.Response.StatusCode = 403; await context.Response.WriteAsync("Nur lokaler Zugriff ist erlaubt."); return;
    }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            // Custom header + no CORS: remote websites cannot trigger writes from a browser.
            var origin = context.Request.Headers.Origin.ToString();
            var expectedOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
            if (context.Request.Headers["X-FocusFlow"].ToString() != "1"
                || (!string.IsNullOrEmpty(origin) && !string.Equals(origin, expectedOrigin, StringComparison.OrdinalIgnoreCase)))
            {
                await Results.Problem(statusCode: 403, title: "Schreibzugriff abgewiesen.").ExecuteAsync(context); return;
            }
        }
    }
    try { await next(context); }
    catch (ValidationException ex) { await Results.Problem(statusCode: 400, title: ex.Message).ExecuteAsync(context); }
    catch (MissingException ex) { await Results.Problem(statusCode: 404, title: ex.Message).ExecuteAsync(context); }
    catch (ConflictException ex) { await Results.Problem(statusCode: 409, title: ex.Message).ExecuteAsync(context); }
    catch (BadHttpRequestException ex) { await Results.Problem(statusCode: ex.StatusCode, title: "Ungültige Anfrage oder ungültige Felder.").ExecuteAsync(context); }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Request failed");
        await Results.Problem(statusCode: 500, title: "Speichern oder Laden fehlgeschlagen. Bitte Serverprotokoll prüfen.").ExecuteAsync(context);
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
static long Version(HttpRequest request)
{
    if (!long.TryParse(request.Headers["X-Data-Version"], out var version) || version < 0)
        throw new BadHttpRequestException("Datenversion fehlt.", 428);
    return version;
}
static object State(TodoService service, JsonStore store) => new
{
    data = service.Read(), serverNow = service.Now, recoveryNotice = store.RecoveryNotice
};
app.MapGet("/api/state", (TodoService s, JsonStore store) => State(s, store));
app.MapGet("/api/statistics", (TodoService s) => Statistics.Build(s.Read(), s.Now));
app.MapGet("/api/export", (TodoService s) => Results.File(
    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(s.Read(), JsonDefaults.Options),
    "application/json", $"focusflow-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json"));
app.MapPost("/api/todos", (TodoInput input, TodoService s, HttpRequest r) => s.CreateTodo(input, Version(r)));
app.MapPut("/api/todos/{id:guid}", (Guid id, TodoInput input, TodoService s, HttpRequest r) => s.UpdateTodo(id, input, Version(r)));
app.MapPatch("/api/todos/{id:guid}/status", (Guid id, StatusInput input, TodoService s, HttpRequest r) => s.ChangeStatus(id, input.Status, Version(r)));
app.MapDelete("/api/todos/{id:guid}", (Guid id, TodoService s, HttpRequest r) => s.DeleteTodo(id, Version(r)));
app.MapPost("/api/todos/{id:guid}/timer/start", (Guid id, TodoService s, HttpRequest r) => s.StartTimer(id, Version(r)));
app.MapPost("/api/todos/{id:guid}/timer/stop", (Guid id, TodoService s, HttpRequest r) => s.StopTimer(id, Version(r)));
app.MapPost("/api/todos/{id:guid}/time", (Guid id, ManualTimeInput input, TodoService s, HttpRequest r) => s.AddTime(id, input, Version(r)));
app.MapDelete("/api/time/{id:guid}", (Guid id, TodoService s, HttpRequest r) => s.DeleteTime(id, Version(r)));
app.MapPost("/api/categories", (CategoryInput input, TodoService s, HttpRequest r) => s.CreateCategory(input, Version(r)));
app.MapPut("/api/categories/{id:guid}", (Guid id, CategoryInput input, TodoService s, HttpRequest r) => s.UpdateCategory(id, input, Version(r)));
app.MapDelete("/api/categories/{id:guid}", (Guid id, TodoService s, HttpRequest r) => s.DeleteCategory(id, Version(r)));
app.Run();
public sealed record StatusInput(TodoStatus Status);
