using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using TokenSnake.Game;
using TokenSnake.IdentityServer;

var builder = WebApplication.CreateBuilder(args);

// URLs come from configuration, hosting somewhere else is a settings change.
var gameOrigin = (builder.Configuration["TokenSnake:GameOrigin"]
    ?? throw new InvalidOperationException("TokenSnake:GameOrigin is not configured.")).TrimEnd('/');

// See https://docs.duendesoftware.com/identityserver/deployment/#proxy-servers-and-load-balancers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// --- Game services ---------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(GameRules.Default);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IGameStateStore, InMemoryGameStateStore>();
builder.Services.AddSingleton<MoveValidator>();

// --- Nickname login --------------------------------------------------------------------
var blocklist = builder.Configuration.GetSection("TokenSnake:NicknameBlocklist").Get<string[]>() ?? [];
builder.Services.AddSingleton(new NicknameBlocklist(blocklist));
builder.Services.AddRazorPages();

// The token endpoint reads a form. Cap it: a move string never needs more than a few KB.
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = 8 * 1024;
    options.ValueCountLimit = 32;
});
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 32 * 1024);

// --- IdentityServer --------------------------------------------------------------------
builder.Services.AddIdentityServer(options =>
    {
        // The subject_token is a bearer token: keep it out of the logs.
        options.Logging.TokenRequestSensitiveValuesFilter.Add("subject_token");

        options.UserInteraction.LogoutUrl = "/Account/Logout"; // Pages/Account/Logout, prompt-free (see the page)

        // Optional. Without a key IdentityServer runs in trial mode. Set it through the environment
        // (IdentityServer__LicenseKey) or user secrets.
        var licenseKey = builder.Configuration["IdentityServer:LicenseKey"];
        if (!string.IsNullOrWhiteSpace(licenseKey)) options.LicenseKey = licenseKey;
    })
    .AddInMemoryIdentityResources(Config.IdentityResources)
    .AddInMemoryApiScopes(Config.ApiScopes)
    .AddInMemoryApiResources(Config.ApiResources)
    .AddInMemoryClients(Config.Clients(gameOrigin))
    .AddExtensionGrantValidator<SnakeEatGrantValidator>()
    .AddCustomTokenRequestValidator<SnakeTokenLifetimeValidator>()
    .AddProfileService<SnakeProfileService>();

var app = builder.Build();

app.UseForwardedHeaders(); // first, so the issuer and links use the public scheme and host

app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();   // IdentityServer applies the clients' AllowedCorsOrigins to its endpoints
app.MapRazorPages();

app.MapGet("/", () => "TokenSnake IdentityServer");

app.Run();

public partial class Program;
