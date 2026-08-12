# ============================================================================
# Dockerfile for Elpis EdgeConnect
# ============================================================================
# Multi-stage Docker build that produces a minimal, secure runtime image.
#
# BUILD STRATEGY:
#   Stage 1 (build):  Uses the full .NET SDK to compile and publish the app.
#                     This stage is ~800MB but is discarded after the build.
#   Stage 2 (runtime): Uses the minimal ASP.NET runtime image (~100MB).
#                     Only the compiled app output is copied here.
#
# SECURITY FEATURES:
#   - Non-root user (appuser) — the process cannot modify system files
#   - Read-only filesystem except for /app/logs
#   - Alpine Linux base — minimal attack surface (fewer packages = fewer CVEs)
#   - No SDK or build tools in the runtime image
#
# BUILD & RUN:
#   $ docker build -t elpis-edgeconnect .
#   $ docker run -d --name edgeconnect \
#       -e EDGECONNECT_ENCRYPTION_KEY="your-key-here" \
#       -v ./config/appsettings.Production.json:/app/appsettings.Production.json:ro \
#       -v ./certs:/app/certs:ro \
#       -p 8080:8080 \
#       elpis-edgeconnect
#
# IMAGE SIZE: ~110MB (Alpine runtime + compiled .NET app)
# ============================================================================


# ===========================================================================
# STAGE 1: BUILD
# ===========================================================================
# Uses the full .NET 8 SDK on Alpine Linux for compiling and publishing.
# Alpine-based images are smaller (~200MB) than Debian-based ones (~700MB).
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build

# Set the working directory inside the build container.
# All subsequent COPY and RUN commands operate relative to this path.
WORKDIR /src

# ── Layer caching optimization ──
# Copy ONLY the .csproj file first and run 'dotnet restore'.
# Docker caches each layer — if the .csproj hasn't changed, the restore
# layer is reused (skipping the slow NuGet download). This saves minutes
# on rebuilds when only source code changes (not package references).
COPY src/ElpisEdgeConnect/ElpisEdgeConnect.csproj src/ElpisEdgeConnect/
RUN dotnet restore src/ElpisEdgeConnect/ElpisEdgeConnect.csproj

# ── Copy all source code and publish ──
# This layer is rebuilt whenever ANY source file changes.
# The 'dotnet publish' command compiles the app in Release mode and
# outputs the result to /app/publish.
COPY . .
RUN dotnet publish src/ElpisEdgeConnect/ElpisEdgeConnect.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false
    # --no-restore:         Skips restore since we already did it above (faster)
    # PublishSingleFile:    false = keep as separate DLLs (easier to debug)
    # PublishTrimmed:       false = don't remove unused code (safer, avoids reflection issues)


# ===========================================================================
# STAGE 2: RUNTIME
# ===========================================================================
# Uses the minimal ASP.NET runtime image (no SDK, no build tools).
# 'aspnet' image includes the ASP.NET Core shared framework needed for
# hosting, health checks, and Kestrel (used by the health check endpoint).
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime

# Set the working directory for the running application.
WORKDIR /app

# ── Security: Create and switch to a non-root user ──
# By default, Docker runs processes as root inside the container.
# Running as non-root prevents:
#   - The app from modifying system files if compromised
#   - Container escape exploits that require root privileges
#   - Accidental deletion of container configuration files
#
# addgroup -S:  Create a system group 'appgroup' (no password prompt)
# adduser -S:   Create a system user 'appuser' in 'appgroup' (no login shell)
RUN addgroup -S appgroup && adduser -S appuser -G appgroup

# Switch all subsequent commands (and the ENTRYPOINT) to run as 'appuser'
USER appuser

# ── Copy the compiled application from the build stage ──
# COPY --from=build copies files from the named build stage, not the host.
# Only the compiled output (/app/publish) is copied — no source code,
# no SDK, no NuGet cache. This keeps the runtime image minimal.
COPY --from=build /app/publish .

# ── Create the logs directory ──
# The application writes rolling log files here (configured in appsettings.json).
# Must be created as appuser since we've already switched to non-root.
RUN mkdir -p /app/logs

# ── Docker health check ──
# Docker (and orchestrators like Kubernetes) use this to determine
# if the container is healthy. The health check:
#   - Runs every 30 seconds (--interval)
#   - Times out after 5 seconds (--timeout)
#   - Marks unhealthy after 3 consecutive failures (--retries)
#   - Calls the /health endpoint on port 8080 using wget
#     (curl is not available on Alpine, wget is built-in)
#
# The health endpoint is served by the .NET health check middleware
# (enabled when AppSettings.EnableHealthChecks = true).
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD wget -qO- http://localhost:8080/health || exit 1

# ── Environment variables ──
# These can be overridden at runtime via docker run -e or docker-compose.yml.
#
# DOTNET_ENVIRONMENT: Tells .NET to load appsettings.Production.json
#                     (in addition to the base appsettings.json).
#                     Values: Development, Staging, Production
#
# EDGECONNECT_ENCRYPTION_KEY: The AES-256 key used to decrypt passwords
#                              stored as "enc:IV:CIPHERTEXT" in config.
#                              Generate with: dotnet run -- generate-key
#                              MUST be set if using encrypted passwords.
ENV DOTNET_ENVIRONMENT=Production
ENV EDGECONNECT_ENCRYPTION_KEY=""

# ── Container entry point ──
# Starts the .NET application using the dotnet runtime.
# Uses ENTRYPOINT (not CMD) so the process receives SIGTERM signals
# from Docker for graceful shutdown (StopAsync on hosted services).
#
# The application:
#   1. Loads configuration from appsettings.json + appsettings.Production.json
#   2. Starts the MQTT publisher (connects to broker)
#   3. Starts machine pollers (begins collecting data from CNC machines)
#   4. Runs until SIGTERM/SIGINT triggers graceful shutdown
ENTRYPOINT ["dotnet", "ElpisEdgeConnect.dll"]
