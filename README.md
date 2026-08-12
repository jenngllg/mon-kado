# Mon Kado backend

Mon Kado is an API for creating, sharing, and managing gift wishlists.

The repository currently contains the technical baseline only. Business endpoints, PostgreSQL persistence, OpenAPI, and containerization are introduced by dedicated backlog items.

## Prerequisites

- [.NET SDK 10.0.302 or a later .NET 10 feature band](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 when using the Visual Studio IDE. Visual Studio 2022 cannot build projects targeting `net10.0`; the .NET CLI or a compatible editor such as VS Code can be used instead.

The repository-level `global.json` selects .NET 10 and allows roll-forward to a later installed .NET 10 feature band. Preview SDKs are not accepted.

## Build and test

```powershell
dotnet restore JennGllg.Fr.MonKado.Back.slnx
dotnet build JennGllg.Fr.MonKado.Back.slnx --configuration Release --no-restore
dotnet test JennGllg.Fr.MonKado.Back.slnx --configuration Release --no-build --no-restore
dotnet format JennGllg.Fr.MonKado.Back.slnx --verify-no-changes --no-restore
dotnet list JennGllg.Fr.MonKado.Back.slnx package --vulnerable --include-transitive
```

## Run locally

```powershell
dotnet run --project src/API/Api.csproj --launch-profile http
```

The local launch profile listens on `http://localhost:7000` and uses the `Local` environment.

| Endpoint | Purpose |
|---|---|
| `GET /liveness` | Confirms that the API process is alive |
| `GET /readiness` | Confirms that the API is ready to receive traffic |

Until PostgreSQL is introduced, readiness has no external dependency check.

## Architecture

The solution keeps four production layers:

- `API`: HTTP composition root and transport concerns.
- `Application`: use cases and MediatR handlers.
- `Domain`: business rules with no dependency on other solution projects.
- `Infrastructure.Persistence.PostgreSql`: PostgreSQL implementation, intentionally empty until persistence is implemented.

Tests are split by layer. Shared test helpers belong in `Tests.Common`; API startup and end-to-end HTTP behavior belong in `Api.IntegrationTests`.
