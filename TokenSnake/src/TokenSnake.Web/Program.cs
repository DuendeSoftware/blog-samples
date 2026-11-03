using Microsoft.AspNetCore.Authentication.JwtBearer;
using TokenSnake.Web;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["TokenSnake:Authority"]!.TrimEnd('/');
var gameOrigin = builder.Configuration["TokenSnake:GameOrigin"]!.TrimEnd('/');

// --- Authentication -------------------------------------------------------------------------
// Tokens are validated against IdentityServer's discovery document and signing keys (JWKS).
// ClockSkew: the leaderboard accepts a token that expired up to 2 minutes ago, because expiry is
// how most games end. ASP.NET Core applies skew per authentication scheme, so the leaderboard gets
// its own scheme with that skew. Nothing else in this host is authenticated.
builder.Services
    .AddAuthentication(LeaderboardApi.Scheme)
    .AddJwtBearer(LeaderboardApi.Scheme, options =>
    {
        options.Authority = authority;
        options.MapInboundClaims = false; // keep "sub", "scope" and "game_id" as they are in the JWT
        options.TokenValidationParameters.ValidAudience = "snake.api";
        options.TokenValidationParameters.ValidIssuer = authority;
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(2);
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(LeaderboardApi.Policy, policy => policy
        .AddAuthenticationSchemes(LeaderboardApi.Scheme)
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => ctx.User.HasClaim("scope", "snake.play") && ctx.User.HasClaim(c => c.Type == "game_id")));

builder.Services.AddSingleton<LeaderboardStore>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// A strict CSP: scripts and styles only from our own origin, and fetch() only to us and IdentityServer.
var csp = $"default-src 'self'; connect-src 'self' {authority}; script-src 'self'; " +
          "object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
app.Use((context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = csp;
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// Runtime configuration for auth.js, so the frontend has no hard-coded URLs.
app.MapGet("/config.json", () => new
{
    authority,
    client_id = "snake.web",
    redirect_uri = $"{gameOrigin}/",
    scope = "openid profile snake.play",
    authorization_endpoint = $"{authority}/connect/authorize",
    token_endpoint = $"{authority}/connect/token",
    end_session_endpoint = $"{authority}/connect/endsession",
    post_logout_redirect_uri = $"{gameOrigin}/",
});

LeaderboardApi.Map(app, app.Services.GetRequiredService<TimeProvider>());

app.Run();

public partial class Program;

/// <summary>Marker for WebApplicationFactory: the IdentityServer host also has a Program class.</summary>
public sealed class WebMarker;
