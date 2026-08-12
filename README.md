# Mon Kado backend

Mon Kado is an API for creating, sharing, and managing gift wishlists.

The repository currently contains the technical baseline and PostgreSQL persistence foundation. Business endpoints, entities, and containerization are introduced by dedicated backlog items.

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

Tests are split by layer. Shared test helpers belong in `Tests.Common`; API startup and end-to-end HTTP behavior belong in `Api.IntegrationTests`.
