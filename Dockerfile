# syntax=docker/dockerfile:1
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src
COPY . .
RUN dotnet restore src/CodexWidget.Web/CodexWidget.Web.csproj

FROM restore AS build
RUN dotnet build src/CodexWidget.Web/CodexWidget.Web.csproj -c Release --no-restore

FROM build AS publish
RUN dotnet publish src/CodexWidget.Web/CodexWidget.Web.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://0.0.0.0:8787 \
    CodexWidgetWeb__AllowLanBinding=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8787

COPY --from=publish /app/publish/ ./
RUN chmod -R a+rX /app
USER $APP_UID
ENTRYPOINT ["dotnet", "CodexWidget.Web.dll"]
