using System.Text.RegularExpressions;

namespace FocusFlow.Core;

public sealed class TodoService(JsonStore store, TimeProvider clock)
{
    public AppData Read() => store.Read();
    public DateTimeOffset Now => clock.GetUtcNow();
    private static TodoItem Find(AppData d, Guid id) => d.Todos.Find(t => t.Id == id)
        ?? throw new MissingException("Die Aufgabe wurde nicht gefunden.");
    private static string Text(string? value, int max, string field, bool required = false)
    {
        var text = (value ?? "").Trim();
        if (required && text.Length == 0) throw new ValidationException($"{field} darf nicht leer sein.");
        if (text.Length > max) throw new ValidationException($"{field}: höchstens {max} Zeichen.");
        return text;
    }
    private static void Hours(decimal hours)
    {
        if (hours is < 0 or > 100000) throw new ValidationException("Stunden müssen zwischen 0 und 100.000 liegen.");
    }
    private static void Date(DateTimeOffset? value)
    {
        if (value.HasValue && (value.Value.Year < 2000 || value.Value.Year > 2100))
            throw new ValidationException("Datumsangaben müssen zwischen 2000 und 2100 liegen.");
    }
    private static void StopActive(AppData d, DateTimeOffset now, Guid? todoId = null)
    {
        foreach (var e in d.TimeEntries.Where(e => e.EndedAt is null && (!todoId.HasValue || e.TodoId == todoId)))
            e.EndedAt = now < e.StartedAt ? e.StartedAt : now;
    }
    private void Apply(AppData d, TodoItem t, TodoInput input)
    {
        t.Title = Text(input.Title, 200, "Titel", true);
        t.Description = Text(input.Description, 10000, "Beschreibung");
        t.Resource = Text(input.Resource, 200, "Ressource");
        if (!Enum.IsDefined(input.Status) || !Enum.IsDefined(input.Priority))
            throw new ValidationException("Ungültiger Status oder ungültige Priorität.");
        Hours(input.EstimatedHours); Date(input.CreatedAt); Date(input.DueAt);
        t.EstimatedHours = input.EstimatedHours;
        t.CreatedAt = input.CreatedAt ?? t.CreatedAt;
        t.DueAt = input.DueAt;
        t.Priority = input.Priority;
        t.CategoryIds = (input.CategoryIds ?? []).Distinct().ToList();
        t.RelatedTodoIds = (input.RelatedTodoIds ?? []).Distinct().ToList();
        if (t.CategoryIds.Any(id => !d.Categories.Any(c => c.Id == id)))
            throw new ValidationException("Eine gewählte Kategorie existiert nicht mehr.");
        if (t.RelatedTodoIds.Any(id => id == t.Id || !d.Todos.Any(other => other.Id == id)))
            throw new ValidationException("Eine verknüpfte Aufgabe ist ungültig.");
        if (input.ParentTodoId.HasValue)
        {
            var seen = new HashSet<Guid> { t.Id };
            Guid? current = input.ParentTodoId;
            while (current.HasValue)
            {
                if (!seen.Add(current.Value)) throw new ValidationException("Übergeordnete Aufgaben dürfen keinen Kreis bilden.");
                current = Find(d, current.Value).ParentTodoId;
            }
        }
        t.ParentTodoId = input.ParentTodoId;
        if (t.Status != TodoStatus.Done && input.Status == TodoStatus.Done) t.CompletedAt = Now;
        if (input.Status != TodoStatus.Done) t.CompletedAt = null;
        t.Status = input.Status;
        t.UpdatedAt = Now;
        if (t.Status is TodoStatus.Done or TodoStatus.Cancelled) StopActive(d, Now, t.Id);
    }
    public AppData CreateTodo(TodoInput input, long version) => store.Mutate(version, d =>
    {
        var t = new TodoItem { CreatedAt = Now };
        Apply(d, t, input); d.Todos.Add(t);
    });
    public AppData UpdateTodo(Guid id, TodoInput input, long version) =>
        store.Mutate(version, d => Apply(d, Find(d, id), input));
    public AppData ChangeStatus(Guid id, TodoStatus status, long version) => store.Mutate(version, d =>
    {
        var t = Find(d, id);
        if (!Enum.IsDefined(status)) throw new ValidationException("Ungültiger Status.");
        if (status == TodoStatus.Done && t.Status != TodoStatus.Done) t.CompletedAt = Now;
        if (status != TodoStatus.Done) t.CompletedAt = null;
        t.Status = status; t.UpdatedAt = Now;
        if (status is TodoStatus.Done or TodoStatus.Cancelled) StopActive(d, Now, id);
    });
    public AppData DeleteTodo(Guid id, long version) => store.Mutate(version, d =>
    {
        d.Todos.Remove(Find(d, id));
        d.TimeEntries.RemoveAll(e => e.TodoId == id);
        foreach (var t in d.Todos)
        {
            t.RelatedTodoIds.Remove(id);
            if (t.ParentTodoId == id) t.ParentTodoId = null;
        }
    });
    private void ApplyCategory(AppData d, Category c, CategoryInput input)
    {
        c.Name = Text(input.Name, 100, "Name", true);
        if (d.Categories.Any(other => other.Id != c.Id && string.Equals(other.Name, c.Name, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Dieser Kategoriename existiert bereits.");
        c.Description = Text(input.Description, 5000, "Beschreibung");
        c.Resource = Text(input.Resource, 200, "Ressource");
        var color = input.Color ?? "#f97316";
        if (!Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) throw new ValidationException("Ungültige Kategorienfarbe.");
        c.Color = color; Hours(input.BudgetHours); Date(input.TargetDate);
        c.BudgetHours = input.BudgetHours; c.TargetDate = input.TargetDate;
    }
    public AppData CreateCategory(CategoryInput input, long version) => store.Mutate(version, d =>
    {
        var c = new Category { CreatedAt = Now }; ApplyCategory(d, c, input); d.Categories.Add(c);
    });
    public AppData UpdateCategory(Guid id, CategoryInput input, long version) => store.Mutate(version, d =>
    {
        var c = d.Categories.Find(c => c.Id == id) ?? throw new MissingException("Kategorie nicht gefunden.");
        ApplyCategory(d, c, input);
    });
    public AppData DeleteCategory(Guid id, long version) => store.Mutate(version, d =>
    {
        var c = d.Categories.Find(c => c.Id == id) ?? throw new MissingException("Kategorie nicht gefunden.");
        d.Categories.Remove(c);
        foreach (var t in d.Todos) t.CategoryIds.Remove(id);
    });
    public AppData StartTimer(Guid id, long version) => store.Mutate(version, d =>
    {
        var t = Find(d, id);
        if (t.Status is TodoStatus.Done or TodoStatus.Cancelled)
            throw new ValidationException("Für erledigte oder abgebrochene Aufgaben kann kein Timer starten.");
        if (d.TimeEntries.Any(e => e.TodoId == id && e.EndedAt is null)) return;
        StopActive(d, Now);
        d.TimeEntries.Add(new TimeEntry { TodoId = id, StartedAt = Now });
        t.Status = TodoStatus.InProgress; t.UpdatedAt = Now;
    });
    public AppData StopTimer(Guid id, long version) => store.Mutate(version, d =>
    {
        Find(d, id); StopActive(d, Now, id);
    });
    public AppData AddTime(Guid id, ManualTimeInput input, long version) => store.Mutate(version, d =>
    {
        Find(d, id); Date(input.StartedAt);
        if (input.Minutes is <= 0 or > 1440) throw new ValidationException("Eine Buchung muss größer als 0 und höchstens 1.440 Minuten sein.");
        var end = input.StartedAt.AddMinutes((double)input.Minutes);
        if (end > Now.AddSeconds(2)) throw new ValidationException("Manuelle Zeitbuchungen dürfen nicht in der Zukunft enden.");
        if (d.TimeEntries.Any(e => input.StartedAt < (e.EndedAt ?? Now) && end > e.StartedAt))
            throw new ValidationException("Die Buchung überschneidet sich mit einer anderen Zeitbuchung.");
        d.TimeEntries.Add(new TimeEntry { TodoId = id, StartedAt = input.StartedAt,
            EndedAt = end, Note = Text(input.Note, 1000, "Notiz"), IsManual = true });
    });
    public AppData DeleteTime(Guid id, long version) => store.Mutate(version, d =>
    {
        var entry = d.TimeEntries.Find(e => e.Id == id) ?? throw new MissingException("Zeitbuchung nicht gefunden.");
        if (entry.EndedAt is null) throw new ValidationException("Den aktiven Timer bitte zuerst stoppen.");
        d.TimeEntries.Remove(entry);
    });
}
