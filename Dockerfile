# syntax=docker/dockerfile:1
# Multi-stage build (handoff §11): Node compiles TypeScript, the .NET SDK publishes, and the final
# ASP.NET runtime image contains no Node/npm. Base images are pinned by digest; refresh deliberately.
ARG NODE_IMAGE=node:22-bookworm-slim@sha256:e9f565d32859f0cbfb52125eafe8be39390f24dac302dc3cdf566e8ba3ab21ee
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e

FROM ${NODE_IMAGE} AS client
WORKDIR /src
COPY package.json package-lock.json tsconfig.json ./
RUN npm ci --ignore-scripts --no-audit --no-fund
COPY ClientApp ./ClientApp
RUN npm run build:client

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/GovBidPro.Domain/GovBidPro.Domain.csproj src/GovBidPro.Domain/packages.lock.json src/GovBidPro.Domain/
COPY src/GovBidPro.Application/GovBidPro.Application.csproj src/GovBidPro.Application/packages.lock.json src/GovBidPro.Application/
COPY src/GovBidPro.Infrastructure/GovBidPro.Infrastructure.csproj src/GovBidPro.Infrastructure/packages.lock.json src/GovBidPro.Infrastructure/
COPY src/GovBidPro.Web/GovBidPro.Web.csproj src/GovBidPro.Web/packages.lock.json src/GovBidPro.Web/
RUN dotnet restore src/GovBidPro.Web/GovBidPro.Web.csproj --locked-mode
COPY src ./src
COPY --from=client /src/src/GovBidPro.Web/wwwroot/dist ./src/GovBidPro.Web/wwwroot/dist
RUN dotnet publish src/GovBidPro.Web/GovBidPro.Web.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GovBidPro.Web.dll"]
