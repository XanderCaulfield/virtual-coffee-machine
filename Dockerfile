# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first so NuGet restore is cached as a layer.
COPY Directory.Build.props ./
COPY src/CoffeeMachine.Contracts/CoffeeMachine.Contracts.csproj src/CoffeeMachine.Contracts/
COPY src/CoffeeMachine.Domain/CoffeeMachine.Domain.csproj src/CoffeeMachine.Domain/
COPY src/CoffeeMachine.Api/CoffeeMachine.Api.csproj src/CoffeeMachine.Api/
COPY src/CoffeeMachine.Client/CoffeeMachine.Client.csproj src/CoffeeMachine.Client/

RUN dotnet restore src/CoffeeMachine.Api/CoffeeMachine.Api.csproj

# Copy the full source and publish the API (this embeds the Blazor client
# via the Microsoft.AspNetCore.Components.WebAssembly.Server package).
COPY . .
RUN dotnet publish src/CoffeeMachine.Api/CoffeeMachine.Api.csproj -c Release -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# The aspnet:8.0 base image ships neither curl nor wget; install curl
# (before dropping privileges) for the HEALTHCHECK probe below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The container runs as the non-root `app` user, which cannot write to
# /app. Give it a writable data dir and point the SQLite database at it,
# so the app starts cleanly without any operator-provided DB_PATH.
RUN mkdir -p /data && chown app:app /data

# Run as the non-root `app` user built into the .NET 8 images.
USER app

ENV DB_PATH=/data/coffeemachine.db
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# /healthz is implemented by the API (see src/CoffeeMachine.Api/Program.cs).
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "CoffeeMachine.Api.dll"]
