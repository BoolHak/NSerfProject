FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
# Install curl for health checks and create snapshots directory
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/* 

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["NSerf.BackendService/NSerf.BackendService.csproj", "NSerf.BackendService/"]
COPY ["NSerf/NSerf.csproj", "NSerf/"]

# Restore dependencies
RUN dotnet restore "NSerf.BackendService/NSerf.BackendService.csproj"

# Copy everything else
COPY . .

# Build the application
WORKDIR "/src/NSerf.BackendService"
RUN dotnet build "NSerf.BackendService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NSerf.BackendService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose HTTP and Serf ports
EXPOSE 5000
EXPOSE 7946

ENTRYPOINT ["dotnet", "NSerf.BackendService.dll"]
