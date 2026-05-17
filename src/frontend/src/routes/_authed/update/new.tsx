import {
  Box,
  Button,
  Group,
  Select,
  Skeleton,
  Stack,
  Text,
  Textarea,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { meQueryOptions, useCatalogServer, useSubmitUpdate, useUpdateRequest } from "@/api/hooks";

export const Route = createFileRoute("/_authed/update/new")({
  validateSearch: (search: Record<string, unknown>) => ({
    from: typeof search.from === "string" ? search.from : undefined,
  }),
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("update:submit")) {
      throw redirect({ to: "/" });
    }
  },
  component: NewUpdatePage,
});

export function NewUpdatePage() {
  const { from } = Route.useSearch();
  const source = useUpdateRequest(from ?? "");

  if (from && source.isPending) {
    return (
      <Stack gap="md">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={60} radius="sm" />
        ))}
      </Stack>
    );
  }

  return (
    <NewUpdateForm
      initialDatabaseId={source.data?.databaseId ?? null}
      initialSqlText={source.data?.sqlText ?? ""}
      sourceRequestId={from}
    />
  );
}

interface NewUpdateFormProps {
  initialDatabaseId: string | null;
  initialSqlText: string;
  sourceRequestId?: string;
}

export function NewUpdateForm({ initialDatabaseId, initialSqlText, sourceRequestId }: NewUpdateFormProps) {
  const navigate = useNavigate();
  const servers = useCatalogServer();
  const submit = useSubmitUpdate();
  const computedColorScheme = useComputedColorScheme();

  const [databaseId, setDatabaseId] = useState<string | null>(initialDatabaseId);
  const [sqlText, setSqlText] = useState(initialSqlText);
  const [reason, setReason] = useState("");

  const databaseOptions = (servers.data?.servers ?? []).flatMap((s) =>
    s.databases
      .filter((d) => d.canWrite)
      .map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
  );

  const canSubmit = databaseId !== null && sqlText.trim() !== "" && reason.trim() !== "";

  function handleSubmit() {
    if (!canSubmit) return;
    submit.mutate(
      { databaseId, sqlText, reason, sourceRequestId },
      {
        onSuccess: (data) => {
          void navigate({ to: "/update/$id", params: { id: data.id } });
        },
      },
    );
  }

  return (
    <Stack gap="md">
      <Title order={2}>New Update Request</Title>

      <Select
        label="Database"
        placeholder="Select a writable database"
        data={databaseOptions}
        value={databaseId}
        onChange={setDatabaseId}
        required
      />

      <Box>
        <Text size="sm" fw={500} mb={4}>
          SQL{" "}
          <Text span c="red">
            *
          </Text>
        </Text>
        <Box
          style={{
            border: "1px solid var(--mantine-color-default-border)",
            borderRadius: "var(--mantine-radius-sm)",
            overflow: "hidden",
          }}
        >
          <CodeMirror
            value={sqlText}
            onChange={setSqlText}
            extensions={[sql()]}
            theme={computedColorScheme === "dark" ? githubDark : githubLight}
            minHeight="300px"
            basicSetup={{
              lineNumbers: true,
              foldGutter: false,
              defaultKeymap: false,
            }}
          />
        </Box>
      </Box>

      <Textarea
        label="Reason"
        description="Describe why this change is needed. A ticket link is fine."
        placeholder="e.g. https://example.com/ticket/... — fixing bad email for user X"
        required
        minRows={3}
        value={reason}
        onChange={(e) => setReason(e.currentTarget.value)}
      />

      <Group>
        <Button onClick={handleSubmit} loading={submit.isPending} disabled={!canSubmit}>
          Submit for Approval
        </Button>
        <Button variant="subtle" component="a" href="/update">
          Cancel
        </Button>
      </Group>
    </Stack>
  );
}
