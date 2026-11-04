# Single-container Todo deployment

## Architecture and constraints

The existing application is Angular 22.2 (locked dependencies; no SSR), ASP.NET Core
net10.0, EF Core 10.0.13, and SQL Server 2022. Angular already requests `/api` on the
same origin; its development proxy is unchanged. The SDK selector is
`todo-app-be/global.json` (10.0.102, latestFeature roll-forward). Local Node was
25.5.0; the container uses supported Node 24.15.0. Angular requires
`^22.22.3 || ^24.15.0 || >=26.0.0`. There was no Dockerfile or CI pipeline. Existing
Compose runs only the local development database and is intentionally unchanged.

The root Dockerfile now builds Angular with `npm ci` and the existing production
configuration, discovers the single emitted `index.html` directory, and copies
browser assets into the published API's `wwwroot`. Multiple indexes fail the build
instead of accidentally packaging prerender/SSR output. Node and SDKs are build-only.
The API serves static assets and anonymous SPA routes. Missing `/api/*` and
`/health/*` endpoints return 404, never Angular HTML. Identity, guest sessions, CSRF,
private ownership, grants, SQL/JSON providers, and all existing migrations remain.

Final image: SQL Server 2022 + ASP.NET Core 10.0.13 runtime + API/SPA + framework-
dependent EF migration bundle + Bash/curl/sqlcmd/tini. The runtime is copied from
Microsoft's Ubuntu Noble image to SQL's Ubuntu 22.04 image, with the corresponding
Jammy native dependencies installed; image build/runtime validation is required
when either base changes. The image verifies the actual sqlcmd18 and sqlservr paths.
Only HTTP 8080 is published; SQL's 1433 remains inside the container.
The upstream SQL image may retain EXPOSE metadata for 1433; never use `docker -P`
or publish-all-ports. Publish only the explicit HTTP mapping shown below.
Both services run as UID 10001, GID 0, not root. `tini` reaps orphans and forwards signals; Bash
starts SQL, waits at most SQL_STARTUP_TIMEOUT plus one bounded probe, starts the
API, detects either service exiting, and stops both. Child output goes to stdout/
stderr. Shutdown allows 40 seconds then forces termination; use a 60-second Docker
stop timeout. A signal stop normally exits 143; unexpected exits are nonzero.

This couples failure, patching, resource usage and scaling. It is not a full service
supervisor or a high-availability database. It cannot restart just one child,
detect every hung process, or guarantee a clean SQL shutdown after host/OOM failure.
Docker health marks failure but does not itself restart an unhealthy container.
Use external monitoring and an operator-approved recovery policy, not blind restart
loops. Microsoft support for this custom combined image must be confirmed for
your production support agreement. Separate database hosting is operationally safer
but is not the deployment implemented here.

## Versions and build

Use an x86-64 Linux host (SQL Server Linux is not supported on ARM). Start with at
least 2 vCPU/8 GiB RAM and adequate disk; SQL alone requires at least 2 GiB.
Choose VM size from measured workload, not just image size. A single VM is not
necessarily cheaper than every managed alternative once SQL licensing is included.

From the **repository root**, using Docker with Linux containers:

```bash
npm --prefix todo-app-fe ci
npm --prefix todo-app-fe run build -- --configuration production
dotnet publish todo-app-be/src/Todo.Api/Todo.Api.csproj -c Release -o artifacts/api /p:UseAppHost=false
node deploy/copy-browser.mjs todo-app-fe/dist artifacts/api/wwwroot
docker build --pull --platform linux/amd64 -t todo-combined:local .
```

The Dockerfile contains the complete build and publish configuration; no change to
Angular's production budgets, proxy, routes, or application dependencies is needed.
The SDK 10.0 and SQL 2022 tags deliberately follow supported servicing updates;
Node and ASP.NET runtime tags are explicit. For releases, resolve **all four** base
images to validated immutable digests using Dockerfile build arguments NODE_IMAGE,
SDK_IMAGE, RUNTIME_IMAGE and SQL_IMAGE. Record them with the application image digest.
Review monthly security updates, rebuild with `--pull`, scan the final image,
rerun smoke/restore tests, then promote by digest. Never deploy a newer SQL major
version on an existing volume without a separate tested upgrade/restore plan.

## Secrets and settings

