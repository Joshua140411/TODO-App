using FocusFlow.Core;

var tests = new (string Name, Action Run)[]
{
    ("Create task with normalized fields and default creation time", () =>
    {
        using var f = new Fixture();
        var d = f.Service.CreateTodo(f.Input("  First task  "), 0);
        Check(d.Version == 1 && d.Todos.Single().Title == "First task");
        Check(d.Todos.Single().CreatedAt == f.Clock.GetUtcNow());
    }),
    ("Invalid title rolls back memory and disk version", () =>
    {
        using var f = new Fixture();
        Throws<ValidationException>(() => f.Service.CreateTodo(f.Input(" "), 0));
        Check(f.Service.Read().Todos.Count == 0 && f.Service.Read().Version == 0);
        Check(!File.Exists(Path.Combine(f.Directory, "focusflow.json")));
    }),
    ("Invalid enums and negative workload are rejected", () =>
    {
        using var f = new Fixture();
        Throws<ValidationException>(() => f.Service.CreateTodo(f.Input("A") with { Status = (TodoStatus)99 }, 0));
        Throws<ValidationException>(() => f.Service.CreateTodo(f.Input("A") with { EstimatedHours = -1 }, 0));
    }),
    ("Read snapshots are isolated from stored data", () =>
    {
        using var f = new Fixture(); f.Add("A");
        var copy = f.Service.Read(); copy.Todos[0].Title = "Tampered";
        Check(f.Service.Read().Todos[0].Title == "A");
    }),
    ("Stale data version causes conflict without mutation", () =>
    {
        using var f = new Fixture(); f.Add("A");
        Throws<ConflictException>(() => f.Service.CreateTodo(f.Input("B"), 0));
        Check(f.Service.Read().Todos.Count == 1 && f.Service.Read().Version == 1);
    }),
    ("Concurrent writers cannot overwrite each other", () =>
    {
        using var f = new Fixture(); int success = 0, conflicts = 0;
        Parallel.For(0, 12, i =>
        {
            try { f.Service.CreateTodo(f.Input("A" + i), 0); Interlocked.Increment(ref success); }
            catch (ConflictException) { Interlocked.Increment(ref conflicts); }
        });
        Check(success == 1 && conflicts == 11 && f.Service.Read().Todos.Count == 1);
    }),
    ("Category uniqueness is case-insensitive", () =>
    {
        using var f = new Fixture(); f.Service.CreateCategory(f.Category("Engineering"), 0);
        Throws<ValidationException>(() => f.Service.CreateCategory(f.Category("engineering"), 1));
    }),
    ("Invalid category colors and budgets are rejected", () =>
    {
        using var f = new Fixture();
        Throws<ValidationException>(() => f.Service.CreateCategory(f.Category("A") with { Color = "red" }, 0));
        Throws<ValidationException>(() => f.Service.CreateCategory(f.Category("A") with { BudgetHours = -1 }, 0));
    }),
    ("Unknown category and self-reference are rejected", () =>
    {
        using var f = new Fixture();
        Throws<ValidationException>(() => f.Service.CreateTodo(f.Input("A") with { CategoryIds = [Guid.NewGuid()] }, 0));
        var a = f.Add("A");
        Throws<ValidationException>(() => f.Service.UpdateTodo(a.Id, f.Input("A") with { RelatedTodoIds = [a.Id] }, f.Version));
    }),
    ("Parent hierarchy prevents indirect cycles", () =>
    {
        using var f = new Fixture(); var a = f.Add("A");
        var b = f.Service.CreateTodo(f.Input("B") with { ParentTodoId = a.Id }, f.Version).Todos.Last();
        var c = f.Service.CreateTodo(f.Input("C") with { ParentTodoId = b.Id }, f.Version).Todos.Last();
        Throws<ValidationException>(() => f.Service.UpdateTodo(a.Id, f.Input("A") with { ParentTodoId = c.Id }, f.Version));
        Check(f.Service.Read().Todos[0].ParentTodoId is null);
    }),
    ("Timer starts and marks task in progress", () =>
    {
        using var f = new Fixture(); var a = f.Add("A");
        var d = f.Service.StartTimer(a.Id, f.Version);
        Check(d.Todos.Single().Status == TodoStatus.InProgress && d.TimeEntries.Single().EndedAt is null);
    }),
    ("Timer stop records exact elapsed time", () =>
    {
        using var f = new Fixture(); var a = f.Add("A");f.Service.StartTimer(a.Id, f.Version);
        f.Clock.Advance(TimeSpan.FromMinutes(12)); var d = f.Service.StopTimer(a.Id, f.Version);
        Check(d.TimeEntries.Single().SecondsAt(f.Clock.GetUtcNow()) == 720);
    }),
    ("Switching timer stops previous session", () =>
    {
        using var f = new Fixture();var a = f.Add("A");var b = f.Add("B");
        f.Service.StartTimer(a.Id, f.Version);f.Clock.Advance(TimeSpan.FromMinutes(5));
        var d = f.Service.StartTimer(b.Id, f.Version);
        Check(d.TimeEntries.Count(e => e.EndedAt is null) == 1);
        Check(d.TimeEntries.First().SecondsAt(f.Clock.GetUtcNow()) == 300);
        Check(d.TimeEntries.Last().TodoId == b.Id);
    }),
    ("Starting same timer is idempotent with respect to entries", () =>
    {
        using var f = new Fixture();var a = f.Add("A");
        f.Service.StartTimer(a.Id, f.Version);f.Service.StartTimer(a.Id, f.Version);
        Check(f.Service.Read().TimeEntries.Count == 1);
    }),
    ("Completing task stops timer and reopening clears completion date", () =>
    {
        using var f = new Fixture();var a = f.Add("A");f.Service.StartTimer(a.Id, f.Version);
        f.Clock.Advance(TimeSpan.FromMinutes(1));var d = f.Service.ChangeStatus(a.Id, TodoStatus.Done, f.Version);
        Check(d.TimeEntries.Single().EndedAt.HasValue && d.Todos.Single().CompletedAt.HasValue);
        d = f.Service.ChangeStatus(a.Id, TodoStatus.Open, f.Version);
        Check(d.Todos.Single().CompletedAt is null);
    }),
    ("Cancelled and completed tasks cannot start timers", () =>
    {
        using var f = new Fixture();var a = f.Add("A");
        f.Service.ChangeStatus(a.Id, TodoStatus.Done, f.Version);
        Throws<ValidationException>(() => f.Service.StartTimer(a.Id, f.Version));
        f.Service.ChangeStatus(a.Id, TodoStatus.Cancelled, f.Version);
        Throws<ValidationException>(() => f.Service.StartTimer(a.Id, f.Version));
    }),
    ("Manual time validates future and nonpositive duration", () =>
    {
        using var f = new Fixture();var a = f.Add("A");var now = f.Clock.GetUtcNow();
        Throws<ValidationException>(() => f.Service.AddTime(a.Id, new(now, 10, ""), f.Version));
        Throws<ValidationException>(() => f.Service.AddTime(a.Id, new(now.AddHours(-1), 0, ""), f.Version));
        f.Service.AddTime(a.Id, new(now.AddHours(-1), 30, "Manual"), f.Version);
        Check(f.Service.Read().TimeEntries.Single().IsManual);
    }),
    ("Manual overlap is rejected across tasks; adjacent entries allowed", () =>
    {
        using var f = new Fixture();var a = f.Add("A");var b = f.Add("B");var now = f.Clock.GetUtcNow();
        f.Service.AddTime(a.Id, new(now.AddHours(-2), 30, ""), f.Version);
        Throws<ValidationException>(() => f.Service.AddTime(b.Id, new(now.AddMinutes(-110), 15, ""), f.Version));
        f.Service.AddTime(b.Id, new(now.AddMinutes(-90), 30, ""), f.Version);
        Check(f.Service.Read().TimeEntries.Count == 2);
    }),
    ("Active entry cannot be deleted, stopped entry can", () =>
    {
        using var f = new Fixture();var a = f.Add("A");var d = f.Service.StartTimer(a.Id, f.Version);
        var id = d.TimeEntries.Single().Id;
        Throws<ValidationException>(() => f.Service.DeleteTime(id, f.Version));
        f.Service.StopTimer(a.Id, f.Version);f.Service.DeleteTime(id, f.Version);
        Check(f.Service.Read().TimeEntries.Count == 0);
    }),
    ("Deleting task cleans links, parent pointers and own time", () =>
    {
        using var f = new Fixture();var a = f.Add("A");
        var b = f.Service.CreateTodo(f.Input("B") with { ParentTodoId = a.Id, RelatedTodoIds = [a.Id] }, f.Version).Todos.Last();
        f.Service.StartTimer(a.Id, f.Version);var d = f.Service.DeleteTodo(a.Id, f.Version);
        Check(d.Todos.Single().Id == b.Id && d.Todos.Single().ParentTodoId is null);
        Check(d.Todos.Single().RelatedTodoIds.Count == 0 && d.TimeEntries.Count == 0);
    }),
    ("Deleting category preserves task and tracked time", () =>
    {
        using var f = new Fixture();var c = f.Service.CreateCategory(f.Category("A"), 0).Categories.Single();
        var a = f.Service.CreateTodo(f.Input("Task") with { CategoryIds = [c.Id] }, f.Version).Todos.Single();
        f.Service.StartTimer(a.Id, f.Version);f.Service.DeleteCategory(c.Id, f.Version);
        Check(f.Service.Read().Todos.Single().CategoryIds.Count == 0 && f.Service.Read().TimeEntries.Count == 1);
    }),
    ("Persistence survives restart including active timer", () =>
    {
        using var f = new Fixture();var a = f.Add("A");f.Service.StartTimer(a.Id, f.Version);
        f.Store.Dispose();using var reopened = new JsonStore(f.Directory);
        Check(reopened.Read().Todos.Single().Title == "A" && reopened.Read().TimeEntries.Single().EndedAt is null);
    }),
    ("Second process writer is blocked", () =>
    {
        using var f = new Fixture();Throws<IOException>(() => { using var second = new JsonStore(f.Directory); });
    }),
    ("Corrupt primary restores backup with visible notice", () =>
    {
        using var f = new Fixture();f.Add("A");f.Add("B");f.Store.Dispose();
        File.WriteAllText(Path.Combine(f.Directory, "focusflow.json"), "{ invalid");
        using var reopened = new JsonStore(f.Directory);
        Check(reopened.RecoveryNotice is not null && reopened.Read().Todos.Count == 1);
        Check(System.IO.Directory.GetFiles(f.Directory,"*.corrupt-*").Length == 1);
    }),
    ("Unknown schema is rejected without overwriting data", () =>
    {
        using var f = new Fixture();f.Add("A");f.Add("B");f.Store.Dispose();
        var path = Path.Combine(f.Directory, "focusflow.json");
        var original = File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999");
        File.WriteAllText(path, original);
        Throws<NotSupportedException>(() => { using var reopened = new JsonStore(f.Directory); });
        Check(File.ReadAllText(path) == original);
    }),
    ("Statistics split overnight session and avoid global category double counting", () =>
    {
        using var f = new Fixture();f.Clock.Set(new DateTimeOffset(2026, 10, 21, 1, 0, 0, TimeSpan.Zero));
        var c1 = f.Service.CreateCategory(f.Category("A"), f.Version).Categories.Last();
        var c2 = f.Service.CreateCategory(f.Category("B"), f.Version).Categories.Last();
        var a = f.Service.CreateTodo(f.Input("Task") with { CategoryIds = [c1.Id, c2.Id], EstimatedHours = 3 }, f.Version).Todos.Single();
        f.Service.AddTime(a.Id, new(f.Clock.GetUtcNow().AddHours(-2), 120, "Midnight"), f.Version);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(Statistics.Build(f.Service.Read(), f.Clock.GetUtcNow()), JsonDefaults.Options);
        Check(json.GetProperty("totalSeconds").GetDouble() == 7200);
        Check(json.GetProperty("todaySeconds").GetDouble() == 3600);
        Check(json.GetProperty("categories")[0].GetProperty("seconds").GetDouble() == 7200);
        Check(json.GetProperty("categories")[1].GetProperty("seconds").GetDouble() == 7200);
        Check(json.GetProperty("remainingEstimatedHours").GetDecimal() == 1);
    }),
    ("Completion status excluded from overdue and remaining effort", () =>
    {
        using var f = new Fixture();var a = f.Service.CreateTodo(f.Input("A") with { DueAt = f.Clock.GetUtcNow().AddDays(-1), EstimatedHours = 2 }, f.Version).Todos.Single();
        f.Service.ChangeStatus(a.Id, TodoStatus.Done, f.Version);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(Statistics.Build(f.Service.Read(), f.Clock.GetUtcNow()), JsonDefaults.Options);
        Check(json.GetProperty("overdue").GetArrayLength() == 0 && json.GetProperty("remainingEstimatedHours").GetDecimal() == 0);
    }),
    ("Clock moving backward never produces negative time", () =>
    {
        using var f = new Fixture();var a = f.Add("A");f.Service.StartTimer(a.Id, f.Version);
        f.Clock.Advance(TimeSpan.FromMinutes(-5));var d = f.Service.StopTimer(a.Id, f.Version);
        Check(d.TimeEntries.Single().SecondsAt(f.Clock.GetUtcNow()) == 0);
    })
};
int failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine("PASS  " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL  " + name + "\n" + ex); }
}
Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
sealed class FakeClock : TimeProvider
{
    private DateTimeOffset time = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => time;
    public void Advance(TimeSpan span) => time += span;
    public void Set(DateTimeOffset value) => time = value;
}
sealed class Fixture : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "focusflow-tests-" + Guid.NewGuid());
    public JsonStore Store { get; }
    public FakeClock Clock { get; } = new();
    public TodoService Service { get; }
    public long Version => Service.Read().Version;
    public Fixture() { Store = new JsonStore(Directory); Service = new TodoService(Store, Clock); }
    public TodoInput Input(string title) => new(title, "Description", null, null, TodoStatus.Open, TodoPriority.Normal, 0, "", [], [], null);
    public CategoryInput Category(string name) => new(name, "Description", "#f97316", "", 0, null);
    public TodoItem Add(string title) => Service.CreateTodo(Input(title), Version).Todos.Last();
    public void Dispose() { Store.Dispose();System.IO.Directory.Delete(Directory, true); }
}
