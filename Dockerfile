# Multi-stage image for Google Cloud Run (framework-dependent, not Native AOT).
# Build from the repo root:
#   docker build -t linux-helper .
#   docker run --rm -e PORT=8080 -p 8080:8080 linux-helper

# ---------- Stage 1: restore + publish ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/LinuxHelper/LinuxHelper.csproj", "src/LinuxHelper/"]
RUN dotnet restore "src/LinuxHelper/LinuxHelper.csproj"

COPY . .
RUN dotnet publish "src/LinuxHelper/LinuxHelper.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------- Stage 2: lean runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Default listen address for Cloud Run (PORT is also handled in Program.cs).
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

# Do not create a user with adduser — some runtime images are minimal and
# lack that tool (build exit 127). Cloud Run runs the container as non-root by default.

EXPOSE 8080

ENTRYPOINT ["dotnet", "LinuxHelper.dll"]
