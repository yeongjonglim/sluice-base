# Connect AI Tools (MCP) UI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an in-app "Connect AI tools" modal that gives copy-ready, per-client instructions for connecting an AI coding agent to this SluiceBase instance's MCP server.

**Architecture:** A subtle header trigger opens a Mantine `Modal` with a client `Tabs` selector. Snippet/deeplink construction lives in a pure-function data module (`mcpClients.ts`) keyed off a `McpConnectionContext` (live endpoint + operator-configured server name). The server name and an enabled flag are injected into the existing `window.__BRANDING__` global by the backend; the trigger renders only when MCP is enabled.

**Tech Stack:** React + TypeScript, Mantine (`@mantine/core`: `Modal`, `Tabs`, `Code`, `CopyButton`, `ActionIcon`, `Tooltip`), Vitest + Testing Library; ASP.NET Core middleware + `IOptions<McpOptions>`, xUnit.

## Global Constraints

- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- Frontend: no new npm dependency — use `Code` + `CopyButton` from `@mantine/core` (do **not** add `@mantine/code-highlight`).
- Branding is injected server-side into `window.__BRANDING__` at `BrandingHtmlMiddleware.cs`; the frontend reads it via `BrandingContext`.
- `mcpEnabled` defaults to **false** on the frontend when absent, so a page served without injection never shows instructions for a disabled server.
- MCP endpoint is always `window.location.origin + "/mcp"`.
- Default server name is `"sluicebase"`; operator override via `Mcp__ServerName`, validated `^[A-Za-z0-9_-]+$` server-side (fall back to `sluicebase` on invalid).
- Backend: suppress experimental API warnings with inline `#pragma`, never `<NoWarn>` (not expected here). Config reads stay at options/injection sites — no Npgsql/domain coupling.
- Commit messages: single subject line, no body. Work stays on branch `feat/connect-ai-tools-mcp-ui`.
- Client tabs, in order: Claude Code, Cursor, VS Code, GitHub Copilot, Codex.

---

### Task 1: Configurable, validated MCP server name (`McpOptions`)

**Files:**
- Modify: `src/SluiceBase.Api/Mcp/McpOptions.cs`
- Test: `tests/SluiceBase.Api.Tests/McpOptionsTests.cs` (create)

**Interfaces:**
- Produces: `McpOptions.ServerName` (string, default `"sluicebase"`) and `McpOptions.GetValidatedServerName(ILogger? logger = null)` returning a safe identifier.

- [ ] **Step 1: Write the failing test**

Create `tests/SluiceBase.Api.Tests/McpOptionsTests.cs`:

```csharp
using SluiceBase.Api.Mcp;

namespace SluiceBase.Api.Tests;

public class McpOptionsTests
{
    [Fact]
    public void GetValidatedServerName_DefaultsToSluicebase()
    {
        var options = new McpOptions();

        Assert.Equal("sluicebase", options.GetValidatedServerName());
    }

    [Theory]
    [InlineData("acme-db")]
    [InlineData("acme_db")]
    [InlineData("AcmeDB2")]
    public void GetValidatedServerName_ReturnsValidIdentifierUnchanged(string name)
    {
        var options = new McpOptions { ServerName = name };

        Assert.Equal(name, options.GetValidatedServerName());
    }

    [Theory]
    [InlineData("acme db")]
    [InlineData("acme.db")]
    [InlineData("")]
    [InlineData("bad/name")]
    public void GetValidatedServerName_FallsBackOnInvalidIdentifier(string name)
    {
        var options = new McpOptions { ServerName = name };

        Assert.Equal("sluicebase", options.GetValidatedServerName());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SluiceBase.Api.Tests --filter FullyQualifiedName~McpOptionsTests`
Expected: FAIL — `McpOptions` has no `ServerName` / `GetValidatedServerName` (compile error).

- [ ] **Step 3: Implement `ServerName` + validation**

