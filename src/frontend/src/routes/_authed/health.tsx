import { Alert, Code, Group, Loader, Stack, Text, Title } from "@mantine/core";
import { IconAlertCircle } from "@tabler/icons-react";
import { createFileRoute } from "@tanstack/react-router";
import { useAuthedHealth } from "@/api/hooks.ts";

export const Route = createFileRoute("/_authed/health")({
  component: HealthPage,
});

function HealthPage() {
  const health = useAuthedHealth();

  return (
    <Stack gap="md">
      <Title order={2}>Authenticated health</Title>
      <Text c="dimmed">
        Calls <Code>GET /api/health/authed</Code> via the BFF. A 200 with your username confirms
        cookie auth is wired end-to-end.
      </Text>

      {health.isPending && (
        <Group>
          <Loader size="sm" />
          <Text>Checking…</Text>
        </Group>
      )}

      {health.isError && (
        <Alert icon={<IconAlertCircle />} color="red" title="Error">
          {health.error.message}
        </Alert>
      )}

      {health.data && (
        <Alert color="teal" title="OK">
          status=<Code>{health.data.status}</Code> · user=
          <Code>{health.data.user ?? "(none)"}</Code>
        </Alert>
      )}
    </Stack>
  );
}
