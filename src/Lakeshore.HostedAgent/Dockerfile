# Build from the repo root:  docker build --platform linux/amd64 -f src/Lakeshore.HostedAgent/Dockerfile -t lakeshore-agent .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Lakeshore.Agents/Lakeshore.Agents.csproj src/Lakeshore.Agents/
COPY src/Lakeshore.HostedAgent/Lakeshore.HostedAgent.csproj src/Lakeshore.HostedAgent/
RUN dotnet restore src/Lakeshore.HostedAgent/Lakeshore.HostedAgent.csproj
COPY src/Lakeshore.Agents/ src/Lakeshore.Agents/
COPY src/Lakeshore.HostedAgent/ src/Lakeshore.HostedAgent/
RUN dotnet publish src/Lakeshore.HostedAgent/Lakeshore.HostedAgent.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql probes for GSSAPI on connect; without the library it logs a red error in the Foundry log stream.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8088
EXPOSE 8088
ENTRYPOINT ["dotnet", "Lakeshore.HostedAgent.dll"]
