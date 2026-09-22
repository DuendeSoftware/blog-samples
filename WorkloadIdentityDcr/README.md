# Workload Identity to Client Identity with DCR

A sample that shows how an Azure workload with a Microsoft Entra ID managed identity
can register itself as an OAuth 2.0 client in Duende IdentityServer through Dynamic
Client Registration (DCR), without a human provisioning a `client_id` or copying a secret.

## What each project does

| Project | Port | Role |
| --- | --- | --- |
| `IdentityServerHost` | 5001 | Duende IdentityServer, hosts the DCR endpoint (protected by Microsoft Entra ID, with a custom validator), and issues tokens |
| `SimpleApi` | 5002 | An API that accepts IdentityServer access tokens |
| `WorkloadClient` | 5004 | The workload, a web app with a `GET /register-and-call` endpoint: gets an Entra token, registers via DCR, calls the API |

All three are web apps, so you can deploy them to Azure App Service and run the workload with
a real managed identity.

The DCR endpoint runs inside `IdentityServerHost` so IdentityServer and DCR share one
Entity Framework configuration store (SQLite) over the same `ConfigurationDbContext`. DCR
writes registered clients into it, and IdentityServer reads them from it at the token
endpoint. On first run the host creates the schema with `EnsureCreated` and seeds the API
scope and resource. In production, use EF migrations and your own database, which also lets
you host the DCR endpoint separately.

## The flow

1. `WorkloadClient` asks Microsoft Entra ID for a token using `DefaultAzureCredential`.
2. It calls `POST /connect/dcr` on `IdentityServerHost`, presenting the Entra token as the bearer.
3. IdentityServer validates that token against Entra. The token becomes `context.Caller`.
4. `EntraWorkloadDcrValidator` confirms the caller is a workload (`idtyp`), checks the tenant and the allow-listed object id, then shapes and creates the client.
5. `WorkloadClient` uses the returned `client_id` and `client_secret` to request an access token.
6. It calls `SimpleApi` with that access token.

## Prerequisites

- .NET 10 SDK
- A Duende IdentityServer license that includes DCR. A trial works.
- For a live run: an Azure workload with a managed identity, or an `az login` session for local development.

Before running, set these in `IdentityServerHost/appsettings.json`:

- `Entra:TenantId`: your Entra tenant (directory) id.
- `Entra:Audience`: the client id (bare GUID) of an app registration you create for the DCR
  endpoint. v2.0 tokens carry the client id in `aud`, so that is what IdentityServer validates.
  Give the registration an Application ID URI (for example `api://duende-dcr`), enable the
  `idtyp` optional claim, and create a service principal for it (`az ad sp create --id <client-id>`).
- `Entra:AllowedObjectIds`: the object id (`oid`) of the managed identity allowed to register.
- `Duende:LicenseKey`: your license key (leave empty to run in trial mode).

The workload requests a token for `<Application ID URI>/.default`. When it runs on Azure with a
user-assigned managed identity, set `AZURE_CLIENT_ID` to that identity's client id.

## Running

Set the real tenant id, audience, and allowed object ids in `IdentityServerHost/appsettings.json` first.

```bash
# Terminal 1
dotnet run --project IdentityServerHost

# Terminal 2
dotnet run --project SimpleApi

# Terminal 3
dotnet run --project WorkloadClient
```

Browse to `https://localhost:5004/register-and-call`. The workload registers itself through
DCR and returns the `client_id` and the API response as JSON.

## Deploy to Azure

To run the flow with a real managed identity, use the deploy script. It creates a dedicated
resource group with the managed identity, an app registration (with the `idtyp` optional
claim), an App Service plan, and the three web apps, then configures and deploys them.

```bash
# Optional: set your license key so DCR runs outside trial mode.
export DUENDE_LICENSE_KEY="<your-license-key>"

./deploy-to-azure.sh
```

The script prints the workload URL. Browse to `<workload-url>/register-and-call` to run the
flow. Clean up with `az group delete --name <resource-group> --yes --no-wait`.

Prerequisites: the Azure CLI (`az login`), the .NET SDK, and `zip`.

## Local development note

`DefaultAzureCredential` uses a managed identity when the code runs in Azure, and falls
back to your `az login` session when it runs on your machine. To test locally, run
`az login`, then request a token for the same audience the DCR endpoint expects.
