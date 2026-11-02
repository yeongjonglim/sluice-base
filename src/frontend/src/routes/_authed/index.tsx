import { Badge, Box, Button, Card, Group, SimpleGrid, Skeleton, Stack, Text, ThemeIcon, Title, VisuallyHidden } from "@mantine/core";
import {
  IconArrowRight,
  IconDatabase,
  IconHistory,
  IconInbox,
  IconShieldOff,
} from "@tabler/icons-react";
import { Link, createFileRoute, useNavigate } from "@tanstack/react-router";
import classes from "./Home.module.css";
import type { ReactNode } from "react";
import { useCatalogServer, useQueryHistory, useUpdateRequests } from "@/api/hooks";
import { useAuth } from "@/auth/hooks/useAuth.tsx";
import { useHasPermission } from "@/auth/permission.ts";
import { timeAgo } from "@/utils/timeAgo";

export const Route = createFileRoute("/_authed/")({
  component: HomePage,
});

function SectionCard({
  icon,
  title,
  count,
  action,
  children,
}: {
  icon: ReactNode;
  title: string;
  count?: number;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card withBorder padding="md" style={{ display: "flex", flexDirection: "column" }}>
      <Group justify="space-between" mb="sm" wrap="nowrap">
        <Group gap={8} wrap="nowrap">
          <ThemeIcon variant="light" size="sm" color="gray">
            {icon}
          </ThemeIcon>
          <Text component="h3" fw={600} style={{ margin: 0 }}>
            {title}
          </Text>
          {count !== undefined && count > 0 && (
            <Badge size="sm" variant="light" color="blue">
              {count}
            </Badge>
          )}
        </Group>
        {action}
      </Group>
      <Box style={{ flex: 1, minHeight: 0 }}>{children}</Box>
    </Card>
  );
}

function ApprovalRowLink({ id, children }: { id: string; children: ReactNode }) {
  return (
    <Link to="/update/$id" params={{ id }} className={classes.row}>
      {children}
    </Link>
  );
}

function PendingApprovals() {
  const navigate = useNavigate();
  const requests = useUpdateRequests({ status: "Pending" });
  const items = (requests.data?.requests ?? []).slice(0, 5);
  const total = requests.data?.requests.length ?? 0;

  return (
    <SectionCard
      icon={<IconInbox size={14} />}
      title="Waiting on you"
      count={total}
      action={
        <Button
          variant="subtle"
          size="compact-sm"
          rightSection={<IconArrowRight size={12} />}
          onClick={() => void navigate({ to: "/update", search: { status: "Pending" } })}
        >
          Review queue
        </Button>
      }
    >
      {requests.isPending && (
        <Stack gap="xs">
          {[1, 2].map((i) => (
            <Skeleton key={i} h={40} radius="sm" />
          ))}
        </Stack>
      )}
      {requests.isError && (
        <Text size="sm" c="red">
          Could not load the review queue.
        </Text>
      )}
      {requests.data && total === 0 && (
        <Text size="sm" c="dimmed">
          Nothing is waiting for your approval. New write requests appear here as soon as they are
          submitted.
        </Text>
      )}
      {items.map((r) => (
        <ApprovalRowLink key={r.id} id={r.id}>
          <Text size="sm" lineClamp={1}>
            {r.reason}
          </Text>
          <Text size="xs" c="dimmed" mt={2}>
            {r.submitterName ?? "Unknown submitter"} · {r.databaseDisplayName ?? "—"} ·{" "}
            {timeAgo(r.submittedAt)}
          </Text>
        </ApprovalRowLink>
      ))}
      {total > items.length && (
        <Box style={{ borderTop: "1px solid var(--mantine-color-default-border)", paddingTop: 8 }}>
          <Button
            variant="subtle"
            size="compact-sm"
            onClick={() => void navigate({ to: "/update", search: { status: "Pending" } })}
          >
            View all {total} requests
          </Button>
        </Box>
      )}
    </SectionCard>
  );
}

