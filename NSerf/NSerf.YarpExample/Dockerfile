FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
# Create snapshots directory and set ownership to app user (UID 1654)
RUN mkdir -p /serf/snapshots && chown -R 1654:1654 /serf

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["NSerf.YarpExample/NSerf.YarpExample.csproj", "NSerf.YarpExample/"]
COPY ["NSerf/NSerf.csproj", "NSerf/"]

# Restore dependencies
RUN dotnet restore "NSerf.YarpExample/NSerf.YarpExample.csproj"

# Copy everything else
COPY . .

# Build the application
WORKDIR "/src/NSerf.YarpExample"
RUN dotnet build "NSerf.YarpExample.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NSerf.YarpExample.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose proxy and Serf ports
EXPOSE 8080
EXPOSE 7946

ENTRYPOINT ["dotnet", "NSerf.YarpExample.dll"]
