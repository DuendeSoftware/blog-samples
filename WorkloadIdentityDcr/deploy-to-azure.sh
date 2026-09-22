#!/usr/bin/env bash
#
# Deploys the Workload Identity to Client Identity with DCR sample to Azure App Service,
# in a dedicated resource group, so you can run the flow with a real managed identity.
#
# What it creates:
#   - A resource group (everything lives here, delete it to clean up)
#   - A user-assigned managed identity for the workload
#   - An Entra app registration that represents the DCR audience (with the idtyp optional claim)
#   - Three Linux App Service apps on one plan: IdentityServer, SimpleApi, WorkloadClient
#
# After it runs, browse to the printed /register-and-call URL.
#
# Prerequisites: az CLI (logged in via `az login`), .NET 10 SDK, zip.
# The account needs permission to create resources and an Entra app registration.
#
# Clean up with:
#   az group delete --name <resource-group> --yes --no-wait
#
set -euo pipefail

# ----------------------------------------------------------------------------
# Configuration. Override any of these with environment variables if you like.
# ----------------------------------------------------------------------------
PREFIX="${PREFIX:-dcrdemo$RANDOM}"
LOCATION="${LOCATION:-westeurope}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-${PREFIX}}"
PLAN="${PLAN:-plan-${PREFIX}}"
IDENTITY_NAME="${IDENTITY_NAME:-id-${PREFIX}-workload}"
LICENSE_KEY="${DUENDE_LICENSE_KEY:-}"

# App names must be globally unique on azurewebsites.net.
IS_APP="${PREFIX}-identityserver"
API_APP="${PREFIX}-simpleapi"
WORKLOAD_APP="${PREFIX}-workload"

# Resolve the sample directory (this script lives in it).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SAMPLE_DIR="${SAMPLE_DIR:-${SCRIPT_DIR}}"

echo "Resource group : ${RESOURCE_GROUP}"
echo "Location       : ${LOCATION}"
echo "Sample dir     : ${SAMPLE_DIR}"
echo

# ----------------------------------------------------------------------------
# 1. Resource group
# ----------------------------------------------------------------------------
echo "==> Creating resource group"
az group create --name "${RESOURCE_GROUP}" --location "${LOCATION}" --output none

# ----------------------------------------------------------------------------
# 2. User-assigned managed identity for the workload
# ----------------------------------------------------------------------------
echo "==> Creating managed identity"
az identity create \
  --name "${IDENTITY_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --location "${LOCATION}" \
  --output none

IDENTITY_ID="$(az identity show --name "${IDENTITY_NAME}" --resource-group "${RESOURCE_GROUP}" --query id -o tsv)"
IDENTITY_PRINCIPAL_ID="$(az identity show --name "${IDENTITY_NAME}" --resource-group "${RESOURCE_GROUP}" --query principalId -o tsv)"
IDENTITY_CLIENT_ID="$(az identity show --name "${IDENTITY_NAME}" --resource-group "${RESOURCE_GROUP}" --query clientId -o tsv)"
TENANT_ID="$(az account show --query tenantId -o tsv)"

echo "    Managed identity object id (oid): ${IDENTITY_PRINCIPAL_ID}"

# ----------------------------------------------------------------------------
# 3. App registration for the DCR audience, with the idtyp optional claim
# ----------------------------------------------------------------------------
echo "==> Creating app registration for the DCR audience"
# Create the app first, then derive an identifier URI of the form api://{appId}. That form
# always satisfies tenant policies that require a verified domain, tenant id, or app id.
APP_ID="$(az ad app create --display-name "${PREFIX} DCR endpoint" --query appId -o tsv)"
APP_OBJECT_ID="$(az ad app show --id "${APP_ID}" --query id -o tsv)"
APP_ID_URI="api://${APP_ID}"

echo "==> Setting the identifier URI and v2 access tokens"
az ad app update --id "${APP_OBJECT_ID}" \
  --identifier-uris "${APP_ID_URI}" \
  --set api='{"requestedAccessTokenVersion":2}'

echo "==> Enabling the idtyp optional claim on the access token"
az ad app update --id "${APP_OBJECT_ID}" \
  --set optionalClaims='{"accessToken":[{"name":"idtyp","essential":false,"additionalProperties":[]}],"idToken":[],"saml2Token":[]}'

echo "==> Creating the service principal for the app"
# Entra only issues tokens for an audience that has a service principal in the tenant.
# `az ad app create` makes the application object but not the service principal, so create it.
az ad sp create --id "${APP_ID}" --output none 2>/dev/null || true

