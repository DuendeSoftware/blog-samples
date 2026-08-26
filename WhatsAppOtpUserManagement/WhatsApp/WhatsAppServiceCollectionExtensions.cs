using System.Net.Http.Headers;
using Duende.UserManagement.Authentication.Otp;

namespace WhatsAppOtpIdentityServer.WhatsApp;

public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>
    /// Registers the WhatsApp OTP dispatcher together with a typed HttpClient that
    /// is pre-configured with the Graph API base address and bearer access token.
    /// </summary>
    public static IServiceCollection AddWhatsAppOtpDispatcher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WhatsAppOptions>()
            .Bind(configuration.GetSection("WhatsApp"));

        services.AddHttpClient<WhatsAppOtpDispatcher>((sp, client) =>
        {
            var options = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<WhatsAppOptions>>().Value;

            client.BaseAddress = new Uri(options.GraphApiBaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.AccessToken);
        });

        // Register the dispatcher against the IOtpDispatcher abstraction so
        // User Management resolves it. It resolves the typed-client instance
        // registered above.
        services.AddTransient<IOtpDispatcher>(sp =>
            sp.GetRequiredService<WhatsAppOtpDispatcher>());

        return services;
    }
}
