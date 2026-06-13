import { Alert, Box, Center, Loader, Stack, Text } from "@mantine/core";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { meQueryOptions, useSchema } from "@/api/hooks";
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

  return (
    <Stack
      gap={0}
      style={{
        margin: "calc(-1 * var(--mantine-spacing-sm))",
        height: "calc(100vh - 44px)",
      }}
    >
      <Box p="xs" style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}>
        <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
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
