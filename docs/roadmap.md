# Roadmap

What is built, what is left, and what was deliberately ruled out. The goal is a small,
boring, resilient API that never blanks the wall display.

## Done

| Phase | |
|---|---|
| 1 — Foundation | One JSON path, global exception handling to RFC 9457, typed `HttpClient`s with 10s timeouts, `TimeProvider`, `.editorconfig` / `Directory.Build.props` / `global.json`. |
| 2 — Response contracts | `ApiResponse<T>` envelope with `meta.stale`. Offset-bearing calendar dates. Ical.Net 5.2.3, which fixed recurring-event duplication. |
| 3 — Caching | `CachedSource` with stale-on-error and single-flight. ETag / `If-None-Match` / 304, `Cache-Control`, `Age`. TTLs tuned to the 2-minute poll. |
| 4 — API key auth | `X-Api-Key`, constant-time compare, `OPTIONS` exempt, health endpoints anonymous. |
| 5 — Weather | NWS forecast, current conditions and alerts behind `IWeatherProvider`. |
| 8 — Tests | 80 tests covering ICS semantics, the cache wrapper, task mapping, the wire contract and the key check. |

Phase 7 is partly done: the AcrPull grant, resource tags, the Log Analytics daily cap, a
stable `container_app_url`, and a concurrency group are all in place.

## Phase 6 — Photos

`GET /photos?limit=200` returning `{ id, url, contentType, sizeBytes }` plus
`meta.urlsExpireAt`, backed by Azure Blob Storage.

New Terraform: a storage account (Standard LRS, no public blob access, TLS 1.2 minimum)
and a private `photos` container. Use `azurerm_storage_container`'s `storage_account_id`
argument rather than the deprecated `storage_account_name` — the ID form goes through the
management plane, which the CI principal can already do. The name form uses the data
plane and would need either shared keys or another role grant.

The app mints **User Delegation SAS** URLs via `Azure.Storage.Blobs` and `Azure.Identity`.
This is what makes the whole design work: an `<img src>` cannot send an `X-Api-Key` header,
so without self-authenticating URLs every photo would have to be proxied through the
container.

### Manual step

The app's identity needs **Storage Blob Data Reader** on the storage account — that role
because it carries `generateUserDelegationKey`, which is what allows SAS minting without
ever handling an account key. The CI principal is Contributor and cannot grant roles, so
this is done by hand **after** the storage account exists:

```sh
PRINCIPAL=$(az containerapp show \
  -n app-petersoncommondataservice \
  -g rg-petersoncommondataservice \
  --query identity.principalId -o tsv)

STORAGE=$(az storage account show \
  -n <storage-account-name> \
  -g rg-petersoncommondataservice \
  --query id -o tsv)

az role assignment create \
  --assignee-object-id "$PRINCIPAL" \
  --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Reader" \
  --scope "$STORAGE"
```

Use `--assignee-object-id` with an explicit principal type, not plain `--assignee`. The
latter resolves the ID through Microsoft Graph first and often fails on a freshly created
identity purely because the directory has not caught up.

Getting photos *into* the container is a data-plane operation, which Owner does not grant
on its own. Either leave `shared_access_key_enabled = true` and upload with the account
key, or disable it and grant yourself **Storage Blob Data Contributor**.

### Constraints the 2-minute refresh imposes

The display fetches the manifest **hourly** and rotates locally every 2 minutes. It must
not call `/photos` every 2 minutes. Three consequences:

- **Do not shuffle server-side.** Randomising per request changes the body every time,
  which breaks the ETag and forces a full re-download on every poll. Return a stable list
  and let the display shuffle.
- **Cache blob names, mint SAS at response time.** Caching the URLs means a hit 55 minutes
  later hands out URLs 55 minutes closer to expiry. Signing is a local HMAC, no network.
- **SAS must outlive a full rotation, not just the cache TTL.** 200 photos at 2 minutes is
  a 6.7-hour loop. With an hourly manifest refetch, issue SAS at 12 hours. Get this wrong
  and photos silently 404 partway through the night.

**Pre-resize the photos** at upload time with a batch script, not in the service. ~700
images/day at 4 MB is ~2.8 GB/day of egress, near Azure's free allowance and slow over
wifi. Resizing to the panel's native resolution cuts it roughly tenfold.

Omit `takenAt` in v1 — it needs EXIF parsing or blob metadata set at upload.

## Phase 7 — CI hardening (remainder)

All in [deploy.yaml](../.github/workflows/deploy.yaml):

- **Gate the image push.** Every PR currently overwrites `:latest` in ACR.
- **Pass secrets as `TF_VAR_*` env vars** instead of interpolating into shell arguments,
  where they land in process args and break on quoting.
- **Build and test before the Docker build.** CI never compiles the app outside Docker and
  runs none of the 80 tests. Note it is `dotnet run --project tests/…`, not `dotnet test`.
- **Add `terraform fmt -check` and `terraform validate`.**
- **Add a `permissions:` block** to narrow the default token.

## Housekeeping

- Delete the `import` block for `azurerm_role_assignment.acr_pull` in
  [main.tf](../infra/terraform/main.tf). It has done its job — the assignment is in state.
  Leaving it is harmless on normal runs but fails the plan on a from-scratch rebuild,
  which is the case the resource exists to support.
- Delete the `/health` shim in `Program.cs`. Both probes now point at `/health/live`.
- Rewrite or delete [deployment.md](deployment.md). It describes a .NET 8 `dotnet publish`
  flow that bears no resemblance to the ACR and Terraform pipeline in use. Nothing links
  to it any more.
- `/health/ready` registers no checks and always answers healthy. Either wire it to
  per-source freshness or drop it.

## Gotchas worth not rediscovering

- **The container app environment cannot be updated.** Its `LogAnalyticsConfiguration`
  must carry the workspace shared key, which Azure returns as `null` on every read, so the
  provider sends the null back and the call fails with `LogAnalyticsConfiguration is
  invalid`. Adding four tags was enough to trigger it. That resource is deliberately
  untagged.
- **Deploys race for the Terraform state lock.** Merging fires the `pull_request` and
  `push` runs seconds apart against one state file. A repository-wide concurrency group
  queues them.
- **The image tag must be the commit SHA.** With `revision_mode = "Single"` and a fixed
  `:latest`, Terraform sees no change and never rolls a revision — a deploy appears to
  succeed while the old container keeps serving.
- **`.env` overwrites real environment variables** in Development, and `dotnet run`
  applies `launchSettings.json` over inherited environment. Use `--no-launch-profile`.

## Deliberately not doing

- **No database.** Nothing needs durable state. Meals and shopping, when they arrive,
  become two more Todoist projects: same client, same cache wrapper, same envelope, no new
  infrastructure. The only thing that keeps that cheap is keeping the label-to-colour
  mapping in a per-endpoint mapper rather than inside the Todoist client.
- **No `min_replicas = 1`.** A 2-minute poll keeps the replica alive inside the ~5 minute
  idle window, so scale-to-zero already behaves like an always-warm instance.
- **No SSE or WebSocket.** Polling is correct here, and long-lived connections fight
  scale-to-zero.
- **No API versioning, no OpenTelemetry, no rate limiting.** One consumer.
- **No health check that pings upstreams.** A liveness probe that fails when Todoist is
  down would have the platform kill a healthy container and destroy the cache that exists
  to survive exactly that outage, turning a partial failure into a total one.