Never commit populated env files, SQL provisioning scripts, or credentials. Create
secrets outside this repository. On Linux, use a root-owned tmpfs directory such
as `/run/todo`; use a secret manager to repopulate it after reboot. Directory access
must permit GID 0 traversal, and secret files must be readable by UID 10001/GID 0
(for example root:root directories 0750 and files 0640). Do not use shell tracing.

Create two secret directories, runtime and migration, each containing:

| File | Contents |
| --- | --- |
| `mssql_sa_password` | Strong existing/new SA password; never include quotes |
| `ConnectionStrings__Todo` | Single-line connection string, as described below |

Runtime connection: `Server=localhost,1433;Database=Todo;User Id=todo_app;Password=<secret>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5`.
Migration connection uses a separate migration administrator or SA, never the
runtime login. Supply the actual passwords through a protected editor or Key Vault,
not command-line arguments. Escape/quote connection-string values correctly (ADO.NET
connection-string builder is recommended). `TrustServerCertificate=True` is limited
to SQL inside this container; configure a validated SQL certificate if that changes.

`/run/secrets` uses ASP.NET Core KeyPerFile; `__` maps to `:` and secret files
override environment/JSON configuration. `ConnectionStrings__Todo` environment is
also supported when there is no corresponding file, but is visible to container
administrators through inspect. Prefer files. Never use SA for runtime requests.
SA is read into SQL's child environment, then removed before starting .NET; it is
not passed as an argument. Privileged VM/container administrators can still inspect
process environments or read secrets. File mounts are not protection from root.

| Environment setting | Purpose |
| --- | --- |
| `ACCEPT_EULA=Y` | Required explicit SQL Server license acceptance |
| `MSSQL_PID` | Required explicit edition/license; Developer only for dev/test |
| `ASPNETCORE_ENVIRONMENT` | Production default; Development only for local HTTP tests |
| `ASPNETCORE_URLS` | Defaults to `http://0.0.0.0:8080`; health probe assumes port 8080 |
| `AllowedHosts` | Semicolon-separated real public hostname plus localhost/127.0.0.1 for probes |
| `ReverseProxy__KnownProxy` | Exact trusted proxy source IP; do not trust arbitrary forwarded headers |
| `SQL_STARTUP_TIMEOUT` | Positive integer seconds, default 180; bounded SQL wait |
| `MSSQL_MEMORY_LIMIT_MB` | SQL memory cap; reserve memory for .NET, OS and backup jobs |
| `DataProtection__KeysDirectory` | Defaults to persistent `/var/opt/mssql/todo-keys` |
| `Logging__LogLevel__Default` | Standard ASP.NET logging override |
| `Storage__UseJsonFile` | Defaults false; keep false in this deployment |

Production cookies remain Secure; do not weaken that setting for HTTP convenience.
Persisting Data Protection keys preserves sessions/guest recovery across recreation.
Keys are sensitive and not encrypted at rest by the application: restrict file
permissions, encrypt disks/backups, and protect keys with a certificate/Key Vault
before handling sensitive production data. Losing keys invalidates sessions and
can make guest recovery impossible. Existing account recovery/MFA/email verification
and session-revocation limitations still apply; review the backend README.

## Local volume, first database setup and migration

These commands are Linux Bash. Substitute your protected absolute secret paths.
PowerShell users can run them in WSL; Podman can replace Docker for local validation.
Never point a test container at the existing development/production volume.

```bash
docker volume create todo-combined-data
docker run -d --name todo --stop-timeout 60 \
  -p 127.0.0.1:8080:8080 \
  --mount source=todo-combined-data,target=/var/opt/mssql \
  --mount type=bind,src=/run/todo/runtime,target=/run/secrets,readonly \
  -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
  -e ASPNETCORE_ENVIRONMENT=Development -e MSSQL_MEMORY_LIMIT_MB=2048 \
  todo-combined:local
docker logs -f todo
```

For **a new volume only**, connect through stdin (no SQL port publication):

```bash
docker exec -i todo sql-admin.sh < /run/todo/bootstrap.sql
```

Create `/run/todo/bootstrap.sql` privately, with the following template. Replace the
password locally, safely SQL-escape any apostrophes, and remove this file after use.
Do not run it against an existing initialized database; it intentionally fails
instead of replacing existing data/logins. The app can start before provisioning
but readiness stays 503 until the database and all migrations exist.

