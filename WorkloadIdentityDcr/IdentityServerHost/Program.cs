// Copyright (c) Duende Software. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Configuration.EntityFramework;
using Duende.IdentityServer.Configuration.Validation.DynamicClientRegistration;
using Duende.IdentityServer.EntityFramework.Storage;
using IdentityServerHost;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// DCR is a licensed feature. Set your license key for both IdentityServer and the
// Configuration (DCR) services.
var licenseKey = builder.Configuration["Duende:LicenseKey"];
if (string.IsNullOrWhiteSpace(licenseKey))
{
    licenseKey = null;
}

// IdentityServer reads its clients, scopes, and resources from the Entity Framework
// configuration store, backed by SQLite in this sample.
builder.Services.AddIdentityServer(options =>
    {
        options.LicenseKey = licenseKey;
        options.EmitStaticAudienceClaim = true;
    })
    .AddConfigurationStore(options =>
    {
        options.ConfigureDbContext = b => b.UseSqlite(connectionString);
    });

// DCR writes newly registered clients into the same configuration store. Because both
// sides use the same ConfigurationDbContext (registered above by AddConfigurationStore),
// a client registered through DCR is visible when the workload requests a token.
builder.Services.AddIdentityServerConfiguration(options =>
    {
        options.LicenseKey = licenseKey;
    })
    .AddClientConfigurationStore();

// Protect the DCR endpoint by trusting Microsoft Entra ID directly. Only callers holding
// a valid Entra token reach the endpoint, and that token becomes context.Caller inside
// the validator.
var entra = builder.Configuration.GetSection("Entra");
var tenantId = entra["TenantId"]!;
var audience = entra["Audience"]!;

builder.Services.AddAuthentication()
    .AddJwtBearer("entra", options =>
    {
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.Audience = audience;
        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("dcr", policy =>
    {
        policy.AddAuthenticationSchemes("entra");
        policy.RequireAuthenticatedUser();
    });
});

// Which workloads may register, and what they may register for.
builder.Services.AddSingleton(new EntraWorkloadOptions
{
    AllowedTenantId = tenantId,
    AllowedObjectIds = entra.GetSection("AllowedObjectIds").Get<string[]>() ?? [],
    AllowedScopes = ["simple-api"]
});

builder.Services.AddTransient<IDynamicClientRegistrationValidator, EntraWorkloadDcrValidator>();

var app = builder.Build();

// Create the database and seed the API scope and resource on first run. In production you
// would use EF migrations and a deliberate seeding step instead.
await SeedData.EnsureSeedDataAsync(app);

app.UseIdentityServer();
app.UseAuthorization();

app.MapDynamicClientRegistration().RequireAuthorization("dcr");

app.Run();
