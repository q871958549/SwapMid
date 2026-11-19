# Multi-stage build for SwapMid.DbMigrator (build context: repository root)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY SwapMid.slnx common.props NuGet.Config ./
COPY src/SwapMid.Domain.Shared/SwapMid.Domain.Shared.csproj src/SwapMid.Domain.Shared/
COPY src/SwapMid.Domain/SwapMid.Domain.csproj src/SwapMid.Domain/
COPY src/SwapMid.Application.Contracts/SwapMid.Application.Contracts.csproj src/SwapMid.Application.Contracts/
COPY src/SwapMid.EntityFrameworkCore/SwapMid.EntityFrameworkCore.csproj src/SwapMid.EntityFrameworkCore/
COPY src/SwapMid.DbMigrator/SwapMid.DbMigrator.csproj src/SwapMid.DbMigrator/
RUN dotnet restore src/SwapMid.DbMigrator/SwapMid.DbMigrator.csproj

FROM restore AS build
COPY . .
RUN dotnet publish src/SwapMid.DbMigrator/SwapMid.DbMigrator.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS run
ARG RELEASE_SHA=unknown
WORKDIR /app
COPY --from=build /app/publish ./

LABEL org.opencontainers.image.title="SwapMid DbMigrator" \
      org.opencontainers.image.revision="$RELEASE_SHA"

ENTRYPOINT ["dotnet", "SwapMid.DbMigrator.dll"]