```sql
CREATE DATABASE [Todo];
GO
CREATE LOGIN [todo_app] WITH PASSWORD = N'<strong runtime password>', CHECK_POLICY = ON;
GO
USE [Todo];
CREATE USER [todo_app] FOR LOGIN [todo_app];
ALTER ROLE [db_datareader] ADD MEMBER [todo_app];
ALTER ROLE [db_datawriter] ADD MEMBER [todo_app];
GO
```

Apply existing migrations explicitly while the API is stopped. The migration secret
directory holds the same SA password and the **administrator** connection string.
No migrations, EnsureCreated, reset or JSON import run during ordinary startup.

```bash
docker stop -t 60 todo
docker rm todo
docker run --rm --name todo-migrate --stop-timeout 60 \
  --mount source=todo-combined-data,target=/var/opt/mssql \
  --mount type=bind,src=/run/todo/migration,target=/run/secrets,readonly \
  -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
  todo-combined:local migrate
# Only after exit status 0, repeat the normal docker run command above.
```

Do not mount one SQL volume into two running containers. Existing database volumes
require their **existing** SA password: changing a secret does not rotate a stored
SQL password. Rotate SQL credentials through an authenticated administrator, then
update the secret in a coordinated maintenance window. Migrations use the existing
SQL Server migration chain (InitialSqlServerTasks, AddPurposes,
AddAccountsAndTaskSharing). Historical PostgreSQL migrations remain excluded.
The source SDK workflow (`dotnet ef database update`) is still available; use the
repository tool manifest and a protected admin connection.

## Verify and operate

```bash
curl -f http://127.0.0.1:8080/
curl -f http://127.0.0.1:8080/login
curl -f http://127.0.0.1:8080/purposes
curl -f http://127.0.0.1:8080/health/live
curl -f http://127.0.0.1:8080/health/ready
curl -i http://127.0.0.1:8080/api/tasks       # 401 without a session, not HTML
curl -i http://127.0.0.1:8080/api/not-found   # 404, not HTML
docker exec todo sql-admin.sh -d Todo -Q 'SELECT COUNT(*) AS MigrationCount FROM dbo.__EFMigrationsHistory;'
docker logs --tail 100 todo
docker stats --no-stream todo
docker inspect todo                         # do not publish output if using secret env vars
docker stop -t 60 todo
docker start todo
```

Readiness performs a database connection and checks pending EF migrations; it returns
only health status, not connection details. Liveness does not depend on SQL. Neither
is a business/security correctness check. Use a browser to sign in or continue as
guest, create a task/project, then stop/remove/recreate **only the container** with
the same named volume and secrets. Verify records, ownership, sharing, and cookies
survive. Refresh `/login` and `/purposes`; verify JS/CSS requests return assets.
Production testing requires HTTPS, otherwise Secure auth cookies will not work.

Failure checks on disposable volumes: omit EULA/secret files (must exit nonzero),
use invalid SA credentials and a short SQL_STARTUP_TIMEOUT (must fail within the
bounded wait), terminate either child (container must exit nonzero), and send
`docker stop -t 60` (both processes must stop). Never kill SQL on a production volume
as a test. Test recreation and backup restoration before promoting a release.

The dependency-free smoke runner automates these checks using uniquely named,
disposable volumes and temporary credentials, and removes only its own resources:

```bash
node deploy/verify-container.mjs todo-combined:local
# Optional explicit engine path/name as the third argument:
node deploy/verify-container.mjs todo-combined:local podman
```

Run the smoke runner in an isolated development environment with enough RAM. On
Linux its temporary secret files must be readable by the image's UID 10001/GID 0
(run the runner from a suitable privileged test environment). The runner makes HTTP
requests inside the container so it also works when Podman's host forwarding is
unavailable. It does not replace real-browser HTTPS/authentication checks. For
Podman builds use `podman build --format docker` to retain HEALTHCHECK metadata.

## Persistence, backup and recovery

`/var/opt/mssql` contains database/log files, SQL system databases, SQL secrets,
backups and Todo Data Protection keys. Persist **the entire directory**. Named
volumes survive `docker rm` but not volume deletion. Never use `docker volume rm`,
`volume prune`, or `compose down -v` on production data. An image has no production
database data; the anonymous VOLUME default is not an adequate managed deployment.

