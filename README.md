# Peterson Common Data Service

A small, boring, resilient JSON API that feeds a wall display. It aggregates a few
personal data sources — calendar, tasks, weather — behind one consistent contract so the
display can stay dumb.

The guiding constraint is that **the display must never go blank**. It runs unattended and
polls forever, so every endpoint serves the last known good response when its upstream is
down, and says so, rather than failing.

## Endpoints

All responses share one envelope: `{ "data": ..., "meta": { source, fetchedAt, stale,
staleReason, ttlSeconds } }`. When `stale` is `true` the upstream call failed and you are
looking at cached data — render it, maybe dimmed, but do not treat it as an error. Real
failures are RFC 9457 `application/problem+json` instead, never the envelope.

| Endpoint | Notes |
|---|---|
| `GET /calendar` | Expanded ICS occurrences. `?days=N` (1–30, default 5), or `?from=`/`?to=`. |
| `GET /tasks` | Todoist items carrying the display label, mapped to a colour. |
| `GET /weather` | National Weather Service forecast, current conditions and active alerts. 503 if no coordinates are configured. |
| `GET /health/live` | Zero checks. Proves the process answers. This is what the Azure probes call. |
| `GET /health/ready` | Reserved for upstream freshness reporting; currently registers no checks and always answers healthy. Wired to no probe, deliberately. |

Everything except the health endpoints requires an `X-Api-Key` header. `OPTIONS` is exempt
so CORS preflights work.

Responses carry `ETag`, `Cache-Control` and `Age`. Send `If-None-Match` and unchanged data
comes back as a `304` — worth doing, since the display polls every two minutes.

## Running locally

```sh
dotnet run --project code/PetersonCommonDataService
```

Secrets come from `code/PetersonCommonDataService/.env` (gitignored) in Development. If no
API key is configured, Development allows requests through with a loud warning; Production
refuses to start.

Tests:

```sh
dotnet run --project tests/PetersonCommonDataService.Tests
```

Not `dotnet test` — xunit.v3 runs on Microsoft.Testing.Platform, and the .NET 10 SDK still
routes `dotnet test` through the retired VSTest bridge, which fails.

## Deployment

Pushing to `main` builds an image, pushes it to ACR tagged with the commit SHA, and runs
`terraform apply`. The SHA tag matters: the container app uses `revision_mode = "Single"`,
so with a fixed `:latest` tag Terraform sees no change and never rolls a new revision. A
deploy would appear to succeed while the old container kept serving. This cost several
hours to diagnose once already.

Required GitHub secrets:

| Secret | Purpose |
|---|---|
| `AZURE_CREDENTIALS` | Service principal JSON. Also supplies subscription and tenant IDs. |
| `ACR_SERVER` | Registry login server. |
| `TF_STORAGE_ACCOUNT`, `TF_CONTAINER`, `TF_STATE_KEY` | Terraform remote state backend. |
| `TF_VAR_DOMAIN_NAME` | Custom domain bound to the container app. |
| `ICS_URL`, `TODOIST_API_KEY`, `TODOIST_PROJECT_ID` | Upstream credentials. |
| `API_KEYS` | Comma-separated keys the service accepts. Rotate by listing both, then dropping the old one. |
| `WEATHER_LATITUDE`, `WEATHER_LONGITUDE` | Forecast location. Optional; `/weather` returns 503 without them. Kept as secrets so a home address is not committed. |

## Rebuilding the infrastructure from scratch

**Terraform cannot do this unassisted.** Granting the AcrPull role is a manual step. If you
are only deploying code changes to existing infrastructure, ignore this section entirely —
it applies only to building the Azure resources from nothing.

Two things stand in the way, and both are ordinary Azure facts rather than bugs here:

- **The CI service principal is Contributor**, which can read role assignments but cannot
  create them. Azure carves permission-granting out of Contributor on purpose — otherwise
  anyone holding it could simply grant themselves Owner.
- **The grant has a chicken-and-egg problem.** The container app pulls its own image using
  its system-assigned identity, but that identity does not exist until the app has been
  created — and the app needs the grant in order to pull its first image.

So the sequence is:

1. Delete (or comment out) the `azurerm_role_assignment.acr_pull` resource in
   [main.tf](infra/terraform/main.tf) **and its `import` block**. An import block aimed at
   something that does not exist yet fails the plan.
2. Run the deploy. The app is created; its first image pull fails, because it has no key
   to the registry yet. This is expected.
3. Grant the role by hand, as a user with Owner or User Access Administrator:

   ```sh
   PRINCIPAL=$(az containerapp show -n app-petersoncommondataservice \
     -g rg-petersoncommondataservice --query identity.principalId -o tsv)

   ACR=$(az acr show -n acrpetersoncommondataservice \
     -g rg-petersoncommondataservice --query id -o tsv)

   az role assignment create --assignee "$PRINCIPAL" --role AcrPull --scope "$ACR"
   ```

4. Re-run the deploy so a new revision starts and pulls successfully.
5. Optionally put the grant back under Terraform's eye, so its removal is detected in
   future: restore the resource and its `import` block, and set
   `acr_pull_role_assignment_id` to the GUID of the assignment you just created —
   `az role assignment list --scope "$ACR" --query "[].name" -o tsv`. The GUID is random
   and differs every rebuild, so the default committed in
   [variables.tf](infra/terraform/variables.tf) will be stale.

The same applies to the managed TLS certificate for the custom domain, which is bound
out-of-band and held behind `ignore_changes` in the custom domain resource.

## Layout

| Path | |
|---|---|
| [code/PetersonCommonDataService/](code/PetersonCommonDataService/) | The service. |
| [tests/PetersonCommonDataService.Tests/](tests/PetersonCommonDataService.Tests/) | xunit.v3 suite. |
| [infra/terraform/](infra/terraform/) | Azure resources. |
| [infra/dockerfile](infra/dockerfile) | Multi-stage build. Needs `Directory.Build.props` and `global.json` present before restore. |
