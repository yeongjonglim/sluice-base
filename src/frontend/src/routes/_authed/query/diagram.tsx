import { useMemo, useState } from "react";
import { Alert, Box, Button, Center, Group, Loader, Stack, Text } from "@mantine/core";
import { IconDownload } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { meQueryOptions, useCatalogServer, useExportSchemaDdl, useSchema } from "@/api/hooks";
import { DatabaseSelect } from "@/components/DatabaseSelect";
import { ErdCanvas } from "@/components/erd/ErdCanvas";
import { SchemaSelect } from "@/components/SchemaSelect";
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

  // null = "all schemas" (the initial default). A concrete array once the user narrows it;
  // an empty array means the user explicitly cleared the picker (show nothing).
  const [selectedSchemas, setSelectedSchemas] = useState<Array<string> | null>(null);

  // Reset the schema filter to "all" whenever the selected database changes.
  // Done during render (not in an effect) per react.dev "You Might Not Need an Effect".
  const [prevDatabaseId, setPrevDatabaseId] = useState(selectedDatabaseId);
  if (selectedDatabaseId !== prevDatabaseId) {
    setPrevDatabaseId(selectedDatabaseId);
    setSelectedSchemas(null);
  }

  const allSchemaNames = (schema.data?.schemas ?? []).map((s) => s.name);
  const effectiveSelected = selectedSchemas ?? allSchemaNames;

  // Stable Set identity (keyed on the selection state) so ErdCanvas's layout effect
  // only re-runs when the filter actually changes, preserving dragged node positions.
  const visibleSchemas = useMemo(
    () => (selectedSchemas === null ? undefined : new Set(selectedSchemas)),
    [selectedSchemas],
  );

  // An explicitly emptied picker (not the initial "all" default) shows nothing.
  const noSchemaSelected = selectedSchemas !== null && selectedSchemas.length === 0;

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
          <Group gap="xs" wrap="nowrap">
            <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
            {allSchemaNames.length > 1 && (
              <SchemaSelect
                schemas={allSchemaNames}
                value={effectiveSelected}
                onChange={setSelectedSchemas}
              />
            )}
          </Group>
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
        {selectedDatabaseId && schema.data && noSchemaSelected && (
          <Center h="100%">
            <Text c="dimmed">Select one or more schemas to view the diagram</Text>
          </Center>
        )}
        {selectedDatabaseId && schema.data && !noSchemaSelected && (
          <ErdCanvas tree={schema.data} visibleSchemas={visibleSchemas} />
        )}
      </Box>
    </Stack>
  );
}