Persistent storage is not a backup. Disk loss, deletion, ransomware and bad schema
changes can destroy persistent data. Define RPO/RTO, retention and restore drills.
Example database backup, using a distinct filename for every backup:

```bash
docker exec todo mkdir -p /var/opt/mssql/backups
docker exec todo sql-admin.sh -Q "BACKUP DATABASE [Todo] TO DISK=N'/var/opt/mssql/backups/Todo-before-upgrade.bak' WITH COPY_ONLY, CHECKSUM;"
docker exec todo sql-admin.sh -Q "RESTORE VERIFYONLY FROM DISK=N'/var/opt/mssql/backups/Todo-before-upgrade.bak' WITH CHECKSUM;"
docker cp todo:/var/opt/mssql/backups/Todo-before-upgrade.bak /secure-off-disk-backup/
```

Copy backups to access-controlled, encrypted Azure Blob Storage on a schedule, with
retention/immutability where required. `VERIFYONLY` is not a restore drill. Back up
system databases/logins and Data Protection keys securely too; recreating a SQL user
with the same name but a different SID can orphan restored users. Full recovery
requires transaction log backups and log-space monitoring; SIMPLE recovery trades
point-in-time recovery for simpler management. Choose deliberately; do not silently
change the existing recovery model. SQL Express lacks SQL Agent; schedule backups
with a VM systemd timer/cron using managed identity for Blob uploads.

Restore into a **new disposable SQL volume/container** first, with the same or newer
compatible SQL engine, never an older engine. Transfer the backup to its backups
directory. Inspect logical file names, then restore to a new database name:

```bash
docker exec todo-recovery sql-admin.sh -Q "RESTORE FILELISTONLY FROM DISK=N'/var/opt/mssql/backups/Todo-before-upgrade.bak';"
docker exec todo-recovery sql-admin.sh -Q "RESTORE DATABASE [TodoRecovery] FROM DISK=N'/var/opt/mssql/backups/Todo-before-upgrade.bak' WITH MOVE N'<logical-data-name>' TO N'/var/opt/mssql/data/TodoRecovery.mdf', MOVE N'<logical-log-name>' TO N'/var/opt/mssql/data/TodoRecovery_log.ldf', CHECKSUM;"
```

Restore/reconcile logins, validate data, run DBCC CHECKDB and application smoke checks,
then explicitly switch the runtime connection after a maintenance window. Do not
automatically use WITH REPLACE or delete current files. For an in-place disaster
restore, an administrator must stop application writes and approve the overwrite.

Image upgrade: take/verify an off-disk backup; record old image digest/config; stop
the old container; apply reviewed migrations once via new-image `migrate`; recreate
the service with the **same volume** and pinned new digest; check health and browser
flows. No image step copies files over the mounted SQL data directory. If migration
or deployment fails, preserve logs/data and stop. A previous app image may only use
the migrated database when schema compatibility is confirmed. Otherwise restore
the pre-upgrade backup to a separate volume and redeploy the old digest/config;
account for writes lost since that backup. Do not blindly downgrade SQL binaries or
reverse a destructive migration. Disk snapshots require application/SQL consistency;
live copying MDF/LDF files is not a backup strategy.

## Validation record (2026-10-27)

Executed in this Windows workspace with the Podman amd64 Linux engine, not a Docker
daemon or Azure VM:

- `dotnet build todo-app-be/Todo.slnx --configuration Release`: passed.
- `podman build --format docker --platform linux/amd64 -t todo-combined:local .`:
  passed; includes npm ci, Angular production build, Linux .NET publish, EF bundle,
  runtime/native dependencies, executable paths and shell syntax checks.
- `node deploy/verify-container.mjs todo-combined:local <absolute-podman-path>`:
  all 13 checks passed. Verified non-root/no SDK/no Node, configuration failures,
  bounded SQL startup failure, distinct liveness/readiness, explicit migrations,
  SPA homepage/nested-route HTML and JS assets, API 401/404 behavior, CSRF, guest
  identity, project/task CRUD, real backup/restore plus DBCC CHECKDB, database and
  session persistence after recreation, both child-exit paths and stop/restart.
- Editor diagnostics: no errors in the changed C# files or smoke runner.
- Disposable test containers, named volumes and temporary credentials were removed;
  the pre-existing development SQL container/data were not modified.

