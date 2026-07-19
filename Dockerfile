# Multi-stage image for Google Cloud Run (framework-dependent, not Native AOT).
# Build from the repo root:
#   docker build -t linux-helper .
#   docker run --rm -e PORT=8080 -p 8080:8080 linux-helper

# ---------- Stage 1: restore + publish ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first for better layer caching of restore
COPY ["src/LinuxHelper/LinuxHelper.csproj", "src/LinuxHelper/"]
RUN dotnet restore "src/LinuxHelper/LinuxHelper.csproj"

# Copy the rest of the source and publish
COPY . .
RUN dotnet publish "src/LinuxHelper/LinuxHelper.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------- Stage 2: lean runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Cloud Run sends traffic to $PORT (default 8080). Prefer binding via env at runtime.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Non-root user (recommended for Cloud Run)
RUN adduser --disabled-password --gecos "" appuser \
    && chown -R appuser /app
USER appuser

# Document the default Cloud Run port
EXPOSE 8080

ENTRYPOINT ["dotnet", "LinuxHelper.dll"]
