# AgriStream IoT – Real-Time Processing System

ASP.NET Core 8 MVC web app + four .NET 8 isolated Azure Functions, using Azure App Service, Azure SQL Database,
Azure Blob / Table / Queue Storage, Application Insights and App Service auto-scale.

```
AgriStream/
├── AgriStream.sln
├── AgriStream.Web/            MVC app (login + roles, field dashboard, drone uploads, user admin)
├── AgriStream.Functions/      BlobUploadAudit, GetTelemetry, TriggerIrrigation, ProcessIrrigationCommand
├── sql/schema.sql             Users / Roles / Devices / IrrigationLog tables + seed data
├── loadtest/loadtest.py       Load-test script (Python standard library only)
└── infra/deploy.sh            Azure CLI script that builds and deploys everything
```

Demo logins (seeded automatically): `admin`, `farmer`, `agronomist` – password `AgriStream@2026`.

---

## How the pieces fit

| Flow | Path |
|---|---|
| Login | MVC → Azure SQL `Users` + `Roles` (hashed passwords, role claims in an auth cookie) |
| Drone image | MVC upload → Blob `drone-images` → **Blob trigger** → Table `ImageAudit` (output binding) |
| Dashboard | MVC → **HTTP GET** `/api/telemetry/{fieldId}` → Table `Telemetry` (input binding) |
| Sensor burst | MVC "Simulate sensor burst" → Table `Telemetry` (direct); dry soil (< 20 %) → automatic irrigation |
| Irrigation | "Trigger Irrigation" → **HTTP POST** `/api/irrigation` → Queue `irrigation-commands` (output binding) → **Queue trigger** → SQL `Devices` + `IrrigationLog` |

---

## Route 1 – Azure CLI (fastest)

1. Open **Azure Cloud Shell (Bash)** in the portal, upload the zipped project and unzip it.
2. No edits needed: the script takes your student number and generates the SQL password (saved to `~/agristream-sql-credentials.txt`). It is policy-tolerant: if your subscription blocks Standard/Premium App Service SKUs or Log Analytics/Application Insights creation, it logs that and falls back automatically (Basic B3 plan, monitoring disabled) instead of aborting.
3. `cd AgriStream/infra && bash deploy.sh <student-number>`
4. Open `https://<student-number>-agristream.azurewebsites.net`, log in as `admin`.

The web app creates the SQL tables and seeds users/devices on first start.

## Route 2 – Portal, step by step (best for screenshots)

Do the steps in this order and take the screenshot listed under each one.

**1. Resource group** – `rg-agristream`.

**2. Storage account** – Standard, StorageV2, LRS (see Section B for why).
Create container `drone-images` (private), tables `Telemetry` and `ImageAudit`, queue `irrigation-commands`.
Copy *Access keys → Connection string*.
📸 Overview page; Containers; Tables; Queues.

**3. Azure SQL** – SQL server (SQL authentication) + database `agristreamdb` (Basic tier is enough).
Networking → *Allow Azure services and resources to access this server* = **Yes**, and add your client IP.
Open **Query editor**, paste `sql/schema.sql`, run it.
📸 Database overview; Query editor after running the script; `SELECT` of Users/Roles after first login.

**4. Application Insights** – workspace-based, same region. Copy the connection string.
*If your subscription blocks creating a Log Analytics workspace or registering `microsoft.insights`*
(common on institutional/student subscriptions), skip this step – deploy the Function App and Web App
without an Application Insights resource (step 5 has a fallback for this) and use the Azure Storage-level
monitoring in Task (f) instead (queue message counts, function execution counts, App Service plan Metrics).

**5. Function App** – Runtime stack **.NET 8 Isolated**, Windows, **Consumption** plan, link the App Insights resource (or tick **Disable Application Insights** if step 4 was skipped).
Environment variables → add `AgriStorage` (storage connection string) and `SqlConnectionString`.
Publish from Visual Studio (right-click `AgriStream.Functions` → Publish → Azure → Function App).
Copy *App keys → default host key*.
📸 Function App overview; the list of 4 functions; each function's *Invocations* tab after use.

> If the portal only offers **Flex Consumption**, host the functions on the S1 App Service plan from step 6
> instead (Always On enabled) – the polling blob trigger then works without extra Event Grid setup.

