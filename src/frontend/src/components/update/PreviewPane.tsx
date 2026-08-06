import { Alert, Badge, Box, Code, Group, Loader, Text } from "@mantine/core";
import { IconEye } from "@tabler/icons-react";
import type { UpdatePreviewResponse } from "@/api/hooks";
import { ApiError } from "@/api/client";
import { isBlocked } from "@/api/useQueryRuns";
import { PreviewResults } from "@/components/update/PreviewResults";

interface PreviewPaneProps {
  isPending: boolean;
  isError: boolean;
  error: unknown;
  result: UpdatePreviewResponse | null;
}

// The preview pane is a sandbox: it runs the query in a transaction and throws
// the transaction away. The header states that plainly so the returned rows are
// never mistaken for a committed change.
export function PreviewPane({ isPending, isError, error, result }: PreviewPaneProps) {
  return (
    <Box style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0 }}>
      <Group
        gap="xs"
        px="sm"
        py={6}
        style={{ flexShrink: 0, borderBottom: "1px solid var(--mantine-color-default-border)" }}
      >
        <IconEye size={15} />
        <Text tt="uppercase" fw={700} fz="xs" c="dimmed" style={{ letterSpacing: "0.05em" }}>
          Preview
        </Text>
        <Badge size="xs" color="grape" variant="light">
          dry run · rolled back
        </Badge>
      </Group>

      <Box style={{ flex: 1, minHeight: 0, overflow: "hidden" }}>
        <PreviewBody isPending={isPending} isError={isError} error={error} result={result} />
      </Box>
    </Box>
  );
}

function PreviewBody({ isPending, isError, error, result }: PreviewPaneProps) {
  if (isPending) {
    return (
      <Group gap="xs" p="md" c="dimmed">
        <Loader size="sm" />
        <Text size="sm">Running in a transaction, then rolling back…</Text>
      </Group>
    );
  }

  if (isError && isBlocked(error)) {
    const body = (error instanceof ApiError ? error.body : null) as
      | { columns?: Array<{ schema: string; table: string; column: string }> }
      | null;
    return (
      <Box style={{ height: "100%", overflow: "auto" }}>
        <Alert color="orange" title="Preview blocked — restricted columns" m="sm">
          <Text size="sm" mb="xs">
            This SQL references columns you are not authorised to access:
          </Text>
          {(body?.columns ?? []).map((c, i) => (
            <Code key={i} display="block" fz="xs">
              {c.schema}.{c.table}.{c.column}
            </Code>
          ))}
        </Alert>
      </Box>
    );
  }

  if (isError) {
    return (
      <Alert color="red" title="Preview failed" m="sm">
        Could not run the preview. Check your connection and try again.
      </Alert>
    );
  }

  if (result) {
    return <PreviewResults result={result} />;
  }

  // Empty state — explain the dry-run semantics before the user even clicks.
  return (
    <Text size="sm" c="dimmed" p="md" maw={520}>
      Run <b>Preview</b> to test this query. It runs inside a transaction and is then rolled
      back — a safe dry run that returns the result without changing any data.
    </Text>
  );
}
