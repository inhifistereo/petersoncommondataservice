using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Graph;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Abstractions.Authentication;
using PetersonCommonDataService.Services;

var builder = WebApplication.CreateBuilder(args);

// ✅ Load Configuration (appsettings.json & Environment Variables)
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// ✅ Read Environment Variables
var tenantId = Environment.GetEnvironmentVariable("AZURE_AD_TENANT_ID") 
    ?? throw new InvalidOperationException("AZURE_AD_TENANT_ID is missing.");
var clientId = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_ID") 
    ?? throw new InvalidOperationException("AZURE_AD_CLIENT_ID is missing.");
var clientSecret = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_SECRET") 
    ?? throw new InvalidOperationException("AZURE_AD_CLIENT_SECRET is missing.");
var userEmail = Environment.GetEnvironmentVariable("USER_EMAIL") 
    ?? throw new InvalidOperationException("USER_EMAIL is missing.");

// ✅ Debugging: Log Values (Make sure they exist)
Console.WriteLine($"🔹 ClientId: {clientId}");
Console.WriteLine($"🔹 TenantId: {tenantId}");
Console.WriteLine($"🔹 ClientSecret: {(string.IsNullOrEmpty(clientSecret) ? "MISSING" : "LOADED")}");
Console.WriteLine($"🔹 Outlook User Email: {userEmail}");

// ✅ Register HTTP Client
builder.Services.AddHttpClient();

// ✅ Register Microsoft Identity Web for Delegated Authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        options.Instance = "https://login.microsoftonline.com/"; // Correct Azure AD instance URL
        options.TenantId = tenantId; // Set TenantId separately
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
        options.CallbackPath = "/signin-oidc";
        options.SaveTokens = true;
    })
    .EnableTokenAcquisitionToCallDownstreamApi(new[] { "Calendars.Read" })
    .AddInMemoryTokenCaches();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// ✅ Register GraphServiceClient with **Delegated Authentication**
builder.Services.AddScoped<GraphServiceClient>(sp =>
{
    var tokenAcquisition = sp.GetRequiredService<ITokenAcquisition>();

    var authProvider = new BaseBearerTokenAuthenticationProvider(new TokenAcquisitionAuthenticationProvider(tokenAcquisition));

    return new GraphServiceClient(new HttpClientRequestAdapter(authProvider));
});

// ✅ Register Controllers and Razor Pages
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ✅ Register Services
builder.Services.AddScoped<IToDoistService, ToDoistService>();
builder.Services.AddScoped<CalendarService>();

// ✅ Register Session Services (Ensures Session Persistence)
builder.Services.AddDistributedMemoryCache();  // REQUIRED for sessions
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".Peterson.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.None; // ✅ Fix for Safari Private Mode
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseSession(); 

app.MapControllers();
app.MapRazorPages();

app.Run();

// ✅ Custom Authentication Provider for Microsoft Graph v5+
public class TokenAcquisitionAuthenticationProvider : IAccessTokenProvider
{
    private readonly ITokenAcquisition _tokenAcquisition;

    public TokenAcquisitionAuthenticationProvider(ITokenAcquisition tokenAcquisition)
    {
        _tokenAcquisition = tokenAcquisition;
    }

    public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object> additionalAuthenticationContext = default, CancellationToken cancellationToken = default)
    {
        return await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { "Calendars.Read" });
    }

    public AllowedHostsValidator AllowedHostsValidator => new();
}
