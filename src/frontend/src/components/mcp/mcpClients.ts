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
  buildSnippet: (ctx: McpConnectionContext) => string;
  buildDeeplink?: (ctx: McpConnectionContext) => string;
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
