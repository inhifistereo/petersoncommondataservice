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

        public TasksController(IToDoistService toDoistService, ILogger<TasksController> logger, HttpClient httpClient)
        {
            _toDoistService = toDoistService;
            _logger = logger;
            _httpClient = httpClient;
        }

        [HttpGet]
        public async Task<IActionResult> GetRYGTasks()
        {
            long projectId = long.Parse(Environment.GetEnvironmentVariable("TODOIST-PROJECT-ID") ?? "0");

            var sections = await _toDoistService.GetSectionsAsync(projectId);
            var tasks = await _toDoistService.GetTasksAsync();

            _logger.LogInformation($"Retrieved {sections.Count} sections and {tasks.Count} tasks");

            var filteredTasks = tasks
                .Where(task => !task.IsCompleted && task.Labels != null && task.Labels.Contains("DakBoard"))
                .ToList();

            foreach (var task in filteredTasks)
            {
                var section = sections.FirstOrDefault(s => s.Id == task.SectionId);
                if (section != null)
                {
                    task.Color = section.Name.ToUpper(); // Convert to all caps
                }
                else
                {
                    _logger.LogWarning($"Section with ID {task.SectionId} not found for task {task.Id}");
                }
            }

            _logger.LogInformation($"Filtered down to {filteredTasks.Count} tasks");

            // Sort tasks by Red, Yellow, Green
            var sortedTasks = filteredTasks.OrderBy(task => task.Color == "RED" ? 0 : task.Color == "YELLOW" ? 1 : 2).ToList();

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(sortedTasks, jsonOptions);

            return Content(json, "application/json");
        }

        [HttpGet("getall")]
        public async Task<IActionResult> GetAllTasks()
        {
            long projectId = long.Parse(Environment.GetEnvironmentVariable("TODOIST-PROJECT-ID") ?? "0");

            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.todoist.com/rest/v2/tasks?project_id={projectId}");
            request.Headers.Add("Authorization", $"Bearer {Environment.GetEnvironmentVariable("TODOIST-API-KEY")}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var rawJson = await response.Content.ReadAsStringAsync();

            _logger.LogInformation($"Retrieved raw JSON from ToDoist");

            // Return the raw JSON response
            return Content(rawJson, "application/json");
        }
    }
}