Initial checks exposed and resolved a missing frontend helper COPY, fresh-volume
ownership, and the migration bundle's unwritable default home extraction path.
A separate real-browser attempt at the packaged listener was blocked by Windows/
Podman loopback forwarding (`ERR_CONNECTION_REFUSED`); HTTP/asset checks inside the
container passed. Browser rendering and production HTTPS/auth flows still need
verification on the target Linux VM. Azure provisioning, managed identity, Key
Vault, mounted managed disk, reboot ordering and SQL licensing were not executed
or validated against an Azure subscription. There is no existing automated test
suite to run; no tests were removed or disabled.

## Files changed

- `Dockerfile`: frontend build, API publish, migration bundle and final SQL image.
- `.dockerignore`, `.gitignore`, `.gitattributes`: secrets/artifact exclusions and shell line endings.
- `deploy/copy-browser.mjs`: discover and copy the actual browser output.
- `deploy/entrypoint.sh`: SQL/API supervision and explicit maintenance migrations.
- `deploy/healthcheck.sh`, `deploy/sql-admin.sh`: readiness and secret-file SQL administration.
- `deploy/verify-container.mjs`: disposable integration/restore smoke checks.
- `deploy/README.md`: this runbook and actual verification record.
- `todo-app-be/src/Todo.Api/Program.cs`: static hosting, routing, file secrets, keys and trusted proxy configuration.
- `todo-app-be/src/Todo.Api/Health/DatabaseReadinessCheck.cs`: SQL/schema readiness.
- `todo-app-be/README.md`: deployment documentation link.
- `.vscode/tasks.json`: build and validation task entries used during this work.

## Azure Linux VM deployment

Prerequisites: Azure subscription, Azure CLI, permissions for VM/disk/ACR/Key Vault
and role assignments, DNS, a TLS certificate strategy, Docker Engine on a supported
x86-64 Ubuntu VM, and an approved SQL license. Use one VM, persistent managed disk,
network security group, Basic ACR (or an existing registry), Key Vault and backup
storage. Budget disk/backup/registry costs and SQL licensing as well as VM compute.
Avoid public database access. Prefer Azure Bastion/JIT or IP-restricted SSH.

Example ACR push from your trusted build machine (replace names/IDs):

```bash
az acr create --resource-group "$RG" --name "$ACR" --sku Basic
az acr login --name "$ACR"
docker build --pull --platform linux/amd64 -t "$ACR.azurecr.io/todo:$RELEASE" .
docker push "$ACR.azurecr.io/todo:$RELEASE"
az vm identity assign --resource-group "$RG" --name "$VM"
VM_PRINCIPAL=$(az vm show -g "$RG" -n "$VM" --query identity.principalId -o tsv)
ACR_ID=$(az acr show -n "$ACR" --query id -o tsv)
az role assignment create --assignee-object-id "$VM_PRINCIPAL" --assignee-principal-type ServicePrincipal --role AcrPull --scope "$ACR_ID"
```

For an ABAC-enabled registry, use its repository-reader role/permissions instead of
AcrPull. Also grant the VM identity **Key Vault Secrets User** only on the required
vault/secrets, and suitable scoped Blob permissions for backups. Never enable the
ACR admin account just to avoid managed identity configuration.

On the VM, log into Azure using identity and authenticate Docker without printing
the short-lived ACR token. Protect Docker config (or use a credential helper):

```bash
az login --identity --output none
az acr login --name "$ACR" --expose-token --query accessToken -o tsv | \
  docker login "$ACR.azurecr.io" -u 00000000-0000-0000-0000-000000000000 --password-stdin
docker pull "$ACR.azurecr.io/todo@$IMAGE_DIGEST"
docker logout "$ACR.azurecr.io"
```

Attach a new managed data disk using the Azure portal/CLI (`az vm disk attach`).
Use `lsblk -f` and `/dev/disk/azure/scsi1/lun*` to identify it. **Only format a verified
new empty disk**; never blindly run mkfs on an existing disk or `/dev/sda`. Partition/
format with ext4 or XFS, mount by UUID at `/srv/todo-disk`, and add a persistent fstab
entry. Follow Microsoft's current managed-disk attachment guidance. Do not use the
VM's temporary/resource disk. Require this mount before container startup; otherwise
Docker can silently create a directory on the OS disk. A systemd deployment unit
should use `RequiresMountsFor=/srv/todo-disk` and verify `mountpoint -q` first.

