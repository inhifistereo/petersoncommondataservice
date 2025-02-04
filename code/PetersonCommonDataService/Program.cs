using Microsoft.Extensions.DependencyInjection;
using PetersonCommonDataService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddScoped<IToDoistService, ToDoistService>();

// Add configuration to read from environment variables
builder.Configuration.AddEnvironmentVariables();

var app = builder.Build();

app.MapControllers();

app.Run();
