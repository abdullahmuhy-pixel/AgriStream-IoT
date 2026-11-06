#!/usr/bin/env bash
# AgriStream IoT - one-shot Azure provisioning + deployment with the Azure CLI.
# Run in Azure Cloud Shell (Bash) or any shell where `az login` has been done and the .NET 8 SDK is installed.
# Everything can also be created by hand in the portal - see README.md for the click-by-click version.
#
# NOTE: this script is policy-tolerant. Some student/institutional Azure subscriptions block
# Standard/Premium App Service SKUs and the creation of Log Analytics / Application Insights
# resources. Where that happens the script logs it, falls back to a working configuration
# (Basic B3 App Service plan, monitoring disabled) and keeps going instead of aborting.
# See README.md Task (g) for what this means for auto-scaling evidence.
set -euo pipefail

# Usage: bash deploy.sh <student-number> [region]
STUDENT=$(echo "${1:?Usage: bash deploy.sh <student-number> [region]}" | tr 'A-Z' 'a-z')
LOC="${2:-southafricanorth}"          # use any region your subscription allows
SQLADMIN="agriadmin"
# Random strong SQL password, generated here and saved to ~/agristream-sql-credentials.txt
SQLPASS="Ag1!$(openssl rand -hex 12)"
umask 077
printf 'SQL server admin: %s\nSQL password: %s\n' "$SQLADMIN" "$SQLPASS" > ~/agristream-sql-credentials.txt

RG="${RG:-rg-agristream}"           # override with: RG=<existing-group> bash deploy.sh ...
SUFFIX=$(echo "$STUDENT" | tr -cd 'a-z0-9' | tail -c 8)
STORAGE="agristream${SUFFIX}"          # 3-24 lowercase letters/digits, globally unique
SQLSERVER="agristream-sql-${SUFFIX}"
DB="agristreamdb"
PLAN="asp-agristream"
WEBAPP="${STUDENT}-agristream"
FUNC="agristream-func-${SUFFIX}"
LAW="law-agristream"
AI="ai-agristream"

echo ">> Resource group"
if [ "$(az group exists -n "$RG")" = "true" ]; then
  echo "   using existing resource group $RG"
else
  az group create -n "$RG" -l "$LOC" -o none
fi

echo ">> Storage account (Blob + Table + Queue)"
az storage account create -g "$RG" -n "$STORAGE" -l "$LOC" --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 -o none
STORAGE_CONN=$(az storage account show-connection-string -g "$RG" -n "$STORAGE" --query connectionString -o tsv)
az storage container create --name drone-images --connection-string "$STORAGE_CONN" -o none
az storage table create --name Telemetry --connection-string "$STORAGE_CONN" -o none
az storage table create --name ImageAudit --connection-string "$STORAGE_CONN" -o none
az storage queue create --name irrigation-commands --connection-string "$STORAGE_CONN" -o none

echo ">> Azure SQL server + database"
az sql server create -g "$RG" -n "$SQLSERVER" -l "$LOC" -u "$SQLADMIN" -p "$SQLPASS" -o none
# --backup-storage-redundancy Local is required by some subscriptions' Azure Policy ("Ensure the Backup is to LRS").
az sql db create -g "$RG" -s "$SQLSERVER" -n "$DB" --service-objective Basic --backup-storage-redundancy Local -o none
az sql server firewall-rule create -g "$RG" -s "$SQLSERVER" -n AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none
SQL_CONN="Server=tcp:${SQLSERVER}.database.windows.net,1433;Initial Catalog=${DB};User ID=${SQLADMIN};Password=${SQLPASS};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

echo ">> Log Analytics + Application Insights (best-effort - some subscriptions block these resource types)"
AI_CONN=""
set +e
az extension add --name application-insights --upgrade -o none 2>/dev/null
az monitor log-analytics workspace create -g "$RG" -n "$LAW" -l "$LOC" -o none 2>/tmp/law_err.log
if [ $? -ne 0 ]; then
  echo "   Log Analytics workspace creation failed (likely blocked by Azure Policy on this subscription) - continuing without it."
  cat /tmp/law_err.log
  LAW_ID=""
else
  LAW_ID=$(az monitor log-analytics workspace show -g "$RG" -n "$LAW" --query id -o tsv)
fi
if [ -n "$LAW_ID" ]; then
  az monitor app-insights component create -g "$RG" --app "$AI" -l "$LOC" --workspace "$LAW_ID" -o none 2>/tmp/ai_err.log
  if [ $? -ne 0 ]; then
    echo "   Application Insights creation failed (subscription not registered for microsoft.insights, or blocked) - continuing without it."
    cat /tmp/ai_err.log
  else
    AI_CONN=$(az monitor app-insights component show -g "$RG" --app "$AI" --query connectionString -o tsv)
  fi
fi
set -e

echo ">> Function App (Consumption, .NET 8 isolated)"
if [ -n "$AI_CONN" ]; then
  az functionapp create -g "$RG" -n "$FUNC" --storage-account "$STORAGE" \
    --consumption-plan-location "$LOC" --os-type Windows \
    --runtime dotnet-isolated --runtime-version 8 --functions-version 4 \
    --app-insights "$AI" -o none
