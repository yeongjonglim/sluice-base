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
