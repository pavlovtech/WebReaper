# WebReaper.Mcp: the stdio MCP server (scrape / map / crawl / extract /
# extract_with_prompt / extract_inferred). This is the server named in
# server.json and on the MCP registry; Glama builds this image in its sandbox
# and runs the MCP introspection handshake (tools/list) against it.
#
# Introspection needs no network and no browser, so this image stays minimal:
# the default HTTP scrape / map / crawl / extract paths work out of the box.
# browser=true needs a Chromium on PATH; see WebReaper.Mcp.AspNetCore/Dockerfile
# for the browser-capable (Debian + Chromium) variant.
#
# Build context = the REPO ROOT (the server references the library projects):
#
#   docker build -f WebReaper.Mcp/Dockerfile -t webreaper-mcp .

# --- build (SDK tag matches global.json) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore as its own layer: shared build props + the csproj graph only
# (Mcp -> WebReaper / Cdp / AI / AI.Http), so a source-only edit skips restore.
COPY global.json Directory.Build.props ./
COPY WebReaper/WebReaper.csproj WebReaper/
COPY WebReaper.Cdp/WebReaper.Cdp.csproj WebReaper.Cdp/
COPY WebReaper.AI/WebReaper.AI.csproj WebReaper.AI/
COPY WebReaper.AI.Http/WebReaper.AI.Http.csproj WebReaper.AI.Http/
COPY WebReaper.Mcp/WebReaper.Mcp.csproj WebReaper.Mcp/
RUN dotnet restore WebReaper.Mcp/WebReaper.Mcp.csproj
# Sources, then framework-dependent publish.
COPY WebReaper/ WebReaper/
COPY WebReaper.Cdp/ WebReaper.Cdp/
COPY WebReaper.AI/ WebReaper.AI/
COPY WebReaper.AI.Http/ WebReaper.AI.Http/
COPY WebReaper.Mcp/ WebReaper.Mcp/
RUN dotnet publish WebReaper.Mcp/WebReaper.Mcp.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# --- runtime: the .NET 10 base runtime is all a stdio host needs ---
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app ./
# MCP is JSON-RPC over stdio; Program.cs routes logs to stderr so stdout stays a
# clean protocol channel.
ENTRYPOINT ["dotnet", "WebReaper.Mcp.dll"]
