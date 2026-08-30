using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Models;
using PetersonCommonDataService.Services;

namespace PetersonCommonDataService.Controllers;

[ApiController]
[Route("tasks")]
public sealed class TasksController(
    IToDoistService toDoistService,
    IOptions<TodoistOptions> options,
    TimeProvider timeProvider,
    ILogger<TasksController> logger) : ControllerBase
{
    private readonly TodoistOptions _options = options.Value;

    /// <summary>
    /// Tasks formatted for the wall display, sorted Red → Yellow → Green.
    /// </summary>
    /// <remarks>
    /// Sections in the configured Todoist project are named "Red"/"Yellow"/"Green" and
    /// become each task's colour. Only incomplete tasks carrying the display label are
    /// returned, so what appears on the wall is controlled by adding or removing that
    /// label in Todoist.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DakBoardTask>>>> GetRygTasks(CancellationToken cancellationToken)
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

        return Ok(new ApiResponse<IReadOnlyList<DakBoardTask>>(
            displayTasks,
            new ResponseMeta
            {
                Source = "todoist",
                FetchedAt = timeProvider.GetUtcNow(),
                TtlSeconds = 90,
            }));
    }

    /// <summary>
    /// Every task in the configured project, unfiltered — the debugging view.
    /// </summary>
    /// <remarks>
    /// Development only. It returns unfiltered task data (labels, section ids, completion
    /// state) that the display has no use for, so it is not exposed in Production.
    /// </remarks>
    [HttpGet("getall")]
    public async Task<ActionResult<IReadOnlyList<ToDoistTask>>> GetAllTasks(
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var tasks = await toDoistService.GetTasksAsync(_options.ProjectId, cancellationToken);
        return Ok(tasks);
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
