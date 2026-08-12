# Mon Kado backend

Mon Kado is an API for creating, sharing, and managing gift wishlists.

The repository currently contains the technical baseline, PostgreSQL persistence foundation, and containerized runtime stack. Business endpoints and entities are introduced by dedicated backlog items.

## Prerequisites

- [.NET SDK 10.0.302 or a later .NET 10 feature band](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 when using the Visual Studio IDE. Visual Studio 2022 cannot build projects targeting `net10.0`; the .NET CLI or a compatible editor such as VS Code can be used instead.
- Docker Desktop with Linux containers enabled to run the PostgreSQL integration tests.

The repository-level `global.json` selects .NET 10 and allows roll-forward to a later installed .NET 10 feature band. Preview SDKs are not accepted.

## Build and test

```powershell
dotnet restore JennGllg.Fr.MonKado.Back.slnx
dotnet build JennGllg.Fr.MonKado.Back.slnx --configuration Release --no-restore
dotnet test JennGllg.Fr.MonKado.Back.slnx --configuration Release --no-build --no-restore
dotnet format JennGllg.Fr.MonKado.Back.slnx --verify-no-changes --no-restore
dotnet list JennGllg.Fr.MonKado.Back.slnx package --vulnerable --include-transitive
```

The complete test suite starts one temporary PostgreSQL 18 container. Docker must be running before `dotnet test`.

## PostgreSQL and migrations

The API reads its database connection exclusively from `ConnectionStrings:PostgreSql`. Store the local value in user secrets; never commit a password:

```powershell
dotnet user-secrets set "ConnectionStrings:PostgreSql" "Host=localhost;Port=5432;Database=mon_kado;Username=mon_kado;Password=<password>;SSL Mode=Disable" --project src/API/Api.csproj
```

Deployments provide the same setting through the `ConnectionStrings__PostgreSql` environment variable. Production connections must use the TLS mode and certificate validation required by the selected PostgreSQL host.

Restore the repository-local EF Core tool before managing migrations:

```powershell
dotnet tool restore
```

The target project owns the `DbContext` and migration files; the API is the startup project that supplies configuration:

```powershell
dotnet ef migrations add MigrationName --project src/Infrastructure.Persistence.PostgreSql/Infrastructure.Persistence.PostgreSql.csproj --startup-project src/API/Api.csproj --context MonKadoDbContext --output-dir Migrations
dotnet ef migrations list --project src/Infrastructure.Persistence.PostgreSql/Infrastructure.Persistence.PostgreSql.csproj --startup-project src/API/Api.csproj --context MonKadoDbContext
dotnet ef migrations has-pending-model-changes --project src/Infrastructure.Persistence.PostgreSql/Infrastructure.Persistence.PostgreSql.csproj --startup-project src/API/Api.csproj --context MonKadoDbContext
dotnet ef database update --project src/Infrastructure.Persistence.PostgreSql/Infrastructure.Persistence.PostgreSql.csproj --startup-project src/API/Api.csproj --context MonKadoDbContext
dotnet ef migrations script --idempotent --output artifacts/postgresql-migrations.sql --project src/Infrastructure.Persistence.PostgreSql/Infrastructure.Persistence.PostgreSql.csproj --startup-project src/API/Api.csproj --context MonKadoDbContext
```

The API never applies migrations during startup. Migrations must be reviewed and executed as a separate deployment action. `InitialPersistenceBaseline` intentionally creates only EF Core's `public.__EFMigrationsHistory` table; business tables will be introduced by their owning backlog items.

## Run locally

```powershell
dotnet run --project src/API/Api.csproj --launch-profile http
```

The local launch profile listens on `http://localhost:7000` and uses the `Local` environment.

| Endpoint | Purpose |
|---|---|
| `GET /liveness` | Confirms that the API process is alive |
| `GET /readiness` | Confirms that the API is ready to receive traffic |
| `GET /openapi/v1.json` | Publishes the versioned OpenAPI 3.1 contract as JSON |

Liveness never contacts PostgreSQL. Readiness allows at most two seconds for PostgreSQL to accept a connection and returns `503 Unhealthy` otherwise; it checks connectivity, not whether all migrations have been applied.

The OpenAPI contract is available in every environment. No interactive Swagger or Scalar UI is installed.

## Architecture

The solution keeps four production layers:

- `API`: HTTP composition root and transport concerns.
- `Application`: use cases and MediatR handlers.
- `Domain`: business rules with no dependency on other solution projects.
- `Infrastructure.Persistence.PostgreSql`: EF Core/Npgsql context, configuration, and versioned migrations.

Tests are separated by responsibility:

- `Api.FunctionalTests` verifies API host behavior without a live external dependency.
- `Api.IntegrationTests` exercises the public HTTP API against a real PostgreSQL instance.
- `Infrastructure.Persistence.PostgreSql.MigrationTests` verifies versioned database migrations without testing persistence implementation details.

## Container stack

Docker Compose runs five services:

| Service | Responsibility | Public port |
|---|---|---|
| `caddy` | Terminates HTTPS and proxies the API hostname | 80 and 443 |
| `api` | Serves HTTP internally on port 8080 | None |
| `worker` | Hosts future background work without running placeholder jobs | None |
| `migrations` | Applies the EF migration bundle once, then exits | None |
| `postgres` | Stores application data in a named volume | None |

API and Worker start only after PostgreSQL is healthy and the migration bundle exits successfully. A migration failure therefore stops the deployment before application traffic is accepted. The API and Worker run as non-root users with read-only root filesystems.

### Local container workflow

Create the local environment file once:

```powershell
Copy-Item .env.example .env
```

Replace the example PostgreSQL password before starting the stack. The committed defaults expose Caddy at `http://localhost:8080`. The explicit local override additionally exposes PostgreSQL only on `127.0.0.1:5432` for development tools:

```powershell
docker compose --env-file .env -f compose.yaml -f compose.local.yaml config
docker compose --env-file .env -f compose.yaml -f compose.local.yaml build --pull api worker
docker compose --env-file .env -f compose.yaml -f compose.local.yaml up --detach
docker compose --env-file .env -f compose.yaml -f compose.local.yaml ps --all
```

The expected steady state is PostgreSQL, API, Worker, and Caddy running, with `migrations` showing `Exited (0)`. Useful commands are:

```powershell
Invoke-WebRequest http://localhost:8080/liveness
Invoke-WebRequest http://localhost:8080/readiness
Invoke-WebRequest http://localhost:8080/openapi/v1.json
docker compose --env-file .env -f compose.yaml -f compose.local.yaml logs --follow api worker
docker compose --env-file .env -f compose.yaml -f compose.local.yaml run --rm migrations
docker compose --env-file .env -f compose.yaml -f compose.local.yaml down
```

Do not add `--volumes` to the final command unless the local PostgreSQL and Caddy data must deliberately be deleted.

### First VPS deployment

The VPS requires a current Docker Engine and Docker Compose v2. Before deployment:

1. Create an `A` record for the API subdomain pointing to the VPS IPv4 address. Add an `AAAA` record only when IPv6 is configured on the VPS.
2. Allow inbound SSH, TCP 80, TCP 443, and UDP 443 in the VPS firewall. Do not expose ports 5432 or 8080.
3. Clone the repository, check out `develop`, and create the production environment file.

```bash
cp .env.example .env
chmod 600 .env
openssl rand -hex 32
```

Put the generated hexadecimal value in `POSTGRES_PASSWORD`, set `API_HOST` to the real hostname such as `api.example.fr`, and use ports 80 and 443:

```dotenv
API_HOST=api.example.fr
HTTP_PORT=80
HTTPS_PORT=443
POSTGRES_DB=mon_kado
POSTGRES_USER=mon_kado
POSTGRES_PASSWORD=<generated-hexadecimal-value>
IMAGE_TAG=local
```

The `.env` file is ignored by Git. Never commit or send it. Start the production stack without the local override:

```bash
docker compose --env-file .env -f compose.yaml config
docker compose --env-file .env -f compose.yaml build --pull api worker
docker compose --env-file .env -f compose.yaml up --detach --remove-orphans
docker compose --env-file .env -f compose.yaml ps --all
```

Caddy automatically obtains and renews the HTTPS certificate when the DNS record resolves to the VPS and ports 80 and 443 are reachable. Internal API and PostgreSQL traffic stays on Docker networks and does not use TLS.

For later deployments:

```bash
git pull --ff-only origin develop
docker compose --env-file .env -f compose.yaml build --pull api worker
docker compose --env-file .env -f compose.yaml up --detach --remove-orphans
docker compose --env-file .env -f compose.yaml ps --all
```

Inspect a failed deployment with:

```bash
docker compose --env-file .env -f compose.yaml logs migrations postgres
docker compose --env-file .env -f compose.yaml logs api worker caddy
docker compose --env-file .env -f compose.yaml run --rm migrations
docker compose --env-file .env -f compose.yaml exec caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
```

## PostgreSQL backup and restore

The `postgres_data` volume survives container recreation and ordinary `docker compose down`, but a persistent volume is not a backup. Copy backups away from the VPS according to the retention policy selected for production.

Create a compressed logical backup on the VPS:

```bash
mkdir -p backups
docker compose --env-file .env -f compose.yaml exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' > "backups/mon-kado-$(date -u +%Y%m%dT%H%M%SZ).dump"
```

Restore a reviewed backup during a maintenance window:

```bash
docker compose --env-file .env -f compose.yaml stop api worker
docker compose --env-file .env -f compose.yaml exec -T postgres sh -c 'pg_restore --clean --if-exists --no-owner -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < backups/mon-kado-backup.dump
docker compose --env-file .env -f compose.yaml start api worker
```

Test restore procedures regularly. PostgreSQL major-version upgrades require a reviewed dump/restore or `pg_upgrade` procedure. Never perform a major upgrade by changing only the image tag while reusing the existing data directory.
