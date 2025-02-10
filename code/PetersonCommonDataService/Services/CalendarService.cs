using Microsoft.Graph;
using PetersonCommonDataService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PetersonCommonDataService.Services
{
    public class CalendarService
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly string _userEmail;

        public CalendarService(GraphServiceClient graphServiceClient)
        {
            _graphServiceClient = graphServiceClient;
            _userEmail = Environment.GetEnvironmentVariable("USER_EMAIL") 
                ?? throw new InvalidOperationException("USER_EMAIL is missing.");
        }

        public async Task<List<CalendarEvent>> GetCalendarEventsAsync()
        {
            var startDateTime = DateTime.UtcNow.ToString("o"); // ISO 8601 format
            var endDateTime = DateTime.UtcNow.AddDays(5).ToString("o");

            // Format the UPN for guest users
            var formattedUserEmail = _userEmail.Replace("#EXT#", "_");

            var events = await _graphServiceClient.Users[formattedUserEmail]
                .CalendarView
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.StartDateTime = startDateTime;
                    requestConfiguration.QueryParameters.EndDateTime = endDateTime;
                    requestConfiguration.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
                });

            return events?.Value?.Select(e => new CalendarEvent
            {
                Subject = e.Subject ?? "No Subject",
                Start = ConvertToCentralTime(e.Start),
                End = ConvertToCentralTime(e.End)
            }).ToList() ?? new List<CalendarEvent>();
        }

        private DateTime ConvertToCentralTime(Microsoft.Graph.Models.DateTimeTimeZone dateTimeTimeZone)
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
