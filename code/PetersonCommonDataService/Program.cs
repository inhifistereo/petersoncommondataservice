using PetersonCommonDataService.Services;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://*:8080");

// Configure the application configuration
builder.Configuration.Sources.Clear();
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
// ✅ Register HTTP Client
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<CalendarService>();
builder.Services.AddHttpContextAccessor();

// ✅ Register Controllers and Razor Pages
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ✅ Register Services
builder.Services.AddScoped<IToDoistService, ToDoistService>(); // ToDoist Service
builder.Services.AddScoped<CalendarService>(); // Calendar Service
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseRouting();

app.MapHealthChecks("/health");

app.MapControllers();
app.MapRazorPages();

app.Run();
