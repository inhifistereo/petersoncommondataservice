using Microsoft.AspNetCore.Mvc;
using PetersonCommonDataService.Services;
using PetersonCommonDataService.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;

namespace PetersonCommonDataService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TasksController : ControllerBase
    {
        private readonly IToDoistService _toDoistService;
        private readonly ILogger<TasksController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public TasksController(IToDoistService toDoistService, ILogger<TasksController> logger, HttpClient httpClient, IConfiguration configuration)
        {
            _toDoistService = toDoistService;
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        // GET /tasks
        // Returns tasks formatted for the DakBoard display, sorted Red → Yellow → Green.
        //
        // How it works:
        //   1. Fetches all sections in the configured Todoist project. The section names
        //      are expected to be "Red", "Yellow", and "Green" — these become the task colors.
        //   2. Fetches all tasks from Todoist.
        //   3. Filters to only incomplete tasks tagged with the "DakBoard" label, so you
        //      control which tasks appear on the board by adding/removing that label in Todoist.
        //   4. Maps each task's section → Color (e.g. a task in the "Red" section gets Color = "RED").
        //   5. Sorts by color priority (Red first, then Yellow, then Green) and returns the result.
        [HttpGet]
        public async Task<IActionResult> GetRYGTasks()
        {
            var projectId = _configuration["TODOIST-PROJECT-ID"] ?? throw new Exception("TODOIST-PROJECT-ID is not configured");

            var sections = await _toDoistService.GetSectionsAsync(projectId);
            var tasks = await _toDoistService.GetTasksAsync();

            _logger.LogInformation($"Retrieved {sections.Count} sections and {tasks.Count} tasks");

            // Keep only incomplete tasks that have the "DakBoard" label
            var filteredTasks = tasks
                .Where(task => !task.IsCompleted && task.Labels != null && task.Labels.Contains("DakBoard"))
                .ToList();

            // Set each task's Color from the name of the section it belongs to
            foreach (var task in filteredTasks)
            {
                var section = sections.FirstOrDefault(s => s.Id == task.SectionId);
                if (section != null)
                {
                    task.Color = section.Name.ToUpper();
                }
                else
                {
                    _logger.LogWarning($"Section with ID {task.SectionId} not found for task {task.Id}");
                }
            }

            _logger.LogInformation($"Filtered down to {filteredTasks.Count} tasks");

            var sortedTasks = filteredTasks.OrderBy(task => task.Color == "RED" ? 0 : task.Color == "YELLOW" ? 1 : 2).ToList();

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            return Content(JsonSerializer.Serialize(sortedTasks, jsonOptions), "application/json");
        }

        // GET /tasks/getall
        // Debug/utility endpoint — returns the raw Todoist API response for all tasks in the
        // project with no filtering or transformation. Useful for inspecting what data exists
        // (task IDs, section IDs, labels, etc.) when troubleshooting the main endpoint.
        [HttpGet("getall")]
        public async Task<IActionResult> GetAllTasks()
        {
            var projectId = _configuration["TODOIST-PROJECT-ID"] ?? throw new Exception("TODOIST-PROJECT-ID is not configured");

            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.todoist.com/api/v1/tasks?project_id={projectId}");
            request.Headers.Add("Authorization", $"Bearer {_configuration["TODOIST-API-KEY"]}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var rawJson = await response.Content.ReadAsStringAsync();

            _logger.LogInformation($"Retrieved raw JSON from ToDoist");

            return Content(rawJson, "application/json");
        }
    }
}
