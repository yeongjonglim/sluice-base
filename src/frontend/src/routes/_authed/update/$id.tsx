import {
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  Paper,
  Skeleton,
  Stack,
  Text,
  Textarea,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import {
  meQueryOptions,
  useApproveUpdate,
  useCancelUpdate,
  useExecuteUpdate,
  useRejectUpdate,
  useUpdateRequest,
} from "@/api/hooks";

export const Route = createFileRoute("/_authed/update/$id")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    const hasAny =
      me?.permissions.includes("update:submit") ||
      me?.permissions.includes("update:approve") ||
      me?.permissions.includes("update:execute");
    if (!hasAny) {
      throw redirect({ to: "/" });
    }
  },
  component: UpdateDetailPage,
});

const STATUS_COLOR: Record<string, string> = {
  pending: "blue",
  approved: "green",
  rejected: "red",
  cancelled: "gray",
  executed: "teal",
};

function UpdateDetailPage() {
  const { id } = Route.useParams();
  const meData = Route.useRouteContext().queryClient.getQueryData(meQueryOptions.queryKey);
  const request = useUpdateRequest(id);
  const approve = useApproveUpdate();
  const reject = useRejectUpdate();
  const cancel = useCancelUpdate();
  const execute = useExecuteUpdate();
  const computedColorScheme = useComputedColorScheme();

  const [approveModalOpen, { open: openApprove, close: closeApprove }] = useDisclosure(false);
  const [rejectModalOpen, { open: openReject, close: closeReject }] = useDisclosure(false);
  const [reviewNote, setReviewNote] = useState("");

  const canApprove = meData?.permissions.includes("update:approve") ?? false;
  const canSubmit = meData?.permissions.includes("update:submit") ?? false;
  const canExecute = meData?.permissions.includes("update:execute") ?? false;

  if (request.isPending) {
    return (
      <Stack gap="md">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={60} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (request.isError) {
    return (
      <Alert color="red" title="Not found">
        Request not found or could not be loaded.
      </Alert>
    );
  }

  const r = request.data;

  function handleApprove() {
    if (!reviewNote.trim()) return;
    approve.mutate(
      { id, note: reviewNote },
      {
        onSuccess: () => {
          closeApprove();
          setReviewNote("");
        },
      },
    );
  }

  function handleReject() {
    if (!reviewNote.trim()) return;
    reject.mutate(
      { id, note: reviewNote },
      {
        onSuccess: () => {
          closeReject();
          setReviewNote("");
        },
      },
    );
  }

  function handleCancel() {
    modals.openConfirmModal({
      title: "Cancel request",
      children: <Text>Are you sure you want to cancel this update request?</Text>,
      labels: { confirm: "Cancel request", cancel: "Keep it" },
      confirmProps: { color: "red" },
      onConfirm: () => cancel.mutate(id),
    });
  }

  function handleExecute() {
    modals.openConfirmModal({
      title: "Execute update",
      children: (
        <Text>
          This will run the SQL against the write connection. This cannot be undone. Proceed?
        </Text>
      ),
      labels: { confirm: "Execute", cancel: "Go back" },
      confirmProps: { color: "green" },
      onConfirm: () => execute.mutate(id),
    });
  }

  const execBadge =
    r.status === "Executed" ? (
      r.execSuccess ? (
        <Badge color="teal">Succeeded</Badge>
      ) : (
        <Badge color="red">Failed</Badge>
      )
    ) : null;

  return (
    <Stack gap="md">
      <Group gap="sm">
        <Title order={2}>Update Request</Title>
        <Badge color={STATUS_COLOR[r.status] ?? "gray"} size="lg">
          {r.status}
        </Badge>
      </Group>

      {/* SQL */}
      <Box
        style={{
          border: "1px solid var(--mantine-color-default-border)",
          borderRadius: "var(--mantine-radius-sm)",
          overflow: "hidden",
        }}
      >
        <CodeMirror
          value={r.sqlText}
          extensions={[sql()]}
          theme={computedColorScheme === "dark" ? githubDark : githubLight}
          height="200px"
          basicSetup={{ lineNumbers: true, foldGutter: false }}
          editable={false}
        />
      </Box>

      {/* Metadata */}
      <Paper withBorder p="md">
        <Stack gap="xs">
          <Group gap="xs">
            <Text size="sm" fw={500}>
              Server:
            </Text>
            <Text size="sm">{r.serverName ?? "—"}</Text>
          </Group>
          <Group gap="xs">
            <Text size="sm" fw={500}>
              Submitted by:
            </Text>
            <Text size="sm">{r.submitterName ?? "—"}</Text>
          </Group>
          <Group gap="xs">
            <Text size="sm" fw={500}>
              Submitted at:
            </Text>
            <Text size="sm">{new Date(r.submittedAt).toLocaleString()}</Text>
          </Group>
          <Group gap="xs" align="flex-start">
            <Text size="sm" fw={500}>
              Reason:
            </Text>
            <Text size="sm" style={{ flex: 1 }}>
              {r.reason}
            </Text>
          </Group>
        </Stack>
      </Paper>

      {/* Review section */}
      {r.status !== "Pending" && r.reviewedAt && (
        <Paper withBorder p="md">
          <Stack gap="xs">
            <Text size="sm" fw={600}>
              Review
            </Text>
            <Group gap="xs">
              <Text size="sm" fw={500}>
                {r.status === "Rejected" ? "Rejected by:" : "Approved by:"}
              </Text>
              <Text size="sm">{r.reviewerName ?? "—"}</Text>
            </Group>
            <Group gap="xs">
              <Text size="sm" fw={500}>
                At:
              </Text>
              <Text size="sm">{new Date(r.reviewedAt).toLocaleString()}</Text>
            </Group>
            {r.reviewNote && (
              <Group gap="xs" align="flex-start">
                <Text size="sm" fw={500}>
                  Note:
                </Text>
                <Text size="sm" style={{ flex: 1 }}>
                  {r.reviewNote}
                </Text>
              </Group>
            )}
          </Stack>
        </Paper>
      )}

      {/* Execution section */}
      {r.status === "Executed" && r.executedAt && (
        <Paper withBorder p="md">
          <Stack gap="xs">
            <Group gap="xs">
              <Text size="sm" fw={600}>
                Execution
              </Text>
              {execBadge}
            </Group>
            <Group gap="xs">
              <Text size="sm" fw={500}>
                Executed by:
              </Text>
              <Text size="sm">{r.executorName ?? "—"}</Text>
            </Group>
            <Group gap="xs">
              <Text size="sm" fw={500}>
                At:
              </Text>
              <Text size="sm">{new Date(r.executedAt).toLocaleString()}</Text>
            </Group>
            {r.execDurationMs != null && (
              <Group gap="xs">
                <Text size="sm" fw={500}>
                  Duration:
                </Text>
                <Text size="sm">{r.execDurationMs} ms</Text>
              </Group>
            )}
            {r.execAffectedRows != null && (
              <Group gap="xs">
                <Text size="sm" fw={500}>
                  Affected rows:
                </Text>
                <Text size="sm">{r.execAffectedRows}</Text>
              </Group>
            )}
            {r.execError && (
              <Alert color="red" title="Error">
                {r.execError}
              </Alert>
            )}
          </Stack>
        </Paper>
      )}

      {/* Action area */}
      <Group>
        {r.status === "Pending" && canApprove && (
          <>
            <Button color="green" onClick={openApprove} loading={approve.isPending}>
              Approve
            </Button>
            <Button color="red" variant="outline" onClick={openReject} loading={reject.isPending}>
              Reject
            </Button>
          </>
        )}
        {(r.status === "Pending" || r.status === "Approved") && canSubmit && (
          <Button color="gray" variant="outline" onClick={handleCancel} loading={cancel.isPending}>
            Cancel
          </Button>
        )}
        {r.status === "Approved" && canExecute && (
          <Button color="teal" onClick={handleExecute} loading={execute.isPending}>
            Execute
          </Button>
        )}
      </Group>

      {/* Approve modal */}
      <Modal opened={approveModalOpen} onClose={closeApprove} title="Approve request">
        <Stack gap="md">
          <Textarea
            label="Note"
            description="Required — describe why you're approving."
            placeholder="Looks correct, verified against staging."
            required
            minRows={3}
            value={reviewNote}
            onChange={(e) => setReviewNote(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={closeApprove}>
              Back
            </Button>
            <Button
              color="green"
              onClick={handleApprove}
              disabled={!reviewNote.trim()}
              loading={approve.isPending}
            >
              Confirm Approve
            </Button>
          </Group>
        </Stack>
      </Modal>

      {/* Reject modal */}
      <Modal opened={rejectModalOpen} onClose={closeReject} title="Reject request">
        <Stack gap="md">
          <Textarea
            label="Note"
            description="Required — describe why you're rejecting."
            placeholder="SQL targets wrong table, needs revision."
            required
            minRows={3}
            value={reviewNote}
            onChange={(e) => setReviewNote(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={closeReject}>
              Back
            </Button>
            <Button
              color="red"
              onClick={handleReject}
              disabled={!reviewNote.trim()}
              loading={reject.isPending}
            >
              Confirm Reject
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
