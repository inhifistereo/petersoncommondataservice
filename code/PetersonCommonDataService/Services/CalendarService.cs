using Microsoft.Graph;
using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Services
{
    public class CalendarService
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly string _userEmail;

        public CalendarService(GraphServiceClient graphServiceClient)
        {
            _graphServiceClient = graphServiceClient;
            _userEmail = Environment.GetEnvironmentVariable("USER_EMAIL") ?? throw new InvalidOperationException("USER_EMAIL not set.");
        }

        public async Task<List<CalendarEvent>> GetCalendarEventsAsync()
        {
            var startDateTime = DateTime.UtcNow.ToString("o"); // ISO 8601 format
            var endDateTime = DateTime.UtcNow.AddDays(5).ToString("o");

            var events = await _graphServiceClient.Users[_userEmail]
                .CalendarView
                .Request(new[]
                {
                    new QueryOption("startDateTime", startDateTime),
                    new QueryOption("endDateTime", endDateTime)
                })
                .Header("Prefer", "outlook.timezone=\"UTC\"")
                .GetAsync();

            return events?.CurrentPage?.Select(e => new CalendarEvent
            {
                Subject = e.Subject ?? "No Subject",
                Start = ConvertToCentralTime(e.Start),
                End = ConvertToCentralTime(e.End)
            }).ToList() ?? new List<CalendarEvent>();
        }

        private DateTime ConvertToCentralTime(Microsoft.Graph.DateTimeTimeZone dateTimeTimeZone)
        {
            if (dateTimeTimeZone?.DateTime == null)
            {
                return default(DateTime);
            }

            if (DateTime.TryParse(dateTimeTimeZone.DateTime, out DateTime parsedDateTime))
            {
                TimeZoneInfo centralTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(parsedDateTime, centralTimeZone);
            }

            return default(DateTime);
        }
    }
}
