# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /src

COPY ["src/Services/UrlService/UrlService.csproj", "src/Services/UrlService/"]
COPY ["src/Shared/Shared.Domain/Shared.Domain.csproj", "src/Shared/Shared.Domain/"]
COPY ["src/Shared/Shared/Shared.csproj", "src/Shared/Shared/"]
COPY ["src/Infrastructure/Infrastructure/Infrastructure.csproj", "src/Infrastructure/Infrastructure/"]

RUN dotnet restore "src/Services/UrlService/UrlService.csproj"

COPY . .
RUN dotnet build "src/Services/UrlService/UrlService.csproj" -c Release -o /app/build

# Install dotnet-ef in builder stage where SDK is available
RUN dotnet tool install --global dotnet-ef

# Publish stage
FROM builder AS publish
RUN dotnet publish "src/Services/UrlService/UrlService.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install netcat for service readiness checks
RUN apt-get update && \
    apt-get install -y curl netcat-openbsd && \
    rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PATH="/root/.dotnet/tools:${PATH}"

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "UrlService.dll"]