else
  az functionapp create -g "$RG" -n "$FUNC" --storage-account "$STORAGE" \
    --consumption-plan-location "$LOC" --os-type Windows \
    --runtime dotnet-isolated --runtime-version 8 --functions-version 4 \
    --disable-app-insights -o none
fi
az functionapp config appsettings set -g "$RG" -n "$FUNC" -o none --settings \
  "AgriStorage=$STORAGE_CONN" "SqlConnectionString=$SQL_CONN"

echo ">> App Service plan + Web App"
echo "   Trying Standard S1 first (lowest tier that supports rules-based auto-scale)..."
set +e
az appservice plan create -g "$RG" -n "$PLAN" -l "$LOC" --sku S1 --is-linux false -o none 2>/tmp/plan_err.log
PLAN_OK=$?
set -e
PLAN_SKU="S1"
if [ $PLAN_OK -ne 0 ]; then
  echo "   S1 was rejected (likely Azure Policy 'SKU not allowed' on this subscription):"
  cat /tmp/plan_err.log
  echo "   Falling back to Basic B3 (highest Basic tier - up to 3 instances, but no rules-based autoscale)."
  echo "   See README.md Task (g) for how to demonstrate scale-out manually within this constraint."
  az appservice plan create -g "$RG" -n "$PLAN" -l "$LOC" --sku B3 --is-linux false -o none
  PLAN_SKU="B3"
fi
az webapp create -g "$RG" -p "$PLAN" -n "$WEBAPP" --runtime "dotnet:8" -o none
az webapp update -g "$RG" -n "$WEBAPP" --https-only true --client-affinity-enabled false -o none
az webapp config set -g "$RG" -n "$WEBAPP" --always-on true -o none

echo ">> Deploy the Functions project"
pushd ../AgriStream.Functions >/dev/null
dotnet publish -c Release -o ./publish
(cd publish && zip -qr ../functions.zip .)
az functionapp deployment source config-zip -g "$RG" -n "$FUNC" --src functions.zip -o none
popd >/dev/null

FUNC_KEY=$(az functionapp keys list -g "$RG" -n "$FUNC" --query functionKeys.default -o tsv)

echo ">> Web App settings"
az webapp config connection-string set -g "$RG" -n "$WEBAPP" -t SQLAzure -o none --settings "AgriStreamDb=$SQL_CONN"
az webapp config connection-string set -g "$RG" -n "$WEBAPP" -t Custom -o none --settings "StorageAccount=$STORAGE_CONN"
if [ -n "$AI_CONN" ]; then
  az webapp config appsettings set -g "$RG" -n "$WEBAPP" -o none --settings \
    "Functions__BaseUrl=https://${FUNC}.azurewebsites.net/api/" \
    "Functions__Key=$FUNC_KEY" \
    "APPLICATIONINSIGHTS_CONNECTION_STRING=$AI_CONN"
else
  az webapp config appsettings set -g "$RG" -n "$WEBAPP" -o none --settings \
    "Functions__BaseUrl=https://${FUNC}.azurewebsites.net/api/" \
    "Functions__Key=$FUNC_KEY"
fi

echo ">> Deploy the Web App"
pushd ../AgriStream.Web >/dev/null
dotnet publish -c Release -o ./publish
(cd publish && zip -qr ../web.zip .)
az webapp deploy -g "$RG" -n "$WEBAPP" --src-path web.zip --type zip -o none
popd >/dev/null

echo ">> Auto-scale rules on the App Service plan (Task g)"
if [ "$PLAN_SKU" = "S1" ] || [ "$PLAN_SKU" = "S2" ] || [ "$PLAN_SKU" = "S3" ]; then
  PLAN_ID=$(az appservice plan show -g "$RG" -n "$PLAN" --query id -o tsv)
  az monitor autoscale create -g "$RG" --resource "$PLAN_ID" -n agristream-autoscale \
    --min-count 1 --max-count 3 --count 1 -o none
  az monitor autoscale rule create -g "$RG" --autoscale-name agristream-autoscale \
    --condition "CpuPercentage > 70 avg 5m" --scale out 1 --cooldown 5 -o none
  az monitor autoscale rule create -g "$RG" --autoscale-name agristream-autoscale \
    --condition "MemoryPercentage > 80 avg 5m" --scale out 1 --cooldown 5 -o none
  az monitor autoscale rule create -g "$RG" --autoscale-name agristream-autoscale \
    --condition "CpuPercentage < 30 avg 10m" --scale in 1 --cooldown 10 -o none
else
  echo "   Skipped: rules-based autoscale needs Standard tier or above (this plan is $PLAN_SKU)."
  echo "   During the load test, scale out/in manually instead: App Service plan > Scale out (App Service plan) >"
  echo "   set the instance count by hand as CPU rises, then back down afterwards. See README.md Task (g)."
fi

echo
echo "Done."
echo "App Service plan SKU: $PLAN_SKU"
echo "Web app : https://${WEBAPP}.azurewebsites.net   (log in as admin / AgriStream@2026)"
echo "Funcs   : https://${FUNC}.azurewebsites.net/api/"
echo "Schema  : the web app created the SQL tables and seeded users/devices on first start."
echo "          (Do NOT run sql/schema.sql now - it is only for the manual route, before the first deploy.)"
echo "Next    : python ../loadtest/loadtest.py --url https://${WEBAPP}.azurewebsites.net --users 60 --minutes 20"
