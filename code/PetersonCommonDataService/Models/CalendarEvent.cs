using System;

namespace PetersonCommonDataService.Models
{
    public class CalendarEvent
    {
        public string Subject { get; set; } = string.Empty;
        public string Start { get; set; }
        public string End { get; set; }
        public bool IsAllDay { get; set; } // Added property to indicate if the event is all-day
    }
}