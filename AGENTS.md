# Repository Guidelines

## Project Structure & Module Organization
- `src/API` hosts the ASP.NET Core entry point, controllers, and runtime configuration (Serilog, health checks).
- `src/Application` contains business orchestration (MediatR handlers, pipelines) and depends on `Domain`.
- `src/Domain` defines entities, value objects, and enums; keep it persistence-agnostic.
- `src/Infrastructure.Persistence.PostgreSql` implements PostgreSQL repositories; switch to `Infrastructure.Persistence.Mock` for in-memory builds.
- `tests` mirrors the layer structure (`*.UnitTests`, `Api.IntegrationTests`) plus `Tests.Common` for shared fixtures.

## Build, Test, and Development Commands
- `dotnet restore JennGllg.Fr.MonKado.Back.sln` downloads NuGet dependencies.
- `dotnet build JennGllg.Fr.MonKado.Back.sln -c Release` validates compilation and analyzers.
- `dotnet run --project src/API/Api.csproj` launches the API (uses `appsettings.local.json` when present).
- `dotnet test JennGllg.Fr.MonKado.Back.sln --configuration Release --collect:"XPlat Code Coverage"` runs all unit and integration suites with coverlet output.

## Coding Style & Naming Conventions
- Follow .NET defaults: 4-space indentation, file-scoped namespaces, `PascalCase` for types, and `camelCase` for locals.
- Treat the `Domain` layer as pure: no logging, I/O, or DI abstractions; crossing layers must go through Application commands/queries.
- Keep XML documentation and nullability annotations intact; run `dotnet format` before pushing if you have the tool installed.

## Testing Guidelines
- Use xUnit for new tests; place them alongside matching layer folders (e.g., `tests/Application.UnitTests/UseCases`).
- Mirror production namespace structure and suffix test classes with `Tests`.
- Prefer `Tests.Common` fixtures for PostgreSQL and mediator setup; integration tests rely on `appsettings.integrationTests.json`.
- Maintain coverage parity when touching modules; add regression tests for bug fixes.

## Commit & Pull Request Guidelines
- Commits follow `[MK-###] Short imperative summary`; include the tracker ID where applicable.
- Keep commits scoped to one vertical slice (domain change + tests).
- Pull requests should describe the change, note breaking impacts, link tickets, and include screenshots for API contract updates (e.g., swagger diffs).

## Environment & Configuration
- Default PostgreSQL connection lives in `appsettings.local.json`; override `PostgreSqlConfiguration:ConnectionString` via secrets in non-dev environments.
- Toggle mocked persistence through `PersistenceConfiguration:IsMocked`; integration tests expect it set to `false`.
- Log output is managed by Serilog; ensure structured properties are preserved when adding log statements.
