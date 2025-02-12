using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Graph;
using PetersonCommonDataService.Services;
using System.Net.Http.Headers;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

// ✅ Load Configuration (appsettings.json & Environment Variables)
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// ✅ Register HTTP Client
builder.Services.AddHttpClient();

// ✅ 1️⃣ Register Azure AD Authentication FIRST
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        options.SaveTokens = true; 
        options.Prompt = "select_account";

        options.Events.OnRemoteFailure = context =>
        {
            var errorMessage = context.Failure?.Message ?? "Unknown error";
            context.Response.Redirect("/error?message=" + errorMessage);
            context.HandleResponse();
            return Task.CompletedTask;
        };
    })
    .EnableTokenAcquisitionToCallDownstreamApi(new[] { "User.Read", "Calendars.Read" })
    .AddInMemoryTokenCaches();

// ✅ 2️⃣ Register GraphServiceClient with Delegated Access (AFTER Azure AD Auth)
builder.Services.AddScoped<GraphServiceClient>(sp =>
{
    var tokenAcquisition = sp.GetRequiredService<ITokenAcquisition>();

    return new GraphServiceClient(new DelegateAuthenticationProvider(async (requestMessage) =>
    {
        // ✅ Get access token for the authenticated user
        string accessToken = await tokenAcquisition.GetAccessTokenForUserAsync(new[] { "User.Read", "Calendars.Read" });

        // ✅ Attach the token to Graph API requests
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }));
});

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();
