import { Alert, Badge, Box, Button, Code, Group, Loader, Text } from "@mantine/core";
import { IconEye } from "@tabler/icons-react";
import type { UpdatePreviewResponse } from "@/api/hooks";
import { ApiError } from "@/api/client";
import { isBlocked } from "@/api/useQueryRuns";
import { PreviewResults } from "@/components/update/PreviewResults";

export interface RecordedPreview {
  success: boolean | null;
  at: string;
  affectedRows: number | null;
  error: string | null;
}

interface PreviewPaneProps {
  isPending: boolean;
  isError: boolean;
  error: unknown;
  result: UpdatePreviewResponse | null;
  /** Last preview recorded in the request's event log, shown when this session has none. */
  lastPreview?: RecordedPreview | null;
  onRunPreview?: () => void;
}

// The preview pane is a sandbox: it runs the query in a transaction and throws
// the transaction away. The header states that plainly so the returned rows are
// never mistaken for a committed change.
export function PreviewPane({
  isPending,
  isError,
  error,
  result,
  lastPreview,
  onRunPreview,
}: PreviewPaneProps) {
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
        <PreviewBody
          isPending={isPending}
          isError={isError}
          error={error}
          result={result}
          lastPreview={lastPreview}
          onRunPreview={onRunPreview}
        />
      </Box>
    </Box>
  );
}

function PreviewBody({ isPending, isError, error, result, lastPreview, onRunPreview }: PreviewPaneProps) {
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

  // This session has not previewed, but the request has a recorded outcome. Show
  // it instead of inviting a run that already happened.
  if (lastPreview) {
    return (
      <Alert
        color={lastPreview.success ? "teal" : "red"}
        variant="light"
        title={lastPreview.success ? "Last dry run passed" : "Last dry run failed"}
        m="sm"
      >
        <Text size="sm">
          {lastPreview.success
            ? `${lastPreview.affectedRows ?? 0} rows would change`
            : (lastPreview.error ?? "The preview did not complete.")}
        </Text>
        <Text size="xs" c="dimmed" mt={4}>
          {new Date(lastPreview.at).toLocaleString()}
        </Text>
        {onRunPreview && (
          <Button
            mt="sm"
            size="compact-xs"
            variant="light"
            color="grape"
            leftSection={<IconEye size={12} />}
            onClick={onRunPreview}
          >
            Run preview again
          </Button>
        )}
      </Alert>
    );
  }

  // Empty state — explain the dry-run semantics before the user even clicks.
  return (
    <Text size="sm" c="dimmed" p="md" maw={520}>
      Run <b>Preview</b> to test this query. It runs inside a transaction and is then rolled
      back — a safe dry run that returns the result without changing any data.
    </Text>
  );
}
