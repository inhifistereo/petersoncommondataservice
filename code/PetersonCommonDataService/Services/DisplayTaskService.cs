using Microsoft.Extensions.Options;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Services;

/// <summary>
/// Produces the display's task list: fetch, filter to the labelled tasks, colour them
/// from their section, sort Red → Yellow → Green.
/// </summary>
/// <remarks>
/// Caching wraps the mapped result rather than the raw Todoist payload, so a cache hit
/// costs no re-mapping and the stored shape is the one actually served.
/// </remarks>
public sealed class DisplayTaskService(
    IToDoistService toDoistService,
    ICachedSource cache,
    IOptions<TodoistOptions> options,
    ILogger<DisplayTaskService> logger)
{
    private readonly TodoistOptions _options = options.Value;

    public Task<CachedResult<IReadOnlyList<DakBoardTask>>> GetDisplayTasksAsync(CancellationToken cancellationToken) =>
        cache.GetAsync(
            "tasks",
            TimeSpan.FromSeconds(_options.CacheSeconds),
            TimeSpan.FromHours(_options.LastGoodHours),
            BuildAsync,
            cancellationToken);

    private async Task<IReadOnlyList<DakBoardTask>> BuildAsync(CancellationToken cancellationToken)
    {
        var projectId = _options.ProjectId;

        var sections = await toDoistService.GetSectionsAsync(projectId, cancellationToken);
        var tasks = await toDoistService.GetTasksAsync(projectId, cancellationToken);

        logger.LogInformation(
            "Retrieved {SectionCount} sections and {TaskCount} tasks for project {ProjectId}",
            sections.Count, tasks.Count, projectId);

        var displayTasks = tasks
            .Where(task => !task.IsCompleted && task.Labels.Contains(_options.DisplayLabel))
            .Select(task => ToDisplayTask(task, sections))
            .OrderBy(task => ColorRank(task.Color))
            .ToList();

        logger.LogInformation("Filtered to {DisplayTaskCount} display tasks", displayTasks.Count);

        return displayTasks;
    }

    private DakBoardTask ToDisplayTask(ToDoistTask task, IReadOnlyList<ToDoistSection> sections)
    {
        var section = sections.FirstOrDefault(s => s.Id == task.SectionId);
        if (section is null)
        {
            logger.LogWarning(
                "Section {SectionId} not found for task {TaskId}; falling back to default colour",
                task.SectionId, task.Id);
        }

        return new DakBoardTask
        {
            Id = task.Id,
            Content = task.Content,
            Color = section?.Name.ToUpperInvariant() ?? "BLACK",
        };
    }

    private static int ColorRank(string color) => color switch
    {
        "RED" => 0,
        "YELLOW" => 1,
        "GREEN" => 2,
        _ => 3,
    };
}
