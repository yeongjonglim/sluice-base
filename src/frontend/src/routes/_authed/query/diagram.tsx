import { Alert, Box, Button, Center, Group, Loader, Stack, Text } from "@mantine/core";
import { IconDownload } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { meQueryOptions, useCatalogServer, useExportSchemaDdl, useSchema } from "@/api/hooks";
import { DatabaseSelect } from "@/components/DatabaseSelect";
import { ErdCanvas } from "@/components/erd/ErdCanvas";
import { useSessionState } from "@/utils/useSessionState";

export const Route = createFileRoute("/_authed/query/diagram")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: DiagramPage,
});

function DiagramPage() {
  const [selectedDatabaseId, setSelectedDatabaseId] = useSessionState<string | null>(
    "sluice:query:db",
    null,
  );
  const schema = useSchema(selectedDatabaseId);
  const catalog = useCatalogServer();
  const exportDdl = useExportSchemaDdl();

  function handleExport() {
    if (!selectedDatabaseId) return;
    const match = (catalog.data?.servers ?? [])
      .flatMap((s) => s.databases.map((d) => ({ id: d.id, label: `${s.name}-${d.displayName}` })))
      .find((d) => d.id === selectedDatabaseId);
    const base = (match?.label ?? "schema").replace(/[^a-zA-Z0-9._-]/g, "-");
    const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
    exportDdl.mutate({ databaseId: selectedDatabaseId, filename: `${base}-schema-${timestamp}.sql` });
  }

  return (
    <Stack
      gap={0}
      style={{
        margin: "calc(-1 * var(--mantine-spacing-sm))",
        height: "calc(100vh - 44px)",
      }}
    >
      <Box p="xs" style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}>
        <Group justify="space-between" wrap="nowrap">
          <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
          <Button
            leftSection={<IconDownload size={14} />}
            size="sm"
            variant="default"
            disabled={!selectedDatabaseId}
            loading={exportDdl.isPending}
            onClick={handleExport}
          >
            Export DDL
          </Button>
        </Group>
      </Box>
      <Box style={{ flex: 1, minHeight: 0 }}>
        {!selectedDatabaseId && (
          <Center h="100%">
            <Text c="dimmed">Select a database to view its diagram</Text>
          </Center>
        )}
        {selectedDatabaseId && schema.isLoading && (
          <Center h="100%">
            <Loader />
          </Center>
        )}
        {selectedDatabaseId && schema.isError && (
          <Alert color="red" m="md" title="Failed to load schema">
            {schema.error instanceof Error ? schema.error.message : "Unknown error"}
          </Alert>
        )}
        {selectedDatabaseId && schema.data && <ErdCanvas tree={schema.data} />}
      </Box>
    </Stack>
  );
}
