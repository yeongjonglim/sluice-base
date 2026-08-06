import { Alert, Code, Collapse, Stack, Text, UnstyledButton } from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconChevronRight } from "@tabler/icons-react";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { ApiError } from "@/api/client";
import { PlanSummaryBadges } from "@/components/query/PlanSummaryBadges";

function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

export function PlanView({ entry }: { entry: ExplainEntry }) {
  const [open, { toggle }] = useDisclosure(false);

  if (entry.status === "pending") {
    return <Text p="xs" size="sm" c="dimmed">Analyzing…</Text>;
  }

  if (entry.status === "blocked") {
    const body = entry.error instanceof ApiError
      ? (entry.error.body as { columns?: Array<{ schema: string; table: string; column: string }> } | null)
      : null;
    return (
      <Alert color="orange" title="Blocked — restricted columns" m="xs">
        {(body?.columns ?? []).map((c, i) => (
          <Code key={i} display="block" fz="xs">{c.schema}.{c.table}.{c.column}</Code>
        ))}
      </Alert>
    );
  }

  if (entry.status === "error" || !entry.plan) {
    const message = entry.error instanceof ApiError
      ? String(entry.error.body ?? entry.error.message)
      : "Could not analyze this statement.";
    return (
      <Alert color="red" title="Explain failed" m="xs">
        {message}
      </Alert>
    );
  }

  return (
    <Stack p="xs" gap="xs">
      <PlanSummaryBadges summary={entry.plan.summary} />
      <UnstyledButton onClick={toggle}>
        <Text size="xs" c="dimmed" style={{ display: "flex", alignItems: "center", gap: 4 }}>
          <IconChevronRight
            size={12}
            style={{ transform: open ? "rotate(90deg)" : "none", transition: "transform 120ms" }}
          />
          Raw plan
        </Text>
      </UnstyledButton>
      <Collapse expanded={open} keepMounted={false}>
        <Code block fz="xs" style={{ maxHeight: 320, overflow: "auto" }}>
          {prettyJson(entry.plan.planJson)}
        </Code>
      </Collapse>
    </Stack>
  );
}
