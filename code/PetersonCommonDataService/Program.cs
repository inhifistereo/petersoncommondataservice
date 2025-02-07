using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web.UI;
using PetersonCommonDataService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
builder.Services.AddRazorPages();
builder.Services.AddScoped<IToDoistService, ToDoistService>();

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Add configuration to read from environment variables
builder.Configuration.AddEnvironmentVariables();

// Configure Azure AD authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        options.Instance = builder.Configuration["AzureAd:Instance"] ?? string.Empty;
        options.Domain = builder.Configuration["AzureAd:Domain"];
        options.TenantId = Environment.GetEnvironmentVariable("AZURE_AD_TENANT_ID");
        options.ClientId = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_ID");
        options.ClientSecret = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_SECRET");
        options.CallbackPath = builder.Configuration["AzureAd:CallbackPath"];
    })
    .EnableTokenAcquisitionToCallDownstreamApi(new string[] { "https://graph.microsoft.com/.default" })
    .AddInMemoryTokenCaches();


var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();  
app.UseAuthorization();   

app.MapControllers();
app.MapRazorPages();

app.Run();