**6. App Service** – Name = your student number, .NET 8, Windows, new plan **Standard S1**
(Basic tiers cannot auto-scale). **If your subscription's Azure Policy rejects S1/S2/S3/Premium plans**
("SKU not allowed" / RequestDisallowedByPolicy – this is common on institutional student subscriptions),
create a **Basic B3** plan instead. B3 still allows up to 3 instances, but only manual scaling; see
step 8 below for how to demonstrate scale-out within that constraint. Configuration:
* Connection strings: `AgriStreamDb` (SQLAzure), `StorageAccount` (Custom)
* App settings: `Functions__BaseUrl` = `https://<func-app>.azurewebsites.net/api/`, `Functions__Key`, `APPLICATIONINSIGHTS_CONNECTION_STRING`
* General settings: **Session affinity (ARR affinity) = Off**, HTTPS only = On, Always On = On

Publish from Visual Studio (right-click `AgriStream.Web` → Publish → Azure App Service).
📸 App Service overview with URL; Configuration page (blur keys/passwords); site home page in the browser.

**7. Use the system once (evidence for every function)**
1. Log in as `admin` → Admin → add a user. 📸 user list + SQL query result.
2. Log in as `agronomist` → Dashboard → *Simulate sensor burst* (2–3 times). 📸 dashboard with chart + table.
3. Click *Trigger Irrigation* on a valve, refresh after ~5 s – status becomes **Irrigating**. 📸 before/after, plus `SELECT * FROM Devices` and `IrrigationLog`.
4. Media → upload a JPG, refresh – audit row shows **Validated**. 📸 media page, Blob container, `ImageAudit` table in Storage browser.
5. Storage browser → `Telemetry` table rows. 📸

**8. Auto-scale (Task g)**

*If you are on Standard tier or above:* App Service plan → *Scale out (App Service plan)* → **Rules Based** → *Manage rules based scaling* → Custom autoscale:
* Instance limits: min 1, max 3, default 1
* Scale out: CPU Percentage > 70 (avg, 5 min) → increase count by 1, cool down 5 min
* Scale out: Memory Percentage > 80 (avg, 5 min) → increase count by 1
* Scale in: CPU Percentage < 30 (avg, 10 min) → decrease count by 1, cool down 10 min

📸 the saved rules page.

*If your subscription only allows Basic tier (B1/B2/B3)*, rules-based autoscale is not available – Basic
tier only supports manually setting the instance count. Document the policy restriction, then demonstrate
scale-out manually during the load test (step 9): watch CPU/memory on the App Service plan's Metrics blade
and, as CPU passes ~70%, go to *Scale out (App Service plan)* → **Manual scale** and increase the instance
count from 1 to 2, then to 3. Scale back down to 1 once the test ends and CPU drops. This reproduces the
same evidence (CPU per instance, requests served by multiple instance IDs) that the automatic rules above
would have produced.
📸 the policy error confirming only Basic SKUs are allowed; the manual instance-count change during the test;
App Service plan Metrics split by instance showing load spread across the added instances.

**9. Load test (Task e)**
```
python loadtest/loadtest.py --url https://<student-number>-agristream.azurewebsites.net --users 60 --minutes 20
```
If CPU stays below 70 %, raise `--users` to 100 or `--stress-ms` to 400.
Alternative: Azure Load Testing → *Quick test* → URL `https://<app>/api/stress?ms=200`, 100 virtual users, 20 min.
📸 terminal summary (shows responses per instance), `results_per_minute.csv` charted in Excel.

**10. Monitoring (Task f)** – during and after the test:
* App Insights → **Live metrics** (request rate, CPU, servers online) 📸 *during* the test
* App Insights → **Performance** and **Failures**; **Application map** (web → functions → SQL/Storage) 📸
* App Service plan → **Metrics**: CPU Percentage + Memory Percentage, split by instance 📸
* Autoscale → **Run history** (instance count stepping 1 → 2 → 3, then back down) 📸 – the key evidence for Task g
* Function App → Metrics: *Function Execution Count*; Storage → Queue → *Queue Message Count* 📸

KQL for App Insights → Logs:
```kusto
requests
| where timestamp > ago(2h)
| summarize requests = count(), avg_ms = avg(duration), p95_ms = percentile(duration, 95) by bin(timestamp, 1m)
| render timechart

requests
| where timestamp > ago(2h)
| summarize count() by cloud_RoleInstance, bin(timestamp, 1m)
| render timechart

performanceCounters
| where timestamp > ago(2h) and name == "% Processor Time Normalized"
| summarize avg(value) by cloud_RoleInstance, bin(timestamp, 1m)
| render timechart
```

**11. Clean up** – delete `rg-agristream` after marking so the S1 plan stops billing.

---

## Running locally
* `AgriStream.Web/appsettings.Development.json` – put your real connection strings there (not committed).
* Functions: fill `local.settings.json`, run with `func start` or F5 in Visual Studio.
* Point `Functions:BaseUrl` at `http://localhost:7071/api/` and leave `Functions:Key` empty.
