using Duende.IdentityServer;
using Duende.Storage.Schema;
using Duende.Storage.Sqlite;
using WhatsAppOtpIdentityServer.WhatsApp;
using Microsoft.AspNetCore.DataProtection;
using Duende.UserManagement;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services
    .AddIdentityServer(options =>
    {
        options.UserInteraction.LoginUrl = "/Account/Login";
        options.UserInteraction.LogoutUrl = "/Account/Logout";
        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseInformationEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
    })
    .AddInMemoryIdentityResources(Config.IdentityResources)
    .AddInMemoryApiScopes(Config.ApiScopes)
    .AddInMemoryClients(Config.Clients)
    .AddUserManagement(options =>
    {
        options.AddSqliteStore(o =>
        {
            o.ConnectionString = "Data Source=usermanagement.db";
        });
    });

// Register the custom WhatsApp OTP dispatcher (typed HttpClient + IOtpDispatcher).
builder.Services.AddWhatsAppOtpDispatcher(builder.Configuration);

builder.Services.AddDataProtection()
    .SetApplicationName("WhatsAppOtpIdentityServer");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider
        .GetRequiredService<IDatabaseSchema>()
        .MigrateAsync(CancellationToken.None);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();

app.Run();
