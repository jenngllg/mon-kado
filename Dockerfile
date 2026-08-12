# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY .config/dotnet-tools.json .config/
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Application/Application.csproj src/Application/
COPY src/Infrastructure.Persistence.PostgreSql/Infrastructure.Persistence.PostgreSql.csproj src/Infrastructure.Persistence.PostgreSql/
COPY src/API/Api.csproj src/API/
COPY src/Worker/Worker.csproj src/Worker/

RUN dotnet restore src/API/Api.csproj \
    && dotnet restore src/Worker/Worker.csproj \
    && dotnet tool restore

COPY src/ src/

RUN dotnet publish src/API/Api.csproj \
        --configuration Release \
        --no-restore \
        --output /out/api \
        /p:UseAppHost=false \
    && dotnet publish src/Worker/Worker.csproj \
        --configuration Release \
        --no-restore \
        --output /out/worker \
        /p:UseAppHost=false

RUN mkdir -p /out/data-protection-keys \
    && touch /out/data-protection-keys/.volume-init

FROM build AS migrations-build

RUN AllowedHosts=localhost \
    WebSecurity__AllowedOrigins__0=https://localhost \
    WebSecurity__DataProtectionKeysPath=/tmp/data-protection-keys \
    ConnectionStrings__PostgreSql="Host=127.0.0.1;Database=mon_kado;Username=mon_kado;Password=build-only" \
    dotnet ef migrations bundle \
        --project src/Infrastructure.Persistence.PostgreSql/Infrastructure.Persistence.PostgreSql.csproj \
        --startup-project src/API/Api.csproj \
        --context MonKadoDbContext \
        --configuration Release \
        --output /out/migrations/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled-extra AS api
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /out/api/ ./
COPY --from=migrations-build --chmod=0555 /out/migrations/efbundle ./efbundle
COPY --from=build --chown=$APP_UID:$APP_UID /out/data-protection-keys/ /var/lib/mon-kado/data-protection-keys/
USER $APP_UID
ENTRYPOINT ["dotnet", "JennGllg.Fr.MonKado.Back.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0.10-noble-chiseled-extra AS worker
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0
COPY --from=build /out/worker/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "JennGllg.Fr.MonKado.Back.Worker.dll"]