function RecentQueries() {
  const history = useQueryHistory({}, { live: false });
  const items = (history.data?.items ?? []).slice(0, 6);

  return (
    <SectionCard
      icon={<IconHistory size={14} />}
      title="Recent queries"
      action={
        <Button
          component={Link}
          to="/query/history"
          variant="subtle"
          size="compact-sm"
          rightSection={<IconArrowRight size={12} />}
        >
          History
        </Button>
      }
    >
      {history.isPending && <Skeleton h={80} radius="sm" />}
      {history.isError && (
        <Text size="sm" c="red">
          Could not load query history.
        </Text>
      )}
      {history.data && items.length === 0 && (
        <Text size="sm" c="dimmed">
          No queries yet. Open the editor and run one against a database you can read.
        </Text>
      )}
      {items.map((item) => (
        <Box
          key={item.id}
          style={{
            padding: "8px 6px",
            borderTop: "1px solid var(--mantine-color-default-border)",
          }}
        >
          <Group gap={8} wrap="nowrap">
            <VisuallyHidden>{item.status}</VisuallyHidden>
            <Box
              w={6}
              h={6}
              style={{
                borderRadius: "50%",
                flexShrink: 0,
                background:
                  item.status === "Success"
                    ? "var(--mantine-color-teal-6)"
                    : "var(--mantine-color-red-6)",
              }}
            />
            <Text size="xs" ff="monospace" truncate>
              {item.queryText.replace(/\s+/g, " ")}
            </Text>
          </Group>
          <Text size="xs" c="dimmed" mt={2}>
            {item.databaseDisplayName ?? "—"} · {timeAgo(item.executedAt)}
          </Text>
        </Box>
      ))}
    </SectionCard>
  );
}

function Databases({ canQuery }: { canQuery: boolean }) {
  const catalog = useCatalogServer();
  const rows = (catalog.data?.servers ?? []).flatMap((server) =>
    server.databases.map((database) => ({ ...database, serverName: server.name })),
  );

  function openInEditor(databaseId: string) {
    try {
      // The query page reads its database from session state on mount.
      sessionStorage.setItem("sluice:query:db", JSON.stringify(databaseId));
    } catch {
      // Storage can be unavailable (private mode); the editor still opens.
    }
  }

  return (
    <SectionCard icon={<IconDatabase size={14} />} title="Your databases">
      {catalog.isPending && <Skeleton h={80} radius="sm" />}
      {catalog.isError && (
        <Text size="sm" c="red">
          Could not load the database catalog.
        </Text>
      )}
      {catalog.data && rows.length === 0 && (
        <Text size="sm" c="dimmed">
          No databases are visible to your account yet. An administrator assigns access per
          database.
        </Text>
      )}
      {rows.map((database) => (
        <Group
          key={database.id}
          justify="space-between"
          wrap="nowrap"
          gap="sm"
          style={{ padding: "8px 4px", borderTop: "1px solid var(--mantine-color-default-border)" }}
        >
          <Box style={{ minWidth: 0 }}>
            <Text size="sm" truncate>
              {database.displayName}
            </Text>
            <Text size="xs" c="dimmed" truncate>
              {database.serverName}
              {database.canWrite ? " · writable" : ""}
            </Text>
          </Box>
          {canQuery && (
            <Button
              component={Link}
              to="/query"
              variant="default"
              size="compact-sm"
              aria-label={`Query ${database.displayName}`}
              onClick={() => openInEditor(database.id)}
            >
              Query
            </Button>
          )}
        </Group>
      ))}
    </SectionCard>
  );
}

function NoAccess() {
  return (
    <Card withBorder padding="lg" maw={640}>
      <Group gap="sm" mb="xs" wrap="nowrap">
        <ThemeIcon variant="light" color="gray" size="md">
          <IconShieldOff size={18} />
        </ThemeIcon>
        <Title order={3}>No access yet</Title>
      </Group>
      <Text size="sm" c="dimmed">
        Your account is signed in, but it has no permissions on this gateway. Ask an administrator
        for read access to the database you need — access is granted per database, and every query
        is audited.
      </Text>
    </Card>
  );
}

function HomePage() {
  const { user } = useAuth();
  const displayName = user.name ?? user.email;
  const canQuery = useHasPermission("query:execute");
  const canAudit = useHasPermission("query:audit");
  const canApprove = useHasPermission("update:approve");
  const canSubmit = useHasPermission("update:submit");

  const showsHistory = canQuery || canAudit;
  const hasWork = showsHistory || canApprove || canSubmit;

  return (
    <Stack gap="lg">
      <Box>
        <Title order={2}>Welcome, {displayName}</Title>
        <Text c="dimmed">Your secure gateway for database queries and controlled updates.</Text>
      </Box>

      {!hasWork && <NoAccess />}

      {canApprove && <PendingApprovals />}

      {hasWork && (
        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
          {showsHistory && <RecentQueries />}
          <Databases canQuery={canQuery} />
        </SimpleGrid>
      )}
    </Stack>
  );
}
