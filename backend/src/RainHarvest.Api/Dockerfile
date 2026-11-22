# Build context: backend/  (docker compose sets it)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so package downloads are cached until a project file changes.
COPY global.json Directory.Build.props ./
COPY src/RainHarvest.Contracts/RainHarvest.Contracts.csproj src/RainHarvest.Contracts/
COPY src/RainHarvest.Api/RainHarvest.Api.csproj src/RainHarvest.Api/
RUN dotnet restore src/RainHarvest.Api/RainHarvest.Api.csproj

COPY src/ src/
RUN dotnet publish src/RainHarvest.Api/RainHarvest.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# The aspnet base image listens on 8080 (ASPNETCORE_HTTP_PORTS).
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RainHarvest.Api.dll"]
