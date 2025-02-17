using PetersonCommonDataService.Services;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// ✅ Load Configuration (appsettings.json & Environment Variables)
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

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapRazorPages();

app.Run();
