using RestSharp;
using System.Text.Json;
using PetersonCommonDataService.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PetersonCommonDataService.Services
{
    public interface IToDoistService
    {
        Task<List<ToDoistTask>> GetTasksAsync();
        Task<List<ToDoistSection>> GetSectionsAsync(long projectId);
    }

    public class ToDoistService : IToDoistService
    {
        private readonly string _apiToken;

        public ToDoistService(IConfiguration configuration)
        {
            _apiToken = Environment.GetEnvironmentVariable("TODOIST_API_KEY") ?? throw new Exception("TODOIST_API_KEY environment variable not set");
        }

        public async Task<List<ToDoistTask>> GetTasksAsync()
        {
            var client = new RestClient("https://api.todoist.com/rest/v2");
            var request = new RestRequest("tasks", Method.Get);
            request.AddHeader("Authorization", $"Bearer {_apiToken}");

            var response = await client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception("Error querying ToDoist API");
            }

            return JsonSerializer.Deserialize<List<ToDoistTask>>(response.Content);
        }

        public async Task<List<ToDoistSection>> GetSectionsAsync(long projectId)
        {
            var client = new RestClient("https://api.todoist.com/rest/v2");
            var request = new RestRequest($"sections?project_id={projectId}", Method.Get);
            request.AddHeader("Authorization", $"Bearer {_apiToken}");

            var response = await client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception("Error querying ToDoist API");
            }

            return JsonSerializer.Deserialize<List<ToDoistSection>>(response.Content);
        }
    }
}