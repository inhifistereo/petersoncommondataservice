using System.Text.Json.Serialization;

namespace PetersonCommonDataService.Models
{
    public class ToDoistSection
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; }
    }
}