using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Models;
using PetersonCommonDataService.Services;

namespace PetersonCommonDataService.Controllers;

[ApiController]
[Route("tasks")]
public sealed class TasksController(
    DisplayTaskService displayTaskService,
    IToDoistService toDoistService,
    IOptions<TodoistOptions> options,
    TimeProvider timeProvider) : ControllerBase
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
        var cached = await displayTaskService.GetDisplayTasksAsync(cancellationToken);

        var meta = new ResponseMeta
        {
            Source = "todoist",
            FetchedAt = cached.FetchedAt,
            Stale = cached.Stale,
            StaleReason = cached.StaleReason,
            TtlSeconds = cached.TtlSeconds,
        };

        Response.ApplyFreshness(meta, timeProvider);

        return Ok(new ApiResponse<IReadOnlyList<DakBoardTask>>(cached.Value, meta));
    }

    /// <summary>
    /// Every task in the configured project, unfiltered — the debugging view.
    /// </summary>
    /// <remarks>
    /// Development only, and deliberately uncached so it always shows current upstream
    /// state. It returns data the display has no use for, so it is not exposed in Production.
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
}
