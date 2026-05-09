import {
  Alert,
  Box,
  Center,
  Code,
  Flex,
  Group,
  NavLink,
  Select,
  Skeleton,
  Stack,
  Text,
} from "@mantine/core";
import { IconChevronDown, IconChevronRight, IconDatabase, IconTable } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import { meQueryOptions, useSchema, useServers } from "@/api/hooks";

export const Route = createFileRoute("/_authed/query")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: QueryPage,
});

function QueryPage() {
  const servers = useServers();
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);
  const schema = useSchema(selectedServerId);

  const serverOptions = (servers.data?.servers ?? []).map((s) => ({
    value: s.id,
    label: s.name,
  }));

  return (
    <Flex h="calc(100vh - 90px)" style={{ overflow: "hidden" }}>
      <Box
        w={280}
        style={{
          borderRight: "1px solid var(--mantine-color-default-border)",
          overflow: "auto",
          flexShrink: 0,
        }}
      >
        <Stack gap={0} p="xs">
          <Select
            placeholder="Select a server"
            data={serverOptions}
            value={selectedServerId}
            onChange={setSelectedServerId}
            mb="xs"
            size="sm"
          />
          <SchemaSidebar schema={schema} />
        </Stack>
      </Box>
      <Box flex={1} style={{ overflow: "auto" }}>
        <Center h="100%">
          <Text c="dimmed">Query editor coming soon</Text>
        </Center>
      </Box>
    </Flex>
  );
}

function SchemaSidebar({ schema }: { schema: ReturnType<typeof useSchema> }) {
  const [expandedSchemas, setExpandedSchemas] = useState<Set<string>>(new Set());
  const [expandedTables, setExpandedTables] = useState<Set<string>>(new Set());

  if (schema.isFetching) {
    return (
      <Stack gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (schema.isError) {
    return (
      <Alert color="red" title="Schema load failed" mt="xs">
        Could not connect to the server.
      </Alert>
    );
  }

  if (!schema.data) {
    return (
      <Text size="sm" c="dimmed" p="xs">
        Select a server to browse its schema.
      </Text>
    );
  }

  function toggleSchema(name: string) {
    setExpandedSchemas((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }

  function toggleTable(key: string) {
    setExpandedTables((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  return (
    <Stack gap={0}>
      {schema.data.schemas.map((s) => {
        const schemaExpanded = expandedSchemas.has(s.name);
        return (
          <div key={s.name}>
            <NavLink
              label={s.name}
              leftSection={<IconDatabase size={14} />}
              rightSection={
                schemaExpanded ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />
              }
              onClick={() => toggleSchema(s.name)}
              active={false}
            />
            {schemaExpanded &&
              s.tables.map((t) => {
                const tableKey = `${s.name}.${t.name}`;
                const tableExpanded = expandedTables.has(tableKey);
                return (
                  <div key={tableKey}>
                    <NavLink
                      label={t.name}
                      leftSection={<IconTable size={14} />}
                      rightSection={
                        tableExpanded ? (
                          <IconChevronDown size={12} />
                        ) : (
                          <IconChevronRight size={12} />
                        )
                      }
                      onClick={() => toggleTable(tableKey)}
                      pl="lg"
                      active={false}
                    />
                    {tableExpanded && (
                      <Stack
                        gap={0}
                        pl="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-xs))"
                      >
                        {t.columns.map((c) => (
                          <Group key={c.name} gap="xs" px="xs" py={2} wrap="nowrap">
                            <Text size="xs" style={{ minWidth: 0 }}>
                              {c.name}
                            </Text>
                            <Code fz="xs">{c.dataType}</Code>
                            {c.isNullable && (
                              <Text size="xs" c="dimmed">
                                null
                              </Text>
                            )}
                          </Group>
                        ))}
                      </Stack>
                    )}
                  </div>
                );
              })}
          </div>
        );
      })}
    </Stack>
  );
}
