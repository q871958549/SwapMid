# Multi-stage build for SwapMid.HttpApi.Host (build context: repository root)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY SwapMid.slnx common.props NuGet.Config ./
COPY src/SwapMid.Domain.Shared/SwapMid.Domain.Shared.csproj src/SwapMid.Domain.Shared/
COPY src/SwapMid.Domain/SwapMid.Domain.csproj src/SwapMid.Domain/
COPY src/SwapMid.Application.Contracts/SwapMid.Application.Contracts.csproj src/SwapMid.Application.Contracts/
COPY src/SwapMid.Application/SwapMid.Application.csproj src/SwapMid.Application/
COPY src/SwapMid.EntityFrameworkCore/SwapMid.EntityFrameworkCore.csproj src/SwapMid.EntityFrameworkCore/
COPY src/SwapMid.HttpApi/SwapMid.HttpApi.csproj src/SwapMid.HttpApi/
COPY src/SwapMid.HttpApi.Host/SwapMid.HttpApi.Host.csproj src/SwapMid.HttpApi.Host/
RUN dotnet restore src/SwapMid.HttpApi.Host/SwapMid.HttpApi.Host.csproj

FROM restore AS build
COPY . .
RUN dotnet publish src/SwapMid.HttpApi.Host/SwapMid.HttpApi.Host.csproj -c Release -o /app/publish --no-restore
ARG OPENIDDICT_PFX_PASS=changeit
ARG OPENIDDICT_PFX_FILE=docker/openiddict/openiddict.pfx
ARG OPENIDDICT_PFX_MOUNT_AT_RUNTIME=false
RUN if [ "$OPENIDDICT_PFX_MOUNT_AT_RUNTIME" = "true" ]; then \
      echo "OpenIddict certificate will be mounted at runtime"; \
    elif [ -f "/src/$OPENIDDICT_PFX_FILE" ]; then \
      cp "/src/$OPENIDDICT_PFX_FILE" /app/publish/openiddict.pfx; \
    else \
      dotnet dev-certs https -ep /app/publish/openiddict.pfx -p "$OPENIDDICT_PFX_PASS"; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS run
ARG RELEASE_SHA=unknown
WORKDIR /app
COPY --from=build /app/publish ./

LABEL org.opencontainers.image.title="SwapMid API" \
      org.opencontainers.image.revision="$RELEASE_SHA" \
      org.swapmid.capability.health-live="v1" \
      org.swapmid.etl-execution-protocol="1"

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SwapMid.HttpApi.Host.dll"]
