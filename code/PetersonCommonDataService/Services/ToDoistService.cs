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
        Task<List<ToDoistSection>> GetSectionsAsync(string projectId);
    }

    public class ToDoistService : IToDoistService
    {
        private readonly RestClient _restClient;
        private readonly string _apiToken;

        public ToDoistService(IConfiguration configuration)
        {
            _apiToken = configuration["TODOIST-API-KEY"] ?? throw new Exception("TODOIST-API-KEY is not configured");
            _restClient = new RestClient("https://api.todoist.com/api/v1");
        }

        public async Task<List<ToDoistTask>> GetTasksAsync()
        {
            var request = new RestRequest("tasks", Method.Get);
            request.AddHeader("Authorization", $"Bearer {_apiToken}");

            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Todoist API error on tasks: {(int)response.StatusCode} {response.StatusCode} — {response.Content}");
            }

            return (JsonSerializer.Deserialize<ToDoistPagedResponse<ToDoistTask>>(response.Content) ?? new()).Results;
        }

        public async Task<List<ToDoistSection>> GetSectionsAsync(string projectId)
        {
            var request = new RestRequest($"sections?project_id={projectId}", Method.Get);
            request.AddHeader("Authorization", $"Bearer {_apiToken}");

            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Todoist API error on sections (project {projectId}): {(int)response.StatusCode} {response.StatusCode} — {response.Content}");
            }

            return (JsonSerializer.Deserialize<ToDoistPagedResponse<ToDoistSection>>(response.Content) ?? new()).Results;
        }
    }
}
