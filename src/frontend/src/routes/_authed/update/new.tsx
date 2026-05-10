import {
  Box,
  Button,
  Group,
  Select,
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
import { meQueryOptions, useServers, useSubmitUpdate } from "@/api/hooks";

export const Route = createFileRoute("/_authed/update/new")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("update:submit")) {
      throw redirect({ to: "/" });
    }
  },
  component: NewUpdatePage,
});

function NewUpdatePage() {
  const navigate = useNavigate();
  const servers = useServers();
  const submit = useSubmitUpdate();
  const computedColorScheme = useComputedColorScheme();

  const [serverId, setServerId] = useState<string | null>(null);
  const [sqlText, setSqlText] = useState("");
  const [reason, setReason] = useState("");

  const serverOptions = (servers.data?.servers ?? [])
    .filter((s) => s.hasWriteCredential && s.isEnabled)
    .map((s) => ({ value: s.id, label: s.name }));

  const canSubmit = serverId !== null && sqlText.trim() !== "" && reason.trim() !== "";

  function handleSubmit() {
    if (!canSubmit) return;
    submit.mutate(
      { serverId, sqlText, reason },
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
        label="Server"
        placeholder="Select a server with write credentials"
        data={serverOptions}
        value={serverId}
        onChange={setServerId}
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
            height="300px"
            basicSetup={{ lineNumbers: true, foldGutter: false }}
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
