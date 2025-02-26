using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PetersonCommonDataService.Models;
using TimeZoneConverter;

public class CalendarService
{
    private readonly HttpClient _httpClient;
    private readonly TimeZoneInfo _centralTimeZone;

    public CalendarService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _centralTimeZone = TZConvert.GetTimeZoneInfo("Central Standard Time");
    }

    public async Task<List<PetersonCommonDataService.Models.CalendarEvent>> GetUpcomingEventsAsync(string icsUrl)
    {
        var icsContent = await _httpClient.GetStringAsync(icsUrl);
        var calendar = Calendar.Load(icsContent);
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _centralTimeZone).Date;
        var lastDay = today.AddDays(4); // Today plus 4 days in the future
        var events = new List<PetersonCommonDataService.Models.CalendarEvent>();

        foreach (var calendarEvent in calendar.Events)
        {
            var occurrences = calendarEvent.GetOccurrences(today, lastDay);
            foreach (var occurrence in occurrences)
            {
                var startDateUtc = occurrence.Period.StartTime.AsUtc;
                var endDateUtc = occurrence.Period.EndTime.AsUtc;

                // Check if the event is an all-day event
                bool isAllDay = occurrence.Period.StartTime.Value.TimeOfDay == TimeSpan.Zero && occurrence.Period.EndTime.Value.TimeOfDay == TimeSpan.Zero;

                var startDate = isAllDay ? TimeZoneInfo.ConvertTimeFromUtc(startDateUtc.Date, _centralTimeZone) : TimeZoneInfo.ConvertTimeFromUtc(startDateUtc, _centralTimeZone);
                var endDate = isAllDay ? TimeZoneInfo.ConvertTimeFromUtc(endDateUtc.Date, _centralTimeZone) : TimeZoneInfo.ConvertTimeFromUtc(endDateUtc, _centralTimeZone);

                events.Add(new PetersonCommonDataService.Models.CalendarEvent
                {
                    Subject = calendarEvent.Summary,
                    Start = startDate,
                    End = endDate,
                    IsAllDay = isAllDay // Set the IsAllDay property
                });
            }
        }

        // Sort events by date, with all-day events appearing first for each day
        return events
            .OrderBy(e => e.Start.Date)
            .ThenBy(e => e.IsAllDay ? 0 : 1)
            .ThenBy(e => e.Start.TimeOfDay)
            .ToList();
    }
}
