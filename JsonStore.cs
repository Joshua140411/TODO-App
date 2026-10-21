using System.Text.Json;

namespace FocusFlow.Core;

/// <summary>Single-process repository. Mutations work on a clone, validate, then persist atomically.</summary>
public sealed class JsonStore : IDisposable
{
    private readonly object gate = new();
    private readonly string filePath;
    private readonly FileStream processLock;
    private AppData data;
    public string? RecoveryNotice { get; }

    public JsonStore(string directory)
    {
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "focusflow.json");
        // The OS releases the exclusive lock even after a crash. Two writers are not supported.
        processLock = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (!File.Exists(filePath)) data = new AppData();
            else
            {
                try { data = Load(filePath); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    if (!File.Exists(filePath + ".bak")) throw;
                    data = Load(filePath + ".bak");
                    // Keep the corrupt original for investigation; restore the last known valid version.
                    File.Copy(filePath, filePath + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    File.Copy(filePath + ".bak", filePath, true);
                    RecoveryNotice = "Die Datendatei war beschädigt. Der letzte Sicherungsstand wurde wiederhergestellt.";
                }
            }
        }
        catch { processLock.Dispose(); throw; }
    }

    private static AppData Load(string path)
    {
        var result = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), JsonDefaults.Options)
            ?? throw new InvalidDataException("Leere Datendatei.");
        if (result.SchemaVersion != 1)
            throw new NotSupportedException($"Datenschema {result.SchemaVersion} wird nicht unterstützt. Keine Daten wurden verändert.");
        if (result.Todos is null || result.Categories is null || result.TimeEntries is null)
            throw new InvalidDataException("Ungültige Datenstruktur.");
        if (result.Todos.Any(t => t is null || string.IsNullOrWhiteSpace(t.Title) || t.CategoryIds is null || t.RelatedTodoIds is null)
            || result.Categories.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name))
            || result.TimeEntries.Any(e => e is null || !result.Todos.Any(t => t.Id == e.TodoId)
                || (e.EndedAt.HasValue && e.EndedAt < e.StartedAt))
            || result.TimeEntries.Count(e => e.EndedAt is null) > 1
            || result.Todos.Select(t => t.Id).Distinct().Count() != result.Todos.Count
            || result.Categories.Select(c => c.Id).Distinct().Count() != result.Categories.Count
            || result.TimeEntries.Select(e => e.Id).Distinct().Count() != result.TimeEntries.Count)
            throw new InvalidDataException("Inkonsistente Datenstruktur.");
        return result;
    }

    private static AppData Clone(AppData value) =>
        JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(value, JsonDefaults.Options), JsonDefaults.Options)!;

    public AppData Read() { lock (gate) return Clone(data); }

    public AppData Mutate(long expectedVersion, Action<AppData> action)
    {
        lock (gate)
        {
            if (expectedVersion != data.Version)
                throw new ConflictException("Die Daten wurden zwischenzeitlich geändert. Bitte aktualisieren und erneut speichern.");
            var next = Clone(data);
            action(next);
            next.Version++;
            var temp = filePath + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, next, JsonDefaults.Options);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(filePath)) File.Copy(filePath, filePath + ".bak", true);
                File.Move(temp, filePath, overwrite: true);
                data = next;
                return Clone(data);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public void Dispose() => processLock.Dispose();
}
