using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Models;
using PetersonCommonDataService.Services;

namespace PetersonCommonDataService.Tests;

public sealed class DisplayTaskServiceTests
{
    private sealed class FakeToDoistService : IToDoistService
    {
        public List<ToDoistTask> Tasks { get; init; } = [];
        public List<ToDoistSection> Sections { get; init; } = [];
        public string? LastProjectId { get; private set; }

        public Task<List<ToDoistTask>> GetTasksAsync(string projectId, CancellationToken cancellationToken = default)
        {
            LastProjectId = projectId;
            return Task.FromResult(Tasks);
        }

        public Task<List<ToDoistSection>> GetSectionsAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sections);
    }

    private static ToDoistSection Section(string id, string name) => new() { Id = id, Name = name, ProjectId = "p" };

    private static ToDoistTask Task_(string id, string content, string sectionId, bool completed = false, params string[] labels) =>
        new()
        {
            Id = id,
            Content = content,
            SectionId = sectionId,
            IsCompleted = completed,
            Labels = [.. labels],
        };

    private static DisplayTaskService Build(IToDoistService upstream)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var cache = new CachedSource(new MemoryCache(new MemoryCacheOptions()), clock, NullLogger<CachedSource>.Instance);
        var options = Options.Create(new TodoistOptions
        {
            ApiKey = "k",
            ProjectId = "project-1",
            DisplayLabel = "DakBoard",
        });

        return new DisplayTaskService(upstream, cache, options, NullLogger<DisplayTaskService>.Instance);
    }

    [Fact]
    public async Task OnlyLabelledIncompleteTasksAreReturned()
    {
        var upstream = new FakeToDoistService
        {
            Sections = [Section("s1", "Red")],
            Tasks =
            [
                Task_("1", "labelled", "s1", false, "DakBoard"),
                Task_("2", "unlabelled", "s1"),
                Task_("3", "completed", "s1", true, "DakBoard"),
                Task_("4", "other label", "s1", false, "Someday"),
            ],
        };

        var result = await Build(upstream).GetDisplayTasksAsync(default);

        Assert.Equal(["labelled"], result.Value.Select(t => t.Content));
    }

    [Fact]
    public async Task ColourComesFromTheSectionName_Uppercased()
    {
        var upstream = new FakeToDoistService
        {
            Sections = [Section("s1", "Yellow")],
            Tasks = [Task_("1", "task", "s1", false, "DakBoard")],
        };

        var result = await Build(upstream).GetDisplayTasksAsync(default);

        Assert.Equal("YELLOW", Assert.Single(result.Value).Color);
    }

    [Fact]
    public async Task TasksSortRedThenYellowThenGreen()
    {
        var upstream = new FakeToDoistService
        {
            Sections = [Section("g", "Green"), Section("r", "Red"), Section("y", "Yellow")],
            Tasks =
            [
                Task_("1", "green", "g", false, "DakBoard"),
                Task_("2", "yellow", "y", false, "DakBoard"),
                Task_("3", "red", "r", false, "DakBoard"),
            ],
        };

        var result = await Build(upstream).GetDisplayTasksAsync(default);

        Assert.Equal(["red", "yellow", "green"], result.Value.Select(t => t.Content));
    }

    [Fact]
    public async Task TaskInAnUnknownSection_FallsBackToBlackAndSortsLast()
    {
        var upstream = new FakeToDoistService
        {
            Sections = [Section("r", "Red")],
            Tasks =
            [
                Task_("1", "orphan", "missing-section", false, "DakBoard"),
                Task_("2", "red", "r", false, "DakBoard"),
            ],
        };

        var result = await Build(upstream).GetDisplayTasksAsync(default);

        Assert.Equal(["red", "orphan"], result.Value.Select(t => t.Content));
        Assert.Equal("BLACK", result.Value[^1].Color);
    }

    [Fact]
    public async Task TasksAreRequestedForTheConfiguredProject()
    {
        // The original outage was an unscoped query returning the first 50 tasks across
        // the whole account, so none of the labelled ones appeared.
        var upstream = new FakeToDoistService { Sections = [], Tasks = [] };

        await Build(upstream).GetDisplayTasksAsync(default);

        Assert.Equal("project-1", upstream.LastProjectId);
    }

    [Fact]
    public async Task ResultIsCached_SoASecondCallDoesNotRefetch()
    {
        var upstream = new CountingToDoistService();
        var service = Build(upstream);

        await service.GetDisplayTasksAsync(default);
        await service.GetDisplayTasksAsync(default);

        Assert.Equal(1, upstream.TaskCalls);
    }

    private sealed class CountingToDoistService : IToDoistService
    {
        public int TaskCalls { get; private set; }

        public Task<List<ToDoistTask>> GetTasksAsync(string projectId, CancellationToken cancellationToken = default)
        {
            TaskCalls++;
            return Task.FromResult(new List<ToDoistTask>());
        }

        public Task<List<ToDoistSection>> GetSectionsAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ToDoistSection>());
    }
}
