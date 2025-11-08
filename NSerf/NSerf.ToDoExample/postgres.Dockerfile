# PostgreSQL with NSerf agent running inside the same container
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy NSerf projects (paths relative to docker-compose context)
COPY ["NSerf/NSerf.csproj", "NSerf/"]
COPY ["NSerf.CLI/NSerf.CLI.csproj", "NSerf.CLI/"]
RUN dotnet restore "NSerf.CLI/NSerf.CLI.csproj"

# Copy source and build
COPY NSerf/ NSerf/
COPY NSerf.CLI/ NSerf.CLI/
WORKDIR /src/NSerf.CLI
RUN dotnet publish "NSerf.CLI.csproj" -c Release -o /app/publish --self-contained false

# Final stage - PostgreSQL with NSerf agent
FROM postgres:16-alpine

# Install .NET runtime, ICU libraries, and supervisor
RUN apk add --no-cache dotnet8-runtime icu-libs supervisor

# Copy NSerf CLI agent
COPY --from=build /app/publish /usr/local/nserf/

# Copy supervisord configuration and NSerf agent script
COPY NSerf.ToDoExample/supervisord.conf /etc/supervisord.conf
COPY NSerf.ToDoExample/nserf-agent.sh /usr/local/bin/nserf-agent.sh
RUN chmod +x /usr/local/bin/nserf-agent.sh

# Expose PostgreSQL and Serf ports
EXPOSE 5432 7946

CMD ["/usr/bin/supervisord", "-c", "/etc/supervisord.conf"]
