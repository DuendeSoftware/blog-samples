using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace IdentityServer.Pages.Diagnostics;

public class ViewModel
{
    public ViewModel(AuthenticateResult result)
    {
        AuthenticateResult = result;

        if (result?.Properties?.Items.TryGetValue("client_list", out var encoded) == true)
        {
            if (encoded != null)
            {
                // Base64Url decode: replace URL-safe chars and add padding
                var base64 = encoded.Replace('-', '+').Replace('_', '/');
                var padding = base64.Length % 4;
                if (padding == 2) base64 += "==";
                else if (padding == 3) base64 += "=";
                var bytes = Convert.FromBase64String(base64);
                var value = Encoding.UTF8.GetString(bytes);
                Clients = JsonSerializer.Deserialize<string[]>(value) ?? Enumerable.Empty<string>();
                return;
            }
        }
        Clients = Enumerable.Empty<string>();
    }

    public AuthenticateResult AuthenticateResult { get; }
    public IEnumerable<string> Clients { get; }
}
