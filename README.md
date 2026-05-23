> [!WARNING]
> This repository was mostly or entirely created through vibe coding: the code, documentation, and project structure were produced primarily by AI agents, with most or all work performed through the Codex app. Any review, cleanup, or validation may also have been performed by AI agents rather than by a human.
>
> The repository owner uses the resulting project, binaries, application, or other outputs personally and is sharing it because it works for their own use case. That does not mean the code is production-ready, secure, reliable, portable, or appropriate for your environment.
>
> Do not use, build, run, deploy, or copy code from this repository without a thorough independent review and without accepting the associated risks. The repository owner does not accept responsibility or liability for any damage, data loss, security issue, malfunction, or other harm caused by using this repository or anything derived from it.

# Codex Widget

Codex Widget is a .NET desktop and web status viewer for Codex profile and usage
state. It reads Codex profile data managed by
[codex-profiles](https://github.com/midhunmonachan/codex-profiles)/Codex from a
local home directory, summarizes account status, and renders it either as a small
desktop widget or as a browser UI with JSON endpoints.

## Status

This repository is in active development. The current codebase includes:

- an Avalonia desktop widget with tray menu, placement/settings persistence, and
  profile/usage refresh;
- an ASP.NET Core web host with static frontend, health checks, and status API;
- shared runtime, profile, usage, status, core, and presentation libraries;
- unit and integration-style tests for the shared logic, desktop shell, and web
  host contracts.

Known project goals are to keep status collection portable outside either UI,
avoid storing secrets in this repo, preserve explicit "unavailable" states, and
support both a local desktop widget and a deployable trusted-LAN web view.

## Stack

- .NET 10 / C#
- Avalonia 12 for the desktop app
- ASP.NET Core minimal APIs plus static HTML/CSS/JS for the web app
- Docker/Compose for containerized web hosting
- xUnit tests

## Data Source

The apps expect a Codex home layout containing `.codex/auth.json`,
`.codex/config.toml`, and `.codex/profiles/`. By default they use the current OS
user home directory. For alternate data, set the home directory path, not the
`.codex` directory itself.

Never commit real auth files, access tokens, refresh tokens, API keys, or local
`.codex` data.

## Build And Test

Prerequisite: .NET 10 SDK.

```bash
dotnet restore CodexWidget.slnx
dotnet build CodexWidget.slnx
dotnet test CodexWidget.slnx
```

## Desktop Widget

Run from source:

```bash
dotnet run --project src/CodexWidget.App/CodexWidget.App.csproj
```

Run with an alternate Codex home:

```bash
CODEX_PROFILES_HOME=/path/to/home-containing-dot-codex \
  dotnet run --project src/CodexWidget.App/CodexWidget.App.csproj
```

Publish a Windows build:

```bash
dotnet publish src/CodexWidget.App/CodexWidget.App.csproj \
  -c Release -r win-x64 --self-contained false
```

Launch the published executable from
`src/CodexWidget.App/bin/Release/net10.0/win-x64/publish/`.

## Web App

### Docker

Copy `docker-compose.env.example` to `.env` if overrides are needed, then set
`CODEX_WIDGET_DATA_DIR` to a host directory that contains `.codex/`.

```bash
docker compose up --build
```

Open `http://127.0.0.1:8787/`. Health checks are available at `/health` and
`/health/status`.

### Non-Docker

Run locally with the .NET SDK:

```bash
CodexWidgetWeb__CodexProfilesHome=/path/to/home-containing-dot-codex \
  dotnet run --project src/CodexWidget.Web/CodexWidget.Web.csproj
```

By default the web host binds to `http://127.0.0.1:8787`. To bind elsewhere, set
`ASPNETCORE_URLS`; non-loopback bindings also require
`CodexWidgetWeb__AllowLanBinding=true`.

Useful endpoints:

- `/` - browser UI
- `/api/status/presentation` - redacted presentation model
- `/api/status/snapshot` - redacted status snapshot
- `/api/status/refresh` - refresh metadata (`GET`) or manual refresh (`POST`)
