// Copyright (c) Duende Software. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerHost;

public static class SeedData
{
    public static async Task EnsureSeedDataAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();

        // Create the schema if it does not exist. This sample uses EnsureCreated to avoid
        // an EF migrations project. Production hosts should use migrations.
        await context.Database.EnsureCreatedAsync();

        if (!await context.ApiScopes.AnyAsync())
        {
            foreach (var scopeModel in Config.ApiScopes)
            {
                context.ApiScopes.Add(scopeModel.ToEntity());
            }
        }

        if (!await context.ApiResources.AnyAsync())
        {
            foreach (var resource in Config.ApiResources)
            {
                context.ApiResources.Add(resource.ToEntity());
            }
        }

        await context.SaveChangesAsync();
    }
}
