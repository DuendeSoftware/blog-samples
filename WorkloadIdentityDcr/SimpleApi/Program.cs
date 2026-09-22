// Copyright (c) Duende Software. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Validate access tokens issued by our IdentityServer host. The dynamically
// registered workload calls this API with one of those tokens. The authority comes
// from configuration so the same build runs locally and in Azure App Service.
builder.Services.AddAuthentication("token")
    .AddJwtBearer("token", options =>
    {
        options.Authority = builder.Configuration["IdentityServer:Authority"];
        options.MapInboundClaims = false;

        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("simple-api", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "simple-api");
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/identity", (ClaimsPrincipal user) =>
        user.Claims.Select(c => new { c.Type, c.Value }))
    .RequireAuthorization("simple-api");

app.Run();
