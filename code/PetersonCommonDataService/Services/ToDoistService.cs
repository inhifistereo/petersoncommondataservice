using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Errors;
using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Services;

public interface IToDoistService
{
    Task<List<ToDoistTask>> GetTasksAsync(string projectId, CancellationToken cancellationToken = default);
    Task<List<ToDoistSection>> GetSectionsAsync(string projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thin client over the Todoist v1 REST API.
/// </summary>
/// <remarks>
/// Uses a typed <see cref="HttpClient"/> rather than RestSharp so the transport can be
/// faked in tests via a stub <see cref="HttpMessageHandler"/>.
/// </remarks>
public sealed class ToDoistService(HttpClient httpClient, IOptions<TodoistOptions> options, ILogger<ToDoistService> logger)
    : IToDoistService
{
    private const string UpstreamName = "todoist";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoistOptions _options = options.Value;

    public Task<List<ToDoistTask>> GetTasksAsync(string projectId, CancellationToken cancellationToken = default) =>
        GetAllPagesAsync<ToDoistTask>("tasks", projectId, cancellationToken);

    public Task<List<ToDoistSection>> GetSectionsAsync(string projectId, CancellationToken cancellationToken = default) =>
        GetAllPagesAsync<ToDoistSection>("sections", projectId, cancellationToken);

    /// <summary>
    /// Fetches every page of a project-scoped collection.
    /// </summary>
    /// <remarks>
    /// Both the project filter and the cursor loop matter: an unscoped request returns the
    /// first 50 items across the whole account, which is how the DakBoard-labelled tasks
    /// went missing entirely.
    /// </remarks>
    private async Task<List<T>> GetAllPagesAsync<T>(string resource, string projectId, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        string? cursor = null;

        do
        {
            var url = $"{resource}?project_id={Uri.EscapeDataString(projectId)}";
            if (!string.IsNullOrEmpty(cursor))
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = await GetPageAsync<T>(url, projectId, cancellationToken);
            results.AddRange(page.Results);
            cursor = page.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return results;
    }

    private async Task<ToDoistPagedResponse<T>> GetPageAsync<T>(string url, string projectId, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new UpstreamException(UpstreamName, null, "Could not reach the Todoist API.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Log the body for diagnosis; never surface it — it can echo the token.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Todoist request to {Resource} for project {ProjectId} failed with {StatusCode}: {Body}",
                    url, projectId, (int)response.StatusCode, body);

                throw new UpstreamException(
                    UpstreamName,
                    response.StatusCode,
                    $"Todoist returned {(int)response.StatusCode} for '{url}'.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<ToDoistPagedResponse<T>>(SerializerOptions, cancellationToken)
                       ?? new ToDoistPagedResponse<T>();
            }
            catch (JsonException ex)
            {
                throw new UpstreamException(UpstreamName, response.StatusCode, "Todoist returned a response that could not be parsed.", ex);
            }
        }
    }
}
