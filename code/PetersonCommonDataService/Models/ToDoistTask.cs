using System.Text.Json.Serialization;

namespace PetersonCommonDataService.Models
{
    public class ToDoistTask
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("checked")]
        public bool IsCompleted { get; set; }

        [JsonPropertyName("labels")]
        public List<string> Labels { get; set; } = new List<string>();

        [JsonPropertyName("section_id")]
        public string SectionId { get; set; } = string.Empty;
    }

    public class Due
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
    }
}