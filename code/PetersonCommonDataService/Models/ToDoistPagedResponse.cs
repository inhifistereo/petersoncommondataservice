using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PetersonCommonDataService.Models
{
    public class ToDoistPagedResponse<T>
    {
        [JsonPropertyName("results")]
        public List<T> Results { get; set; } = [];

        [JsonPropertyName("next_cursor")]
        public string? NextCursor { get; set; }
    }
}
