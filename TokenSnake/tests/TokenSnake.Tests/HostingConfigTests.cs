using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TokenSnake.IdentityServer;
using TokenSnake.Web;
using Xunit;

namespace TokenSnake.Tests;

/// <summary>Hosting behind a reverse proxy: configuration-driven URLs and forwarded headers.</summary>
public class HostingConfigTests
{
    private const string NewAuthority = "https://id.example.test";
    private const string NewGame = "https://play.example.test";

    private static WebApplicationFactory<SnakeEatGrantValidator> Identity(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<SnakeEatGrantValidator>().WithWebHostBuilder(b =>
        {
            b.UseSetting("TokenSnake:GameOrigin", "https://localhost:5002");
            configure?.Invoke(b);
        });

    private static WebApplicationFactory<WebMarker> Web(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<WebMarker>().WithWebHostBuilder(b => configure?.Invoke(b));

    [Fact]
    public async Task Web_config_json_and_csp_follow_configuration()
    {
        using var f = Web(b =>
        {
            b.UseSetting("TokenSnake:Authority", NewAuthority + "/");
            b.UseSetting("TokenSnake:GameOrigin", NewGame);
        });
        var http = f.CreateClient();
        var res = await http.GetAsync("/config.json", T.Ct);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync(T.Ct)).RootElement;
        Assert.Equal(NewAuthority, json.GetProperty("authority").GetString());
        Assert.Equal(NewGame + "/", json.GetProperty("redirect_uri").GetString());
        Assert.Equal(NewAuthority + "/connect/token", json.GetProperty("token_endpoint").GetString());
        Assert.Equal(NewAuthority + "/connect/endsession", json.GetProperty("end_session_endpoint").GetString());
        Assert.Equal(NewGame + "/", json.GetProperty("post_logout_redirect_uri").GetString());
        var csp = string.Join(";", res.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("connect-src 'self' " + NewAuthority + ";", csp);
        Assert.DoesNotContain("example.invalid", csp);
    }

    [Fact]
    public async Task Identity_redirect_uri_and_cors_follow_game_origin()
    {
        using var f = Identity(b => b.UseSetting("TokenSnake:GameOrigin", NewGame));
        var http = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        string Authorize(string redirect) =>
            "/connect/authorize?client_id=snake.web&response_type=code&scope=openid&state=x" +
            "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256" +
            "&redirect_uri=" + Uri.EscapeDataString(redirect);

        // New origin's redirect URI: goes on to the login page. Old origin's: rejected, no redirect to login.
        var ok = await http.GetAsync(Authorize(NewGame + "/"), T.Ct);
        Assert.Contains("/Account/Login", ok.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        var old = await http.GetAsync(Authorize("https://localhost:5002/"), T.Ct);
        Assert.DoesNotContain("/Account/Login", old.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        async Task<string?> Cors(string origin)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
            req.Headers.Add("Origin", origin);
            var r = await http.SendAsync(req, T.Ct);
            return r.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.Single() : null;
        }
        Assert.Equal(NewGame, await Cors(NewGame));
        Assert.Null(await Cors("https://localhost:5002"));
        Assert.Null(await Cors("https://evil.example"));
    }

    [Fact]
    public async Task Forwarded_proto_and_host_make_discovery_issuer_public_https()
    {
        using var f = Identity();
        var http = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        req.Headers.Add("X-Forwarded-Proto", "https");
        req.Headers.Add("X-Forwarded-Host", "id.example.test");
        var doc = JsonDocument.Parse(await (await http.SendAsync(req, T.Ct)).Content.ReadAsStringAsync(T.Ct)).RootElement;
        Assert.Equal("https://id.example.test", doc.GetProperty("issuer").GetString());
        Assert.StartsWith("https://id.example.test/connect/", doc.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public async Task License_key_is_optional_and_applied_when_configured()
    {
        using var without = Identity();
        Assert.Equal(HttpStatusCode.OK, (await without.CreateClient().GetAsync("/", T.Ct)).StatusCode);

        using var with = Identity(b => b.UseSetting("IdentityServer:LicenseKey", "not-a-real-key"));
        Assert.Equal(HttpStatusCode.OK, (await with.CreateClient().GetAsync("/", T.Ct)).StatusCode);
        var options = with.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Duende.IdentityServer.Configuration.IdentityServerOptions>>();
        Assert.Equal("not-a-real-key", options.Value.LicenseKey);
    }
}
