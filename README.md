# SluiceBase

SluiceBase is a self-hosted database query gateway. It gives a team controlled, auditable access to databases — secured by OIDC authentication, per-user permissions, and an optional approval workflow for write operations.

## Features

- **OIDC authentication** — integrates with any OIDC provider (Keycloak, Auth0, Entra ID, etc.)
- **Permission model** — assign read/write permissions per user and database
- **Write approval workflow** — write requests require approval before execution
- **Query history** — all queries are logged with results
- **Schema browser** — explore table schemas directly in the UI
- **Customisable branding** — app name, colour, logo, and favicon configurable at runtime

## Architecture

A single Docker container runs the .NET 10 API which also serves the React frontend. The only external dependencies at runtime are a PostgreSQL database (metadata store) and an OIDC provider.

```
Browser → SluiceBase (React SPA + .NET 10 API) → PostgreSQL (metadata)
                                                 → Target databases
                                                 → OIDC provider
```

## Quick start

### Prerequisites

- Docker
- PostgreSQL 16+ (metadata store)
- An OIDC provider with a confidential client configured (see [OIDC setup](#oidc-setup))

### Docker Compose

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_USER: sluicebase
      POSTGRES_PASSWORD: changeme
      POSTGRES_DB: sluicebase
    volumes:
      - postgres-data:/var/lib/postgresql/data

  app:
    image: ghcr.io/yeongjonglim/sluice-base:latest
    ports:
      - "8080:8080"
    environment:
      ConnectionStrings__Metadata: "Host=postgres;Database=sluicebase;Username=sluicebase;Password=changeme"
      Oidc__Authority: "https://your-keycloak/realms/your-realm"
      Oidc__ClientId: "sluicebase"
      Oidc__ClientSecret: "your-client-secret"
      Permissions__Bootstrap__Admins__0: "admin@your-company.com"
    depends_on:
      - postgres

volumes:
  postgres-data:
```

### Run the container directly

```bash
docker run -p 8080:8080 \
  -e ConnectionStrings__Metadata="Host=...;Database=sluicebase;Username=...;Password=..." \
  -e Oidc__Authority="https://your-keycloak/realms/your-realm" \
  -e Oidc__ClientId="sluicebase" \
  -e Oidc__ClientSecret="your-client-secret" \
  ghcr.io/yeongjonglim/sluice-base:latest
```

The app is available at `http://localhost:8080`.

> **HTTPS:** deploy behind a reverse proxy (nginx, Caddy, Traefik) that terminates TLS. The container listens on HTTP only.

## Environment variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `ConnectionStrings__Metadata` | ✓ | — | PostgreSQL connection string for the SluiceBase metadata database |
| `Oidc__Authority` | ✓ | — | OIDC authority URL (e.g. `https://keycloak/realms/myrealm`) |
| `Oidc__ClientId` | ✓ | — | OIDC client ID |
| `Oidc__ClientSecret` | ✓ | — | OIDC client secret |
| `Migrations__AutoApply` | | `true` | Apply pending DB migrations on startup |
| `Permissions__Bootstrap__Admins__0` | | — | Email of the first admin (granted full permissions on first login) |
| `Branding__AppName` | | `SluiceBase` | Application name shown in the UI |
| `Branding__PrimaryColor` | | `teal` | Mantine colour — see [supported values](#branding-colours) |
| `Branding__LogoUrl` | | — | URL to a custom logo image — see [using local files](#using-local-branding-files) |
| `Branding__FaviconUrl` | | — | URL to a custom favicon — see [using local files](#using-local-branding-files) |
| `Query__TimeoutSeconds` | | `30` | Maximum query execution time in seconds |

### Branding colours

`Branding__PrimaryColor` accepts any Mantine built-in colour name:

`dark` `gray` `red` `pink` `grape` `violet` `indigo` `blue` `cyan` `teal` `green` `lime` `yellow` `orange`

### Using local branding files

Mount a directory into `/branding/` inside the container. Name the files `logo.<ext>` and `favicon.<ext>` — the API detects them automatically and serves them via `/api/branding/logo` and `/api/branding/favicon`. Do **not** set `LogoUrl`/`FaviconUrl` when using local files; those vars are only for remote `http(s)://` URLs.

Supported extensions: `.png` `.svg` `.jpg` `.jpeg` `.gif` `.webp` `.ico`

```yaml
# docker-compose.yml
services:
  app:
    image: ghcr.io/yeongjonglim/sluice-base:latest
    volumes:
      - ./branding:/branding:ro
    # Branding__LogoUrl and Branding__FaviconUrl are NOT set — local files are auto-detected
```

Place `logo.png` (and/or `favicon.ico`) in a local `./branding/` directory.

## Database migrations

Migrations are applied automatically on startup by default. Set `Migrations__AutoApply=false` to disable this behaviour.

To apply migrations manually using the .NET CLI:

```bash
dotnet ef database update \
  --project src/SluiceBase.Api \
  --connection "Host=...;Database=sluicebase;Username=...;Password=..."
```

## OIDC setup

Register a **confidential** OIDC client with these settings:

| Setting | Value |
|---|---|
| Client type | Confidential (authorization code + PKCE) |
| Redirect URI | `https://your-domain/signin-oidc` |
| Post-logout redirect URI | `https://your-domain/signout-callback-oidc` |
| Scopes | `openid`, `profile`, `email` |

The ID token must include the `sub`, `email`, and `name` claims.

**Keycloak:** create a realm, add a client with the settings above, and point `Oidc__Authority` to `https://your-keycloak/realms/your-realm`.

## Development setup

SluiceBase uses [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) to orchestrate all services locally, including Keycloak and PostgreSQL.

**Prerequisites:** .NET 10 SDK, Node.js 24, Docker

```bash
git clone https://github.com/yeongjonglim/sluice-base.git
cd sluice-base

dotnet run --project src/AppHost
```

The Aspire dashboard opens at `https://localhost:15888`. Once all services are healthy, use the **Seed Server Registry** command on the Metadata database resource to populate sample target databases.

## License

[MIT](LICENSE)
