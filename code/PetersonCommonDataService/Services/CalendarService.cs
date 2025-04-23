using Ical.Net;
using Ical.Net.CalendarComponents;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PetersonCommonDataService.Models;
using TimeZoneConverter;
using CalendarEvent = PetersonCommonDataService.Models.CalendarEvent;

public class CalendarService
{
    private readonly HttpClient _httpClient;
    private readonly TimeZoneInfo _centralTimeZone;

    public CalendarService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _centralTimeZone = TZConvert.GetTimeZoneInfo("Central Standard Time");
    }

    public async Task<List<CalendarEvent>> GetUpcomingEventsAsync(string icsUrl)
    {
        var icsContent = await _httpClient.GetStringAsync(icsUrl);
        var calendar = Ical.Net.Calendar.Load(icsContent);

        // our 5-day window in local Central time
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _centralTimeZone).Date;
        var lastDay = today.AddDays(5);

        var events = new List<CalendarEvent>();

        foreach (var vevent in calendar.Events)
        {
            // true date-only detection
            bool isAllDay = !vevent.DtStart.HasTime
                         && !vevent.DtEnd.HasTime;

            var occurrences = vevent.GetOccurrences(today, lastDay);
            foreach (var occ in occurrences)
            {
                if (isAllDay)
                {
                    // raw start date
                    var startDate = occ.Period.StartTime.Value.Date;
                    // dtEnd from ICS is exclusive → subtract 1 day to make it inclusive
                    var endInclusive = occ.Period.EndTime.Value.Date.AddDays(-1);

                    events.Add(new CalendarEvent
                    {
                        Subject = vevent.Summary,
                        Start = startDate.ToString("yyyy-MM-dd"),    // e.g. "2025-04-22"
                        End = endInclusive.ToString("yyyy-MM-dd"),  // now "2025-04-24"
                        IsAllDay = true
                    });
                }
                else
                {
                    // timed events: UTC→Central, full ISO local
                    var startUtc = occ.Period.StartTime.AsUtc;
                    var endUtc = occ.Period.EndTime.AsUtc;
                    var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, _centralTimeZone);
                    var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, _centralTimeZone);

                    events.Add(new CalendarEvent
                    {
                        Subject = vevent.Summary,
                        Start = localStart.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                        End = localEnd.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                        IsAllDay = false
                    });
                }
            }
        }

        // finally, sort by day → all-day first → clock time
        return events
          .Select(e => new
          {
              E = e,
              StartDt = DateTime.ParseExact(
                e.Start,
                e.IsAllDay
                  ? "yyyy-MM-dd"
                  : "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture
              )
          })
          .OrderBy(x => x.StartDt.Date)
          .ThenBy(x => x.E.IsAllDay ? 0 : 1)
          .ThenBy(x => x.StartDt.TimeOfDay)
          .Select(x => x.E)
          .ToList();
    }
}
