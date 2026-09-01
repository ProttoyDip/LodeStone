# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY . .
RUN dotnet restore src/Lodestone.Web/Lodestone.Web.csproj
RUN dotnet publish src/Lodestone.Web/Lodestone.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl openssl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/App_Data/ml /app/keys /https \
    && chown -R app:app /app /https

WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
COPY --chmod=0755 deployment/docker/docker-entrypoint.sh /usr/local/bin/lodestone-entrypoint
RUN sed -i 's/\r$//' /usr/local/bin/lodestone-entrypoint

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080 5001

HEALTHCHECK --interval=30s --timeout=5s --start-period=75s --retries=4 \
    CMD curl --fail --silent --show-error --insecure https://127.0.0.1:5001/health/ready || exit 1

ENTRYPOINT ["lodestone-entrypoint"]
