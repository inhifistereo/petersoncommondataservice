namespace PetersonCommonDataService.Models
{
    // Response shape for GET /tasks — what the DakBoard display consumes.
    // Deliberately separate from ToDoistTask so Todoist's wire format
    // (snake_case fields, internal flags) never leaks into our own API.
    public class DakBoardTask
    {
        public string Id { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Color { get; set; } = "BLACK";
    }
}