# ----------------------------------------------------------------------------
# 4. App Service plan and three web apps
# ----------------------------------------------------------------------------
echo "==> Creating App Service plan"
az appservice plan create \
  --name "${PLAN}" \
  --resource-group "${RESOURCE_GROUP}" \
  --location "${LOCATION}" \
  --sku B1 \
  --is-linux \
  --output none

for app in "${IS_APP}" "${API_APP}" "${WORKLOAD_APP}"; do
  echo "==> Creating web app: ${app}"
  az webapp create \
    --name "${app}" \
    --resource-group "${RESOURCE_GROUP}" \
    --plan "${PLAN}" \
    --runtime "DOTNETCORE:10.0" \
    --output none
done

IS_URL="https://${IS_APP}.azurewebsites.net"
API_URL="https://${API_APP}.azurewebsites.net"
WORKLOAD_URL="https://${WORKLOAD_APP}.azurewebsites.net"

# ----------------------------------------------------------------------------
# 5. Assign the managed identity to the workload app
# ----------------------------------------------------------------------------
echo "==> Assigning the managed identity to the workload app"
az webapp identity assign \
  --name "${WORKLOAD_APP}" \
  --resource-group "${RESOURCE_GROUP}" \
  --identities "${IDENTITY_ID}" \
  --output none

# ----------------------------------------------------------------------------
# 6. App settings (now that URLs and the identity's oid exist)
# ----------------------------------------------------------------------------
echo "==> Configuring IdentityServer app settings"
# Entra v2.0 access tokens carry the bare application (client) id in the aud claim, not the
# api:// identifier URI. IdentityServer validates against that bare id.
az webapp config appsettings set --name "${IS_APP}" --resource-group "${RESOURCE_GROUP}" --output none --settings \
  "ConnectionStrings__DefaultConnection=Data Source=/home/IdentityServer.db" \
  "Duende__LicenseKey=${LICENSE_KEY}" \
  "Entra__TenantId=${TENANT_ID}" \
  "Entra__Audience=${APP_ID}" \
  "Entra__AllowedObjectIds__0=${IDENTITY_PRINCIPAL_ID}"

echo "==> Configuring SimpleApi app settings"
az webapp config appsettings set --name "${API_APP}" --resource-group "${RESOURCE_GROUP}" --output none --settings \
  "IdentityServer__Authority=${IS_URL}"

echo "==> Configuring Workload app settings"
# AZURE_CLIENT_ID tells DefaultAzureCredential which user-assigned managed identity to use.
# The workload requests a token for the api:// audience with the /.default scope.
az webapp config appsettings set --name "${WORKLOAD_APP}" --resource-group "${RESOURCE_GROUP}" --output none --settings \
  "AZURE_CLIENT_ID=${IDENTITY_CLIENT_ID}" \
  "Workload__IdentityServer=${IS_URL}" \
  "Workload__SimpleApi=${API_URL}" \
  "Workload__DcrAudience=${APP_ID_URI}/.default"

# ----------------------------------------------------------------------------
# 7. Publish and deploy each project
# ----------------------------------------------------------------------------
deploy() {
  local project="$1" app="$2"
  echo "==> Publishing and deploying ${project}"
  local out zip
  out="$(mktemp -d)"
  zip="$(mktemp -u).zip"
  dotnet publish "${SAMPLE_DIR}/${project}/${project}.csproj" -c Release -o "${out}" >/dev/null
  (cd "${out}" && zip -r -q "${zip}" .)
  az webapp deploy \
    --name "${app}" \
    --resource-group "${RESOURCE_GROUP}" \
    --src-path "${zip}" \
    --type zip \
    --output none
  rm -rf "${out}" "${zip}"
}

deploy "IdentityServerHost" "${IS_APP}"
deploy "SimpleApi" "${API_APP}"
deploy "WorkloadClient" "${WORKLOAD_APP}"

# ----------------------------------------------------------------------------
# Done
# ----------------------------------------------------------------------------
cat <<EOF

Done.

IdentityServer : ${IS_URL}
SimpleApi      : ${API_URL}
Workload       : ${WORKLOAD_URL}

Run the flow (allow a minute for the apps to warm up):

  curl ${WORKLOAD_URL}/register-and-call

or open ${WORKLOAD_URL}/register-and-call in a browser.

Clean up everything with:

  az group delete --name ${RESOURCE_GROUP} --yes --no-wait
EOF
