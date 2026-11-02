import {
  Alert,
  Badge,
  Box,
  Button,
  Checkbox,
  Group,
  Modal,
  Skeleton,
  Splitter,
  Stack,
  Text,
  Textarea,
  Timeline,
  Title,
} from "@mantine/core";
import {
  IconAlertTriangle,
  IconBan,
  IconCheck,
  IconEye,
  IconPlayerPlay,
  IconSend,
  IconX,
} from "@tabler/icons-react";
import { useMediaQuery } from "@mantine/hooks";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import React, { useState } from "react";
import type { UpdatePreviewResponse } from "@/api/hooks";
import { SqlEditor } from "@/components/SqlEditor";
import { PreviewPane } from "@/components/update/PreviewPane";
import {
  meQueryOptions,
  useApproveUpdate,
  useCancelUpdate,
  useExecuteUpdate,
  usePreviewUpdate,
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

const BORDER = "1px solid var(--mantine-color-default-border)";

// One dialog for every review decision: the note state belongs to the action the
// reviewer actually chose, so a rejection note can never reappear in an approval.
type ReviewAction = "approve" | "reject" | "cancel";

const REVIEW_COPY: Record<ReviewAction, { title: string; description: string; placeholder: string; confirm: string; color: string }> = {
  approve: {
    title: "Approve request",
    description: "Required — describe what you verified before approving.",
    placeholder: "Looks correct, verified against staging.",
    confirm: "Confirm approve",
    color: "green",
  },
  reject: {
    title: "Reject request",
    description: "Required — describe what needs to change.",
    placeholder: "Targets the wrong table, needs revision.",
    confirm: "Confirm reject",
    color: "red",
  },
  cancel: {
    title: "Cancel request",
    description: "Required — describe why this request is being withdrawn.",
    placeholder: "Submitted by mistake, resubmitting with corrected SQL.",
    confirm: "Confirm cancel",
    color: "red",
  },
};

function relativeDays(iso: string): string {
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
  if (days <= 0) return "today";
  if (days === 1) return "1 day ago";
  return `${days} days ago`;
}

function UpdateDetailPage() {
  const { id } = Route.useParams();
  const meData = Route.useRouteContext().queryClient.getQueryData(meQueryOptions.queryKey);
  const navigate = useNavigate();
  const request = useUpdateRequest(id);
  const approve = useApproveUpdate();
  const reject = useRejectUpdate();
  const cancel = useCancelUpdate();
  const execute = useExecuteUpdate();
  const preview = usePreviewUpdate();
  // Below the navbar breakpoint the two-axis splitter leaves the timeline too
  // narrow to read, so the page becomes one scrolling column of full-width blocks.
  const isMobile = useMediaQuery("(max-width: 47.99em)", false, { getInitialValueInEffect: false });

  const [reviewAction, setReviewAction] = useState<ReviewAction | null>(null);
  const [reviewNote, setReviewNote] = useState("");
  const [overridePreview, setOverridePreview] = useState(false);
  const [previewResult, setPreviewResult] = useState<UpdatePreviewResponse | null>(null);

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
  const canPreview = r.submitterId === meData?.id || canApprove || canExecute;

  // The append-only event log is the only record of what the dry run actually did,
  // so approval reads from it rather than from local component state.
  const lastPreview = r.events.length > 0 ? r.events[r.events.length - 1] : null;
  const lastPreviewFailed = lastPreview?.success === false;
  const lastPreviewError = lastPreview?.error ?? null;

  function runPreview() {
    preview.mutate(id, { onSuccess: (data) => setPreviewResult(data) });
  }

  function openReview(action: ReviewAction) {
    setReviewNote("");
    setOverridePreview(false);
    setReviewAction(action);
  }

  function closeReview() {
    setReviewAction(null);
  }

  function handleApprove() {
    if (!reviewNote.trim()) return;
    if (lastPreviewFailed && !overridePreview) return;
    approve.mutate({ id, note: reviewNote }, { onSuccess: closeReview });
  }

  function handleReject() {
    if (!reviewNote.trim()) return;
    reject.mutate({ id, note: reviewNote }, { onSuccess: closeReview });
  }

  function handleCancel() {
    if (!reviewNote.trim()) return;
    cancel.mutate({ id, note: reviewNote }, { onSuccess: closeReview });
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

  // ── Timeline ───────────────────────────────────────────────────────────────
  // Single-shot lifecycle events come from the request's columns; repeatable
  // Previewed events come from the append-only event log. Both are merged and
  // sorted chronologically.
  const eventItems: Array<{ ts: string; node: React.ReactNode }> = (
    [
      r.status !== "Pending" && r.reviewedAt
        ? {
            ts: r.reviewedAt,
            node: (
              <Timeline.Item
                key="review"
                title={r.status === "Rejected" ? "Rejected" : "Approved"}
                bullet={r.status === "Rejected" ? <IconX size={14} /> : <IconCheck size={14} />}
                color={r.status === "Rejected" ? "red" : "green"}
              >
                <Stack gap={4} mt={4}>
                  <Text size="sm" c="dimmed">
                    {r.reviewerName} &middot; {new Date(r.reviewedAt).toLocaleString()}
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
              <Timeline.Item key="cancel" title="Cancelled" bullet={<IconBan size={14} />} color="gray">
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
      ...r.events.map((e) => ({
        ts: e.at,
        node: (
          <Timeline.Item
            key={`preview-${e.at}`}
            title="Previewed"
            bullet={<IconEye size={14} />}
            color={e.success ? "grape" : "red"}
          >
            <Stack gap={4} mt={4}>
              <Text size="sm" c="dimmed">
                {e.actorName} &middot; {new Date(e.at).toLocaleString()}
              </Text>
              <Text size="sm" c="dimmed">
                {e.success
                  ? `${e.affectedRows ?? 0} rows would change · ${e.resultSetCount ?? 0} result set(s) · rolled back`
                  : (e.error ?? "Preview failed")}
              </Text>
            </Stack>
          </Timeline.Item>
        ),
      })),
    ] as Array<{ ts: string; node: React.ReactNode } | null>
  )
    .filter((x): x is { ts: string; node: React.ReactNode } => x !== null)
    .sort((a, b) => new Date(a.ts).getTime() - new Date(b.ts).getTime());

  const timeline = (
    <Timeline active={eventItems.length} bulletSize={26}>
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

  const sqlBlock = (
    <SqlEditor value={r.sqlText} editable={false} height="100%" style={{ flex: 1, minHeight: 0 }} />
  );

  const previewBlock = (
    <PreviewPane
      isPending={preview.isPending}
      isError={preview.isError}
      error={preview.error}
      result={previewResult}
      lastPreview={
        lastPreview
          ? {
              success: lastPreview.success,
              at: lastPreview.at,
              affectedRows:
                lastPreview.affectedRows == null ? null : Number(lastPreview.affectedRows),
              error: lastPreview.error,
            }
          : null
      }
      onRunPreview={runPreview}
    />
  );

  const timelineBlock = (
    <>
      <Text tt="uppercase" fw={700} fz="xs" c="dimmed" mb="md" style={{ letterSpacing: "0.05em" }}>
        Timeline
      </Text>
      {timeline}
    </>
  );

  return (
    <Box
      style={{
        height: isMobile ? "calc(100dvh - 44px)" : "calc(100vh - 44px)",
        margin: "calc(-1 * var(--mantine-spacing-sm))",
        display: "flex",
        flexDirection: "column",
      }}
    >
      {/* Header: identity, status, and every action — always visible, never scrolls */}
      <Group
        justify="space-between"
        wrap="wrap"
        gap="sm"
        px="sm"
        py="xs"
        style={{ flexShrink: 0, borderBottom: BORDER }}
      >
        <Stack gap={2} style={{ minWidth: 0, flex: "1 1 280px" }}>
          <Group gap="sm" wrap="wrap" style={{ minWidth: 0 }}>
            <Title order={3} style={{ whiteSpace: "nowrap" }}>Update request</Title>
            <Badge color={STATUS_COLOR[r.status] ?? "gray"} size="lg" style={{ flexShrink: 0 }}>
              {r.status}
            </Badge>
            {r.status === "Pending" && lastPreviewFailed && (
              <Badge
                color="orange"
                variant="light"
                leftSection={<IconAlertTriangle size={12} />}
                style={{ flexShrink: 0 }}
              >
                Last dry run failed
              </Badge>
            )}
          </Group>
          <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
            <Text size="xs" c="dimmed" ff="monospace" title={r.id} truncate>
              #{r.id.slice(0, 8)}
            </Text>
            <Text size="xs" c="dimmed">·</Text>
            <Text size="xs" c="dimmed" truncate>
              {r.databaseDisplayName ?? "database unavailable"}
            </Text>
            <Text size="xs" c="dimmed">·</Text>
            <Text size="xs" c="dimmed" style={{ whiteSpace: "nowrap" }}>
              submitted {relativeDays(r.submittedAt)}
            </Text>
            {r.sourceRequestId && (
              <Text size="xs" c="dimmed" truncate>
                · recreated from{" "}
                <Text component="a" href={`/update/${r.sourceRequestId}`} size="xs" c="blue">
                  {r.sourceRequestId.slice(0, 8)}
                </Text>
              </Text>
            )}
          </Group>
        </Stack>
        <Group gap="xs" justify="flex-end" style={{ marginInlineStart: "auto" }}>
          {r.status === "Pending" && canApprove && (
            <>
              <Button color="green" onClick={() => openReview("approve")} loading={approve.isPending}>
                Approve
              </Button>
              <Button color="red" variant="outline" onClick={() => openReview("reject")} loading={reject.isPending}>
                Reject
              </Button>
            </>
          )}
          {(r.status === "Pending" || r.status === "Approved") && canSubmit && (
            <Button color="gray" variant="outline" onClick={() => openReview("cancel")} loading={cancel.isPending}>
              Cancel
            </Button>
          )}
          {r.status === "Approved" && canExecute && (
            <Button color="teal" onClick={handleExecute} loading={execute.isPending}>
              Execute
            </Button>
          )}
          {canPreview && (r.status === "Pending" || r.status === "Approved") && (
            <Button
              variant="light"
              color="grape"
              leftSection={<IconEye size={16} />}
              loading={preview.isPending}
              onClick={runPreview}
            >
              Preview
            </Button>
          )}
          {/* Cloning only makes sense once the request is closed; during review it is noise. */}
          {canSubmit && (r.status === "Rejected" || r.status === "Cancelled" || r.status === "Executed") && (
            <Button variant="light" onClick={() => navigate({ to: "/update/new", search: { from: id } })}>
              Recreate
            </Button>
          )}
        </Group>
      </Group>

      {isMobile ? (
        /* One scrolling column: SQL, then the dry-run pane, then the audit trail. */
        <Box style={{ flex: 1, minHeight: 0, overflow: "auto" }}>
          <Box
            p="xs"
            style={{ height: 260, borderBottom: BORDER, display: "flex", flexDirection: "column" }}
          >
            {sqlBlock}
          </Box>
          <Box style={{ height: 300, borderBottom: BORDER }}>{previewBlock}</Box>
          <Box p="md">{timelineBlock}</Box>
        </Box>
      ) : (
        /* Body: SQL over preview on the left, timeline on the right — all resizable */
        <Box style={{ flex: 1, minHeight: 0 }}>
          <Splitter
            orientation="horizontal"
            h="100%"
            withHandle={false}
            handleColor="var(--mantine-color-default-border)"
            lineSize={4}
          >
            <Splitter.Pane
              defaultSize={66}
              min={35}
              style={{ display: "flex", flexDirection: "column", minWidth: 0, overflow: "hidden" }}
            >
              <Splitter
                orientation="vertical"
                h="100%"
                withHandle={false}
                handleColor="var(--mantine-color-default-border)"
                lineSize={4}
                style={{ flex: 1 }}
              >
                <Splitter.Pane
                  defaultSize={52}
                  min={15}
                  style={{ display: "flex", flexDirection: "column", overflow: "hidden" }}
                >
                  <Box p="xs" style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0 }}>
                    {sqlBlock}
                  </Box>
                </Splitter.Pane>

                <Splitter.Pane defaultSize={48} min={15} style={{ overflow: "hidden" }}>
                  {previewBlock}
                </Splitter.Pane>
              </Splitter>
            </Splitter.Pane>

            <Splitter.Pane
              defaultSize={34}
              min={22}
              style={{ display: "flex", flexDirection: "column", overflow: "hidden", borderLeft: BORDER }}
            >
              <Box style={{ flex: 1, minHeight: 0, overflow: "auto", scrollbarGutter: "stable" }} p="md">
                {timelineBlock}
              </Box>
            </Splitter.Pane>
          </Splitter>
        </Box>
      )}

      {/* One review dialog for approve / reject / cancel */}
      <Modal opened={reviewAction !== null} onClose={closeReview} title={reviewAction ? REVIEW_COPY[reviewAction].title : ""}>
        {reviewAction && (
          <Stack gap="md">
            <Group gap="lg" style={{ rowGap: 4 }}>
              <Group gap={6}>
                <Text size="xs" c="dimmed">Database</Text>
                <Text size="sm">{r.databaseDisplayName ?? "unavailable"}</Text>
              </Group>
              <Group gap={6}>
                <Text size="xs" c="dimmed">Submitted</Text>
                <Text size="sm">{relativeDays(r.submittedAt)}</Text>
              </Group>
              <Group gap={6}>
                <Text size="xs" c="dimmed">Last dry run</Text>
                <Text size="sm">
                  {lastPreview
                    ? lastPreview.success
                      ? `${lastPreview.affectedRows ?? 0} rows would change`
                      : "failed"
                    : "never run"}
                </Text>
              </Group>
            </Group>

            {reviewAction === "approve" && lastPreviewFailed && (
              <Alert color="orange" title="The last dry run failed" icon={<IconAlertTriangle size={16} />}>
                <Text size="sm" fw={500}>
                  {lastPreviewError ?? "No rows were verified."}
                </Text>
                <Text size="sm" mt={6}>
                  Approving now sends SQL that has never run successfully. Re-run the preview first
                  if you can.
                </Text>
              </Alert>
            )}

            {reviewAction === "approve" && lastPreviewFailed && (
              <Checkbox
                checked={overridePreview}
                onChange={(e) => setOverridePreview(e.currentTarget.checked)}
                label="I reviewed this SQL without a successful dry run"
              />
            )}

            <Textarea
              label="Note"
              description={REVIEW_COPY[reviewAction].description}
              placeholder={REVIEW_COPY[reviewAction].placeholder}
              required
              minRows={3}
              value={reviewNote}
              onChange={(e) => setReviewNote(e.currentTarget.value)}
            />
            <Group justify="flex-end">
              <Button variant="subtle" onClick={closeReview}>
                Back
              </Button>
              <Button
                color={REVIEW_COPY[reviewAction].color}
                onClick={
                  reviewAction === "approve"
                    ? handleApprove
                    : reviewAction === "reject"
                      ? handleReject
                      : handleCancel
                }
                disabled={
                  !reviewNote.trim() || (reviewAction === "approve" && lastPreviewFailed && !overridePreview)
                }
                loading={approve.isPending || reject.isPending || cancel.isPending}
              >
                {REVIEW_COPY[reviewAction].confirm}
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </Box>
  );
}
