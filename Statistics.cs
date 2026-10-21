namespace FocusFlow.Core;

public static class Statistics
{
    public static object Build(AppData data, DateTimeOffset now)
    {
        double Seconds(Guid id) => data.TimeEntries.Where(e => e.TodoId == id).Sum(e => e.SecondsAt(now));
        bool Pending(TodoItem t) => t.Status is TodoStatus.Open or TodoStatus.InProgress;
        var pending = data.Todos.Where(Pending).ToList();
        var overdue = pending.Where(t => t.DueAt < now).OrderBy(t => t.DueAt).ToList();
        // UTC buckets make the API deterministic; the frontend labels UTC explicitly.
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var days = Enumerable.Range(0, 14).Select(i =>
        {
            var start = today.AddDays(i - 13); var end = start.AddDays(1);
            var seconds = data.TimeEntries.Sum(e =>
            {
                var from = e.StartedAt > start ? e.StartedAt : start;
                var to = (e.EndedAt ?? now) < end ? (e.EndedAt ?? now) : end;
                return Math.Max(0, (to - from).TotalSeconds);
            });
            return new { date = start.ToString("yyyy-MM-dd"), seconds };
        }).ToList();
        var categories = data.Categories.Select(c =>
        {
            var tasks = data.Todos.Where(t => t.CategoryIds.Contains(c.Id)).ToList();
            return new { c.Id, c.Name, c.Color, c.Resource, c.BudgetHours, c.TargetDate,
                count = tasks.Count, done = tasks.Count(t => t.Status == TodoStatus.Done),
                estimatedHours = tasks.Sum(t => t.EstimatedHours),
                remainingEstimatedHours = tasks.Where(Pending).Sum(t => Math.Max(0, t.EstimatedHours - (decimal)(Seconds(t.Id) / 3600))),
                seconds = tasks.Sum(t => Seconds(t.Id)) };
        }).ToList();
        var resources = data.Todos.GroupBy(t => string.IsNullOrWhiteSpace(t.Resource) ? "Nicht zugeordnet" : t.Resource)
            .Select(g => new { name = g.Key, count = g.Count(), pending = g.Count(Pending),
                estimatedHours = g.Sum(t => t.EstimatedHours), seconds = g.Sum(t => Seconds(t.Id)) }).ToList();
        return new
        {
            generatedAt = now, data.Version, total = data.Todos.Count,
            open = data.Todos.Count(t => t.Status == TodoStatus.Open),
            inProgress = data.Todos.Count(t => t.Status == TodoStatus.InProgress),
            done = data.Todos.Count(t => t.Status == TodoStatus.Done),
            cancelled = data.Todos.Count(t => t.Status == TodoStatus.Cancelled),
            overdue = overdue.Select(t => new { t.Id, t.Title, t.DueAt }),
            upcoming = pending.Where(t => t.DueAt >= now && t.DueAt <= now.AddDays(7)).OrderBy(t => t.DueAt)
                .Select(t => new { t.Id, t.Title, t.DueAt }),
            withoutDueDate = pending.Count(t => !t.DueAt.HasValue),
            estimatedHours = data.Todos.Sum(t => t.EstimatedHours),
            pendingEstimatedHours = pending.Sum(t => t.EstimatedHours),
            remainingEstimatedHours = pending.Sum(t => Math.Max(0, t.EstimatedHours - (decimal)(Seconds(t.Id) / 3600))),
            totalSeconds = data.TimeEntries.Sum(e => e.SecondsAt(now)),
            todaySeconds = days.Last().seconds,
            categories, resources, days,
            topTasks = data.Todos.OrderByDescending(t => Seconds(t.Id)).Take(8)
                .Select(t => new { t.Id, t.Title, t.EstimatedHours, seconds = Seconds(t.Id) }),
            completedRecent = data.Todos.Where(t => t.Status == TodoStatus.Done)
                .OrderByDescending(t => t.CompletedAt).Take(10).Select(t => new { t.Id, t.Title, t.CompletedAt })
        };
    }
}
