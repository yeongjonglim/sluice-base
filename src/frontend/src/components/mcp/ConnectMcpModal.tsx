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
                    <Code
                      style={{
                        flex: 1,
                        display: "block",
                        whiteSpace: "pre-wrap",
                        padding: "var(--mantine-spacing-xs) var(--mantine-spacing-sm)",
                      }}
                    >
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