```bash
mountpoint -q /srv/todo-disk
sudo install -d -o 10001 -g 0 -m 0770 /srv/todo-disk/mssql
sudo install -d -o root -g root -m 0750 /run/todo/runtime /run/todo/migration
```

Fetch secrets without stdout/logging exposure (run in a privileged provisioning
context with restrictive umask; disable shell tracing):

```bash
umask 077
az keyvault secret download --vault-name "$VAULT" --name todo-sa-password \
  --file /run/todo/runtime/mssql_sa_password --encoding utf-8 --output none
az keyvault secret download --vault-name "$VAULT" --name todo-runtime-connection \
  --file /run/todo/runtime/ConnectionStrings__Todo --encoding utf-8 --output none
chmod 0640 /run/todo/runtime/*
```

Provision migration secrets similarly only for maintenance. Files must be root:root
and readable by GID 0. Azure CLI download may refuse existing destination files;
rotate via new protected files and atomic replacement, then recreate the container
to remount secrets. KeyPerFile is not configured for live reload. Use a systemd
oneshot/deployment service to fetch secrets after disk/network readiness and before
Docker starts the application. Do not let Docker auto-start before secret tmpfs and
disk mounts exist after a reboot; configure service ordering explicitly and verify
the reboot path. Restrict Docker socket access (equivalent to root).

Provision the database and apply migrations as above, replacing the named volume
with a bind mount of `/srv/todo-disk/mssql`. Choose a properly licensed MSSQL_PID.
Developer is **not** permitted for production. Express may suit a small workload
within its database size/CPU/memory limits; verify SQL Server 2022 Express limits
and Azure licensing terms. Standard/Enterprise require the appropriate paid license
(BYOL/subscription/PAYG as applicable). Get licensing approval before production.
Never assume a generic VM image includes a SQL Server license.

Example production run, after provisioning and migrations:

```bash
docker run -d --name todo --restart unless-stopped --stop-timeout 60 \
  --memory 6g --cpus 2 --log-driver json-file \
  --log-opt max-size=20m --log-opt max-file=5 \
  -p 127.0.0.1:8080:8080 \
  --mount type=bind,src=/srv/todo-disk/mssql,dst=/var/opt/mssql \
  --mount type=bind,src=/run/todo/runtime,dst=/run/secrets,readonly \
  -e ACCEPT_EULA=Y -e MSSQL_PID=Express \
  -e MSSQL_MEMORY_LIMIT_MB=2048 -e ASPNETCORE_ENVIRONMENT=Production \
  -e 'AllowedHosts=todo.example.com;localhost;127.0.0.1' \
  -e ReverseProxy__KnownProxy=172.17.0.1 \
  "$ACR.azurecr.io/todo@$IMAGE_DIGEST"
```

`172.17.0.1` is an example, **not an assumption**. Confirm the Docker bridge/proxy
source IP on this VM (it may be IPv4-mapped IPv6); set the exact trusted source.
Use a systemd service ordered after mounts/secrets, and ensure the Docker daemon
cannot auto-restore the container before those dependencies are present. Resource
caps are examples: SQL edition caps and other resident services also matter.

Install Caddy or nginx **on the VM**, not as another application container. Example
Caddyfile for automatic public TLS (DNS must point to the VM):

```caddyfile
todo.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

The proxy terminates TLS and sets forwarded scheme/client IP; test HTTPS sign-in,
CSRF and IP rate limiting from outside. Set HSTS at the TLS proxy after TLS is
working. Restrict NSG and host firewall to HTTPS 443 and HTTP 80 for redirect/ACME;
restrict SSH to administrator sources or Bastion. Do not allow 8080/1433 publicly
and never publish SQL's port. An internal load balancer/other TLS ingress can be
used instead, with exact trusted proxy settings and allowed hosts.

Monitor Azure VM CPU/RAM, Docker stats/restarts/health, SQL error logs, disk free
space, SQL log growth, backup age and restore success. Configure Azure Monitor/
Log Analytics alerts and bounded Docker log retention; avoid logging secrets or
task contents. SQL also writes internal error logs to the persistent disk; include
them in disk-space/retention monitoring. Keep backups off the data disk. Patch the
host, Docker and base images regularly. Recreate by digest for updates, following
the backup/migrate/verify/rollback procedure above. VM deployment, real HTTPS,
Key Vault roles, disk provisioning and production licensing require operator action.