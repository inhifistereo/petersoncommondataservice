using System;

namespace PetersonCommonDataService.Models
{
    public class CalendarEvent
    {
        public string Subject { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }
}