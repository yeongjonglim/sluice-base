import {
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  Skeleton,
  Stack,
  Text,
  Textarea,
  Timeline,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
import { IconBan, IconCheck, IconPlayerPlay, IconSend, IconX } from "@tabler/icons-react";
import { useDisclosure } from "@mantine/hooks";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import React, { useState } from "react";
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
  Pending: "blue",
  Approved: "green",
  Rejected: "red",
  Cancelled: "gray",
  Executed: "teal",
};

function UpdateDetailPage() {
  const { id } = Route.useParams();
  const meData = Route.useRouteContext().queryClient.getQueryData(meQueryOptions.queryKey);
  const navigate = useNavigate();
  const request = useUpdateRequest(id);
  const approve = useApproveUpdate();
  const reject = useRejectUpdate();
  const cancel = useCancelUpdate();
  const execute = useExecuteUpdate();
  const computedColorScheme = useComputedColorScheme();

  const [approveModalOpen, { open: openApprove, close: closeApprove }] = useDisclosure(false);
  const [rejectModalOpen, { open: openReject, close: closeReject }] = useDisclosure(false);
  const [cancelModalOpen, { open: openCancel, close: closeCancel }] = useDisclosure(false);
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
    if (!reviewNote.trim()) return;
    cancel.mutate(
      { id, note: reviewNote },
      {
        onSuccess: () => {
          closeCancel();
          setReviewNote("");
        },
      },
    );
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

      {/* Recreated from */}
      {r.sourceRequestId && (
        <Text size="sm" c="dimmed">
          Recreated from{" "}
          <Text
            component="a"
            href={`/update/${r.sourceRequestId}`}
            size="sm"
            c="blue"
          >
            {`/update/${r.sourceRequestId}`}
          </Text>
        </Text>
      )}

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
          basicSetup={{
            lineNumbers: true,
            foldGutter: false,
            defaultKeymap: false,
          }}
          editable={false}
        />
      </Box>

      {/* Timeline */}
      {(() => {
        const eventItems: Array<{ ts: string; node: React.ReactNode }> = (
          [
            r.status !== "Pending" && r.reviewedAt
              ? {
                  ts: r.reviewedAt,
                  node: (
                    <Timeline.Item
                      key="review"
                      title={r.status === "Rejected" ? "Rejected" : "Approved"}
                      bullet={
                        r.status === "Rejected" ? <IconX size={14} /> : <IconCheck size={14} />
                      }
                      color={r.status === "Rejected" ? "red" : "green"}
                    >
                      <Stack gap={4} mt={4}>
                        <Text size="sm" c="dimmed">
                          {r.status === "Rejected" ? r.reviewerName : r.reviewerName} &middot;{" "}
                          {new Date(r.reviewedAt).toLocaleString()}
                        </Text>
                        {r.reviewNote && <Text size="sm">{r.reviewNote}</Text>}
                      </Stack>
                    </Timeline.Item>
                  ),
                }
              : null,
            r.status === "Cancelled" && r.cancelledAt
              ? {
                  ts: r.cancelledAt,
                  node: (
                    <Timeline.Item
                      key="cancel"
                      title="Cancelled"
                      bullet={<IconBan size={14} />}
                      color="gray"
                    >
                      <Stack gap={4} mt={4}>
                        <Text size="sm" c="dimmed">
                          {r.cancelledByName} &middot; {new Date(r.cancelledAt).toLocaleString()}
                        </Text>
                        {r.cancelNote && <Text size="sm">{r.cancelNote}</Text>}
                      </Stack>
                    </Timeline.Item>
                  ),
                }
              : null,
            r.status === "Executed" && r.executedAt
              ? {
                  ts: r.executedAt,
                  node: (
                    <Timeline.Item
                      key="exec"
                      title="Executed"
                      bullet={<IconPlayerPlay size={14} />}
                      color={r.execSuccess ? "teal" : "red"}
                    >
                      <Stack gap={4} mt={4}>
                        <Group gap="xs">
                          <Text size="sm" c="dimmed">
                            {r.executorName} &middot; {new Date(r.executedAt).toLocaleString()}
                          </Text>
                          {execBadge}
                        </Group>
                        {r.execDurationMs != null && (
                          <Text size="sm" c="dimmed">
                            {r.execDurationMs} ms
                            {r.execAffectedRows != null && ` · ${r.execAffectedRows} rows affected`}
                          </Text>
                        )}
                        {r.execError && (
                          <Alert color="red" title="Error" mt={4}>
                            {r.execError}
                          </Alert>
                        )}
                      </Stack>
                    </Timeline.Item>
                  ),
                }
              : null,
          ] as Array<{ ts: string; node: React.ReactNode } | null>
        )
          .filter((x): x is { ts: string; node: React.ReactNode } => x !== null)
          .sort((a, b) => new Date(a.ts).getTime() - new Date(b.ts).getTime());

        return (
          <Timeline active={eventItems.length} bulletSize={26} mt="xs">
            <Timeline.Item title="Submitted" bullet={<IconSend size={14} />} color="blue">
              <Stack gap={4} mt={4}>
                <Text size="sm" c="dimmed">
                  {r.submitterName} &middot; {new Date(r.submittedAt).toLocaleString()}
                </Text>
                <Text size="sm" c="dimmed">
                  {r.databaseDisplayName}
                </Text>
                <Text size="sm">{r.reason}</Text>
              </Stack>
            </Timeline.Item>
            {eventItems.map((x) => x.node)}
          </Timeline>
        );
      })()}

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
          <Button color="gray" variant="outline" onClick={openCancel} loading={cancel.isPending}>
            Cancel
          </Button>
        )}
        {r.status === "Approved" && canExecute && (
          <Button color="teal" onClick={handleExecute} loading={execute.isPending}>
            Execute
          </Button>
        )}
        {canSubmit && (
          <Button
            variant="light"
            onClick={() => navigate({ to: "/update/new", search: { from: id } })}
          >
            Recreate
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

      {/* Cancel modal */}
      <Modal opened={cancelModalOpen} onClose={closeCancel} title="Cancel request">
        <Stack gap="md">
          <Textarea
            label="Note"
            description="Required — describe why you're cancelling."
            placeholder="Submitted by mistake, resubmitting with corrected SQL."
            required
            minRows={3}
            value={reviewNote}
            onChange={(e) => setReviewNote(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={closeCancel}>
              Back
            </Button>
            <Button
              color="red"
              onClick={handleCancel}
              disabled={!reviewNote.trim()}
              loading={cancel.isPending}
            >
              Confirm Cancel
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
