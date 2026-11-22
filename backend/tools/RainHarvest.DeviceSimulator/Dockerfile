# Build context: backend/  (docker compose sets it)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/RainHarvest.Contracts/RainHarvest.Contracts.csproj src/RainHarvest.Contracts/
COPY tools/RainHarvest.DeviceSimulator/RainHarvest.DeviceSimulator.csproj tools/RainHarvest.DeviceSimulator/
RUN dotnet restore tools/RainHarvest.DeviceSimulator/RainHarvest.DeviceSimulator.csproj

COPY src/RainHarvest.Contracts/ src/RainHarvest.Contracts/
COPY tools/RainHarvest.DeviceSimulator/ tools/RainHarvest.DeviceSimulator/
RUN dotnet publish tools/RainHarvest.DeviceSimulator/RainHarvest.DeviceSimulator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
# Arguments come from the compose "command:" (see docker-compose.yml).
ENTRYPOINT ["dotnet", "RainHarvest.DeviceSimulator.dll"]
