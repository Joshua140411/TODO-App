using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusFlow.Core;

public enum TodoStatus { Open, InProgress, Done, Cancelled }
public enum TodoPriority { Low, Normal, High, Urgent }

public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TodoStatus Status { get; set; }
    public TodoPriority Priority { get; set; } = TodoPriority.Normal;
    public decimal EstimatedHours { get; set; }
    public string Resource { get; set; } = "";
    public List<Guid> CategoryIds { get; set; } = [];
    public List<Guid> RelatedTodoIds { get; set; } = [];
    public Guid? ParentTodoId { get; set; }
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#f97316";
    public string Resource { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public DateTimeOffset? TargetDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TodoId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Note { get; set; } = "";
    public bool IsManual { get; set; }
    public double SecondsAt(DateTimeOffset now) => Math.Max(0, ((EndedAt ?? now) - StartedAt).TotalSeconds);
}

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;
    public long Version { get; set; }
    public List<TodoItem> Todos { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<TimeEntry> TimeEntries { get; set; } = [];
}

public sealed record TodoInput(string Title, string? Description, DateTimeOffset? CreatedAt,
    DateTimeOffset? DueAt, TodoStatus Status, TodoPriority Priority, decimal EstimatedHours,
    string? Resource, List<Guid>? CategoryIds, List<Guid>? RelatedTodoIds, Guid? ParentTodoId);
public sealed record CategoryInput(string Name, string? Description, string? Color,
    string? Resource, decimal BudgetHours, DateTimeOffset? TargetDate);
public sealed record ManualTimeInput(DateTimeOffset StartedAt, decimal Minutes, string? Note);

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed class ValidationException(string message) : Exception(message);
public sealed class MissingException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
