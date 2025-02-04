using Microsoft.AspNetCore.Mvc;
using PetersonCommonDataService.Services;
using PetersonCommonDataService.Models;
using Microsoft.Extensions.Logging;

namespace PetersonCommonDataService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TasksController : ControllerBase
    {
        private readonly IToDoistService _toDoistService;
        private readonly ILogger<TasksController> _logger;

        public TasksController(IToDoistService toDoistService, ILogger<TasksController> logger)
        {
            _toDoistService = toDoistService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetTasks()
        {
            long projectId = 2329811342; // Replace with your project ID

            var sections = await _toDoistService.GetSectionsAsync(projectId);
            var tasks = await _toDoistService.GetTasksAsync();

            _logger.LogInformation($"Retrieved {sections.Count} sections");
            foreach (var section in sections)
            {
                _logger.LogInformation($"Section: {section.Id}, Name: {section.Name}");
            }

            _logger.LogInformation($"Retrieved {tasks.Count} tasks");
            foreach (var task in tasks)
            {
                _logger.LogInformation($"Task: {task.Id}, Content: {task.Content}, SectionId: {task.SectionId}, IsCompleted: {task.IsCompleted}, Labels: {string.Join(", ", task.Labels)}");
            }

            var filteredTasks = tasks
                .Where(task => !task.IsCompleted && task.Labels != null && task.Labels.Contains("DakBoard"))
                .ToList();

            foreach (var task in filteredTasks)
            {
                var section = sections.FirstOrDefault(s => s.Id == task.SectionId);
                if (section != null)
                {
                    task.Color = section.Name;
                    _logger.LogInformation($"Task {task.Id} assigned color {task.Color}");
                }
                else
                {
                    _logger.LogWarning($"Section with ID {task.SectionId} not found for task {task.Id}");
                }
            }

            _logger.LogInformation($"Filtered down to {filteredTasks.Count} tasks");

            return Ok(filteredTasks);
        }
    }
}