# Working in this repo

## The user does all pushes

Do not run `git push`. Do not run `gh pr create`. Do not delete remote branches.

Commit locally — that part is fine — then stop and report the branch name, the commit
subject, and what merging it would cause. The user decides when anything leaves the
machine.

The reason is that this repo deploys itself. A merge to `main` builds an image, runs
`terraform apply` against live Azure infrastructure, and rolls a new container app
revision. Opening a PR fires a `terraform plan`. Neither should happen because an agent
thought it was being helpful.

If the user pushes and then asks for a PR, opening it is fine. The rule is about not
initiating.

## Running things

```sh
dotnet run --project code/PetersonCommonDataService    # the service
dotnet run --project tests/PetersonCommonDataService.Tests    # the tests
```

**Not `dotnet test`.** xunit.v3 runs on Microsoft.Testing.Platform, and the .NET 10 SDK
still routes `dotnet test` through the retired VSTest bridge, which fails. Several
documented workarounds were tried and none of them work today.

In Development, `.env` overwrites real environment variables, and `dotnet run` applies
`launchSettings.json` over anything inherited — pass `--no-launch-profile` when that
matters.

## Things that will waste your time if you rediscover them

- **The container app environment cannot be updated.** Its `LogAnalyticsConfiguration`
  must carry the workspace shared key, which Azure returns as `null` on every read, so the
  provider sends that null back and the call fails with `LogAnalyticsConfiguration is
  invalid`. Adding four tags was enough to break an apply. That resource is deliberately
  the only untagged one.
- **The image tag must be the commit SHA.** With `revision_mode = "Single"` and a fixed
  `:latest`, Terraform sees no drift and never rolls a revision, so a deploy appears to
  succeed while the old container keeps serving.
- **Deploys race for the Terraform state lock.** Merging fires the `pull_request` and
  `push` runs seconds apart against one state file; a repository-wide concurrency group
  queues them.
- **`az containerapp secret list` is blocked** by the permission classifier. Do not try to
  work around it.
- **`code/PetersonCommonDataService/.env` holds real secrets.** It is gitignored. Never
  print its values — key names only.
- **Check `gh run view --json conclusion`**, not the exit code of `gh run watch`, which has
  reported success on a failed run.

## Where things are

[docs/roadmap.md](docs/roadmap.md) has what is built, what is left, and what was ruled out
on purpose. [README.md](README.md) has the endpoint contracts, the deploy pipeline, and the
manual Azure role grants a from-scratch rebuild needs.
