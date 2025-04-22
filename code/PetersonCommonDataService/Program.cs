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

// ✅ Add CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy.WithOrigins("https://app.devin.ai", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ✅ Use CORS Middleware
app.UseCors("AllowSpecificOrigin");
app.UseRouting();

app.MapHealthChecks("/health");

app.MapControllers();
app.MapRazorPages();

app.Run();