Replace the contents of `src/SluiceBase.Api/Mcp/McpOptions.cs` with:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SluiceBase.Api.Mcp;

internal sealed partial class McpOptions
{
    public const string SectionName = "Mcp";
    public bool Enabled { get; set; } = true;
    public string ServerName { get; set; } = "sluicebase";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int AuthCodeSeconds { get; set; } = 120;

    // The server name becomes a client-side alias used verbatim in a TOML table
    // name ([mcp_servers.<name>]) and JSON keys, so it must be a safe identifier.
    public string GetValidatedServerName(ILogger? logger = null)
    {
        if (!string.IsNullOrEmpty(ServerName) && ServerNameRegex().IsMatch(ServerName))
        {
            return ServerName;
        }

        logger?.WarningInvalidServerName(ServerName);
        return "sluicebase";
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex ServerNameRegex();
}

internal static partial class McpLoggerMessage
{
    [LoggerMessage(
        LogLevel.Warning,
        Message = "Mcp:ServerName '{ServerName}' is not a valid identifier (allowed: letters, digits, '-', '_'). Falling back to 'sluicebase'.")]
    public static partial void WarningInvalidServerName(this ILogger logger, string serverName);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SluiceBase.Api.Tests --filter FullyQualifiedName~McpOptionsTests`
Expected: PASS (7 cases).

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Mcp/McpOptions.cs tests/SluiceBase.Api.Tests/McpOptionsTests.cs
git commit -m "Add configurable, validated Mcp__ServerName option"
```

---

### Task 2: Inject `mcpEnabled` + `mcpServerName` into `window.__BRANDING__`

**Files:**
- Modify: `src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs`

**Interfaces:**
- Consumes: `McpOptions.Enabled`, `McpOptions.GetValidatedServerName(ILogger)` (Task 1).
- Produces: `window.__BRANDING__` now includes `mcpEnabled: boolean` and `mcpServerName: string`.

- [ ] **Step 1: Add the `McpOptions` dependency**

In `src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs`, add the using and constructor parameter. Change the class signature block:

```csharp
using SluiceBase.Api.Mcp;
using SluiceBase.Core.Branding;
```

```csharp
internal sealed partial class BrandingHtmlMiddleware(
    RequestDelegate next,
    IOptions<BrandingOptions> options,
    IOptions<McpOptions> mcpOptions,
    IWebHostEnvironment env,
    IHttpClientFactory httpClientFactory,
    ILogger<BrandingHtmlMiddleware> logger)
```

(Keep the existing `using System.Net;`, `using System.Text.Json;`, `using System.Text.RegularExpressions;`, and `using Microsoft.Extensions.Options;` lines.)

- [ ] **Step 2: Include the MCP fields in the injected JSON**

In `InjectBranding`, replace the `brandingJson` assignment:

```csharp
        var mcp = mcpOptions.Value;

        var brandingJson = JsonSerializer.Serialize(
            new
            {
                branding.AppName,
                primaryColor,
                logoUrl,
                faviconUrl,
                mcpEnabled = mcp.Enabled,
                mcpServerName = mcp.GetValidatedServerName(logger),
            },
            JsonOptions);
```

- [ ] **Step 3: Verify the backend builds**

Run: `dotnet build SluiceBase.slnx`
Expected: Build succeeded, 0 warnings (warnings-as-errors).

- [ ] **Step 4: Manually confirm the injected shape (dev)**

Run: `grep -n "mcpEnabled\|mcpServerName" src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs`
Expected: both keys present in the anonymous object passed to `JsonSerializer.Serialize`. (The camelCase policy already configured emits `mcpEnabled` / `mcpServerName`.)

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs
git commit -m "Inject mcpEnabled and mcpServerName into window.__BRANDING__"
```

---

### Task 3: Surface `mcpEnabled` + `mcpServerName` through `BrandingContext`

**Files:**
- Modify: `src/frontend/src/theme/BrandingContext.tsx`
- Modify: `src/frontend/src/main.tsx`
- Test: `src/frontend/src/theme/__tests__/BrandingContext.test.tsx`

**Interfaces:**
- Produces: `BrandingValue` gains `mcpEnabled: boolean` and `mcpServerName: string`; `useBranding()` returns them.

- [ ] **Step 1: Write the failing test**

Append to `src/frontend/src/theme/__tests__/BrandingContext.test.tsx` a test asserting the defaults. First check the existing file's imports; add this test inside the existing top-level `describe` (or add a new one). Use the exported `BrandingContext` default:

```tsx
import { BrandingContext, useBranding } from "@/theme/BrandingContext";
import { renderHook } from "@testing-library/react";

it("defaults mcpEnabled to false and mcpServerName to sluicebase", () => {
  const { result } = renderHook(() => useBranding());

  expect(result.current.mcpEnabled).toBe(false);
  expect(result.current.mcpServerName).toBe("sluicebase");
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/frontend && npx vitest run src/theme/__tests__/BrandingContext.test.tsx`
Expected: FAIL — `mcpEnabled` / `mcpServerName` are `undefined` / not on the type.

- [ ] **Step 3: Extend `BrandingValue` and the default**

Replace `src/frontend/src/theme/BrandingContext.tsx` with:

```tsx
import { createContext, useContext } from "react";

export interface BrandingValue {
  appName: string;
  logoUrl: string | null;
  faviconUrl: string | null;
  mcpEnabled: boolean;
  mcpServerName: string;
}

const DEFAULT_BRANDING: BrandingValue = {
  appName: "SluiceBase",
  logoUrl: null,
  faviconUrl: null,
  mcpEnabled: false,
  mcpServerName: "sluicebase",
};

export const BrandingContext = createContext<BrandingValue>(DEFAULT_BRANDING);

export function useBranding(): BrandingValue {
  return useContext(BrandingContext);
}
```

- [ ] **Step 4: Map the injected globals in `main.tsx`**

In `src/frontend/src/main.tsx`, extend the `Window.__BRANDING__` type and the `brandingValue` mapping:

```tsx
declare global {
  interface Window {
    __BRANDING__?: {
      appName: string;
      primaryColor: string;
      logoUrl: string | null;
      faviconUrl: string | null;
      mcpEnabled?: boolean;
      mcpServerName?: string;
    };
  }
}
```

```tsx
const brandingValue: BrandingValue = {
  appName: branding?.appName ?? "SluiceBase",
  logoUrl: branding?.logoUrl ?? null,
  faviconUrl: branding?.faviconUrl ?? null,
  mcpEnabled: branding?.mcpEnabled ?? false,
  mcpServerName: branding?.mcpServerName ?? "sluicebase",
};
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd src/frontend && npx vitest run src/theme/__tests__/BrandingContext.test.tsx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/theme/BrandingContext.tsx src/frontend/src/main.tsx src/frontend/src/theme/__tests__/BrandingContext.test.tsx
git commit -m "Expose mcpEnabled and mcpServerName via BrandingContext"
```

---

### Task 4: Client data module (`mcpClients.ts`)

**Files:**
- Create: `src/frontend/src/components/mcp/mcpClients.ts`
- Test: `src/frontend/src/components/mcp/__tests__/mcpClients.test.ts` (create)

**Interfaces:**
- Produces:
  - `interface McpConnectionContext { endpoint: string; serverName: string }`
  - `interface McpClient { id: string; label: string; icon: TablerIcon; snippetLang: string; buildSnippet(ctx: McpConnectionContext): string; buildDeeplink?(ctx: McpConnectionContext): string; deeplinkLabel?: string; authNote: string }`
  - `const MCP_CLIENTS: Array<McpClient>`

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/components/mcp/__tests__/mcpClients.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { MCP_CLIENTS } from "@/components/mcp/mcpClients";

const ctx = { endpoint: "https://acme.example.com/mcp", serverName: "acme-db" };

describe("MCP_CLIENTS", () => {
  it("includes the five expected clients in order", () => {
    expect(MCP_CLIENTS.map((c) => c.id)).toEqual([
      "claude-code",
      "cursor",
      "vscode",
      "copilot",
      "codex",
    ]);
  });

  it("bakes the endpoint and server name into every snippet", () => {
    for (const client of MCP_CLIENTS) {
      const snippet = client.buildSnippet(ctx);
      expect(snippet).toContain(ctx.endpoint);
      expect(snippet).toContain(ctx.serverName);
    }
  });

  it("builds a Cursor deeplink whose base64 config decodes to the endpoint", () => {
    const cursor = MCP_CLIENTS.find((c) => c.id === "cursor");
    const link = cursor?.buildDeeplink?.(ctx) ?? "";
    expect(link).toContain("cursor://anysphere.cursor-deeplink/mcp/install");

    const config = new URL(link).searchParams.get("config") ?? "";
    const decoded = JSON.parse(atob(config)) as { url: string };
    expect(decoded.url).toBe(ctx.endpoint);
  });

  it("builds a VS Code deeplink containing the endpoint", () => {
    const vscode = MCP_CLIENTS.find((c) => c.id === "vscode");
    const link = vscode?.buildDeeplink?.(ctx) ?? "";
    expect(link).toContain("vscode:mcp/install");
    expect(decodeURIComponent(link)).toContain(ctx.endpoint);
  });

  it("only Cursor and VS Code provide deeplinks", () => {
    const withDeeplink = MCP_CLIENTS.filter((c) => c.buildDeeplink).map((c) => c.id);
    expect(withDeeplink).toEqual(["cursor", "vscode"]);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/frontend && npx vitest run src/components/mcp/__tests__/mcpClients.test.ts`
Expected: FAIL — module `@/components/mcp/mcpClients` does not exist.

- [ ] **Step 3: Implement `mcpClients.ts`**

Create `src/frontend/src/components/mcp/mcpClients.ts`:

```ts
import {
  IconBrandVscode,
  IconRobot,
  IconSparkles,
  IconTerminal2,
} from "@tabler/icons-react";
import type { IconProps } from "@tabler/icons-react";
import type { ComponentType } from "react";

type TablerIcon = ComponentType<IconProps>;

export interface McpConnectionContext {
  /** Live MCP endpoint, e.g. https://acme.example.com/mcp */
  endpoint: string;
  /** Operator-configured client alias, validated server-side. */
  serverName: string;
}

export interface McpClient {
  id: string;
  label: string;
  icon: TablerIcon;
  /** Display hint for the snippet block. */
  snippetLang: string;
  buildSnippet(ctx: McpConnectionContext): string;
  buildDeeplink?(ctx: McpConnectionContext): string;
  deeplinkLabel?: string;
  authNote: string;
}

export const MCP_CLIENTS: Array<McpClient> = [
  {
    id: "claude-code",
    label: "Claude Code",
    icon: IconSparkles,
    snippetLang: "bash",
    buildSnippet: ({ endpoint, serverName }) =>
      `claude mcp add --transport http ${serverName} ${endpoint}`,
    authNote:
      "Then run /mcp in Claude Code and select the server → Authenticate. Your usual login opens in the browser — no extra credentials.",
  },
  {
    id: "cursor",
    label: "Cursor",
    icon: IconRobot,
    snippetLang: "json",
    buildSnippet: ({ endpoint, serverName }) =>
      `{\n  "mcpServers": {\n    "${serverName}": {\n      "url": "${endpoint}"\n    }\n  }\n}`,
    buildDeeplink: ({ endpoint, serverName }) => {
      const config = btoa(JSON.stringify({ url: endpoint }));
      return `cursor://anysphere.cursor-deeplink/mcp/install?name=${encodeURIComponent(
        serverName,
      )}&config=${config}`;
    },
    deeplinkLabel: "Add to Cursor",
    authNote: "Cursor runs the sign-in flow on first use — your usual login opens in the browser.",
  },
  {
    id: "vscode",
    label: "VS Code",
    icon: IconBrandVscode,
    snippetLang: "bash",
    buildSnippet: ({ endpoint, serverName }) =>
      `code --add-mcp '{"name":"${serverName}","type":"http","url":"${endpoint}"}'`,
    buildDeeplink: ({ endpoint, serverName }) => {
      const config = JSON.stringify({ name: serverName, type: "http", url: endpoint });
      return `vscode:mcp/install?${encodeURIComponent(config)}`;
    },
    deeplinkLabel: "Add to VS Code",
    authNote: "Start the server from the MCP view; sign in with your usual login on first use.",
  },
  {
    id: "copilot",
    label: "GitHub Copilot",
    icon: IconBrandVscode,
    snippetLang: "json",
    buildSnippet: ({ endpoint, serverName }) =>
      `{\n  "servers": {\n    "${serverName}": {\n      "type": "http",\n      "url": "${endpoint}"\n    }\n  }\n}`,
    authNote:
      "Save this as .vscode/mcp.json, start the server, and sign in with your usual login on first use.",
  },
  {
    id: "codex",
    label: "Codex",
    icon: IconTerminal2,
    snippetLang: "toml",
    buildSnippet: ({ endpoint, serverName }) =>
      `[mcp_servers.${serverName}]\ntransport = "http"\nurl = "${endpoint}"`,
    authNote: "Codex runs the sign-in flow on first use — your usual login opens in the browser.",
  },
];
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/frontend && npx vitest run src/components/mcp/__tests__/mcpClients.test.ts`
Expected: PASS (5 cases).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/mcp/mcpClients.ts src/frontend/src/components/mcp/__tests__/mcpClients.test.ts
git commit -m "Add MCP client snippet and deeplink builders"
```

---

### Task 5: `ConnectMcpModal` component

**Files:**
- Create: `src/frontend/src/components/mcp/ConnectMcpModal.tsx`
- Test: `src/frontend/src/components/mcp/__tests__/ConnectMcpModal.test.tsx` (create)

**Interfaces:**
- Consumes: `MCP_CLIENTS`, `McpConnectionContext` (Task 4); `useBranding()` (Task 3).
- Produces: `function ConnectMcpModal(props: { opened: boolean; onClose: () => void }): JSX.Element`.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/components/mcp/__tests__/ConnectMcpModal.test.tsx`:

```tsx
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { ConnectMcpModal } from "@/components/mcp/ConnectMcpModal";
import { BrandingContext } from "@/theme/BrandingContext";

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(), removeListener: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

// Match against the rendered <code> block only, so parent elements that also
// contain the text don't cause a multiple-match error. Endpoint is derived from
// jsdom's own origin rather than overriding window.location (which throws in jsdom).
function codeBlockWith(substr: string) {
  return (_: string, el: Element | null) =>
    el?.tagName === "CODE" && (el.textContent ?? "").includes(substr);
}

function renderModal() {
  const branding = {
    appName: "Acme",
    logoUrl: null,
    faviconUrl: null,
    mcpEnabled: true,
    mcpServerName: "acme-db",
  };
  return render(
    <MantineProvider>
      <BrandingContext value={branding}>
        <ConnectMcpModal opened onClose={vi.fn()} />
      </BrandingContext>
    </MantineProvider>,
  );
}

describe("ConnectMcpModal", () => {
  it("shows a tab per client with the Claude Code snippet using the live endpoint and name", () => {
    renderModal();

    expect(screen.getByRole("tab", { name: /claude code/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /cursor/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /vs code/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /github copilot/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /codex/i })).toBeInTheDocument();

    const endpoint = `${window.location.origin}/mcp`;
    expect(
      screen.getByText(codeBlockWith(`claude mcp add --transport http acme-db ${endpoint}`)),
    ).toBeInTheDocument();
  });

  it("switches the snippet when another client tab is selected", async () => {
    renderModal();
    const user = userEvent.setup();

    await user.click(screen.getByRole("tab", { name: /codex/i }));

    expect(screen.getByText(codeBlockWith("[mcp_servers.acme-db]"))).toBeInTheDocument();
  });

  it("lists the three MCP tools", () => {
    renderModal();

    expect(screen.getByText(/list_databases/)).toBeInTheDocument();
    expect(screen.getByText(/get_schema/)).toBeInTheDocument();
    expect(screen.getByText(/run_query/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/frontend && npx vitest run src/components/mcp/__tests__/ConnectMcpModal.test.tsx`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement `ConnectMcpModal.tsx`**

Create `src/frontend/src/components/mcp/ConnectMcpModal.tsx`:

```tsx
import {
  ActionIcon,
  Anchor,
  Button,
  Code,
  CopyButton,
  Group,
  Modal,
  Stack,
  Tabs,
  Text,
  Tooltip,
} from "@mantine/core";
import {
  IconCheck,
  IconClipboard,
  IconExternalLink,
  IconEye,
  IconListCheck,
  IconUser,
} from "@tabler/icons-react";
import { useMemo, useState } from "react";
import type { McpConnectionContext } from "@/components/mcp/mcpClients";
import { MCP_CLIENTS } from "@/components/mcp/mcpClients";
import { useBranding } from "@/theme/BrandingContext";

interface TrustItem {
  icon: typeof IconUser;
  label: string;
}

const TRUST_ITEMS: Array<TrustItem> = [
  { icon: IconUser, label: "Runs as you" },
  { icon: IconEye, label: "Your permissions & sensitive-column screening" },
  { icon: IconListCheck, label: "Every query audited" },
];

export function ConnectMcpModal({
  opened,
  onClose,
}: {
  opened: boolean;
  onClose: () => void;
}) {
  const { mcpServerName } = useBranding();
  const [active, setActive] = useState<string>(MCP_CLIENTS[0].id);

  const ctx: McpConnectionContext = useMemo(
    () => ({ endpoint: `${window.location.origin}/mcp`, serverName: mcpServerName }),
    [mcpServerName],
  );

  return (
    <Modal opened={opened} onClose={onClose} title="Connect AI tools" size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Point your AI coding agent at {window.location.host}. The agent connects with your
          identity, not a shared key.
        </Text>

        <Group gap="lg" wrap="wrap">
          {TRUST_ITEMS.map((item) => (
            <Group key={item.label} gap={6} wrap="nowrap">
              <item.icon size={16} />
              <Text size="xs" fw={500}>
                {item.label}
              </Text>
            </Group>
          ))}
        </Group>

        <Tabs value={active} onChange={(value) => value && setActive(value)}>
          <Tabs.List>
            {MCP_CLIENTS.map((client) => (
              <Tabs.Tab
                key={client.id}
                value={client.id}
                leftSection={<client.icon size={14} />}
              >
                {client.label}
              </Tabs.Tab>
            ))}
          </Tabs.List>

          {MCP_CLIENTS.map((client) => {
            const snippet = client.buildSnippet(ctx);
            const deeplink = client.buildDeeplink?.(ctx);
            return (
              <Tabs.Panel key={client.id} value={client.id} pt="md">
                <Stack gap="sm">
                  <Text size="sm" fw={600}>
                    1. Add the server
                  </Text>
                  <Group align="flex-start" wrap="nowrap" gap="xs">
                    <Code block style={{ flex: 1, whiteSpace: "pre-wrap" }}>
                      {snippet}
                    </Code>
                    <CopyButton value={snippet}>
                      {({ copied, copy }) => (
                        <Tooltip label={copied ? "Copied" : "Copy"} withArrow>
                          <ActionIcon
                            variant="subtle"
                            onClick={copy}
                            aria-label="Copy snippet"
                          >
                            {copied ? <IconCheck size={16} /> : <IconClipboard size={16} />}
                          </ActionIcon>
                        </Tooltip>
                      )}
                    </CopyButton>
                  </Group>

                  {deeplink && (
                    <Button
                      component="a"
                      href={deeplink}
                      variant="light"
                      size="xs"
                      leftSection={<IconExternalLink size={14} />}
                      style={{ alignSelf: "flex-start" }}
                    >
                      {client.deeplinkLabel}
                    </Button>
                  )}

                  <Text size="sm" fw={600}>
                    2. Authenticate
                  </Text>
                  <Text size="sm" c="dimmed">
                    {client.authNote}
                  </Text>
                </Stack>
              </Tabs.Panel>
            );
          })}
        </Tabs>

        <Text size="xs" c="dimmed">
          Tools available to the agent: <Code>list_databases</Code> <Code>get_schema</Code>{" "}
          <Code>run_query</Code>. Learn more in the{" "}
          <Anchor
            href="https://modelcontextprotocol.io"
            target="_blank"
            rel="noreferrer"
            size="xs"
          >
            MCP docs
          </Anchor>
          .
        </Text>
      </Stack>
    </Modal>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/frontend && npx vitest run src/components/mcp/__tests__/ConnectMcpModal.test.tsx`
Expected: PASS (3 cases).

- [ ] **Step 5: Lint the new files**

Run: `cd src/frontend && npx eslint src/components/mcp`
Expected: no errors (confirms `Array<T>` usage etc.).

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/components/mcp/ConnectMcpModal.tsx src/frontend/src/components/mcp/__tests__/ConnectMcpModal.test.tsx
git commit -m "Add ConnectMcpModal with per-client tabs and copy snippets"
```

---

### Task 6: `ConnectMcpTrigger` + header wiring

**Files:**
- Create: `src/frontend/src/components/mcp/ConnectMcpTrigger.tsx`
- Modify: `src/frontend/src/routes/_authed.tsx`
- Test: `src/frontend/src/components/mcp/__tests__/ConnectMcpTrigger.test.tsx` (create)

**Interfaces:**
- Consumes: `ConnectMcpModal` (Task 5); `useBranding()` (Task 3).
- Produces: `function ConnectMcpTrigger(): JSX.Element | null` — renders nothing when `mcpEnabled` is false.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/components/mcp/__tests__/ConnectMcpTrigger.test.tsx`:

```tsx
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { ConnectMcpTrigger } from "@/components/mcp/ConnectMcpTrigger";
import { BrandingContext } from "@/theme/BrandingContext";

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(), removeListener: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

function renderTrigger(mcpEnabled: boolean) {
  return render(
    <MantineProvider>
      <BrandingContext
        value={{
          appName: "Acme",
          logoUrl: null,
          faviconUrl: null,
          mcpEnabled,
          mcpServerName: "acme-db",
        }}
      >
        <ConnectMcpTrigger />
      </BrandingContext>
    </MantineProvider>,
  );
}

describe("ConnectMcpTrigger", () => {
  it("renders nothing when MCP is disabled", () => {
    renderTrigger(false);
    expect(screen.queryByLabelText(/connect ai tools/i)).not.toBeInTheDocument();
  });

  it("renders the trigger and opens the modal when MCP is enabled", async () => {
    renderTrigger(true);
    const user = userEvent.setup();

    const button = screen.getByLabelText(/connect ai tools/i);
    expect(button).toBeInTheDocument();

    await user.click(button);
    expect(screen.getByRole("tab", { name: /claude code/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/frontend && npx vitest run src/components/mcp/__tests__/ConnectMcpTrigger.test.tsx`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement `ConnectMcpTrigger.tsx`**

Create `src/frontend/src/components/mcp/ConnectMcpTrigger.tsx`:

```tsx
import { ActionIcon, Tooltip } from "@mantine/core";
import { IconSparkles } from "@tabler/icons-react";
import { useState } from "react";
import { ConnectMcpModal } from "@/components/mcp/ConnectMcpModal";
import { useBranding } from "@/theme/BrandingContext";

export function ConnectMcpTrigger() {
  const { mcpEnabled } = useBranding();
  const [opened, setOpened] = useState(false);

  if (!mcpEnabled) {
    return null;
  }

  return (
    <>
      <Tooltip label="Connect AI tools" withArrow>
        <ActionIcon
          variant="subtle"
          onClick={() => setOpened(true)}
          aria-label="Connect AI tools"
        >
          <IconSparkles size={18} />
        </ActionIcon>
      </Tooltip>
      <ConnectMcpModal opened={opened} onClose={() => setOpened(false)} />
    </>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/frontend && npx vitest run src/components/mcp/__tests__/ConnectMcpTrigger.test.tsx`
Expected: PASS (2 cases).

- [ ] **Step 5: Wire the trigger into the header**

In `src/frontend/src/routes/_authed.tsx`, add the import near the other local imports (after line 33):

```tsx
import { ConnectMcpTrigger } from "@/components/mcp/ConnectMcpTrigger";
```

Then, in the right-hand header `Group gap="xs"` (the one starting at line 96), add the trigger immediately before the color-scheme `ActionIcon`:

```tsx
            <Group gap="xs">
              <ConnectMcpTrigger />
              <ActionIcon
                variant="subtle"
                onClick={() => toggleColorScheme()}
                aria-label="Toggle color scheme"
              >
```

- [ ] **Step 6: Verify build, lint, and full frontend tests**

Run: `cd src/frontend && npx tsc -b && npx eslint src/components/mcp src/routes/_authed.tsx && npx vitest run src/components/mcp src/theme`
Expected: type-check clean, lint clean, all MCP + branding tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/components/mcp/ConnectMcpTrigger.tsx src/frontend/src/components/mcp/__tests__/ConnectMcpTrigger.test.tsx src/frontend/src/routes/_authed.tsx
git commit -m "Add Connect AI tools header trigger gated on mcpEnabled"
```

---

### Task 7: Document `Mcp__ServerName` and the in-app entry point

**Files:**
- Modify: `README.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Add the config row**

In `README.md`, in the MCP config table (the rows near line 94 listing `Mcp__Enabled`, `Mcp__AccessTokenMinutes`, ...), add a row after `Mcp__Enabled`:

```markdown
| `Mcp__ServerName` | | `sluicebase` | Client alias shown in connect snippets (letters, digits, `-`, `_`; falls back to `sluicebase` if invalid) |
```

- [ ] **Step 2: Note the in-app entry point**

In the `## Connecting AI tools (MCP)` section, after the paragraph ending "...tokens are user-scoped and revocable." (around line 154), add:

```markdown
Signed-in users can also open **Connect AI tools** (the ✨ icon in the app header) for copy-ready, per-client setup snippets pre-filled with this instance's URL and server name.
```

- [ ] **Step 3: Verify the edits**

Run: `grep -n "Mcp__ServerName\|Connect AI tools" README.md`
Expected: both additions present.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Document Mcp__ServerName and the in-app Connect AI tools entry"
```

---

## Final verification (after all tasks)

- [ ] `dotnet build SluiceBase.slnx` — 0 warnings.
- [ ] `dotnet test tests/SluiceBase.Api.Tests --filter FullyQualifiedName~McpOptionsTests` — PASS.
- [ ] `cd src/frontend && npm run test` — full suite PASS with coverage.
- [ ] `cd src/frontend && npm run lint` — clean.
- [ ] Confirm no diff in `src/SluiceBase.Api/openapi.json` / `src/frontend/src/api/schema.ts` (no API surface change): `git status --porcelain src/SluiceBase.Api/openapi.json src/frontend/src/api/schema.ts` is empty.
