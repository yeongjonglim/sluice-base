import { Paper, Stack, Text, Title } from "@mantine/core";
import type { Principal } from "./ByPrincipalTab";
import { useEffectiveUserPermissions } from "@/api/hooks";

export default function PrincipalDetail({
  principal,
}: {
  principal: Principal;
}) {
  // Hook is always called; passing null when the principal is a group keeps it
  // disabled (rules-of-hooks: never call hooks conditionally).
  const effective = useEffectiveUserPermissions(
    principal.type === "user" ? principal.id : null,
  );

  return (
    <Stack gap="md" style={{ flex: 1 }}>
      <div>
        <Title order={3}>
          {principal.type === "user"
            ? (principal.email ?? principal.id)
            : principal.name}
        </Title>
        {principal.type === "user" && principal.name && (
          <Text size="sm" c="dimmed">
            {principal.name}
          </Text>
        )}
        {principal.type === "group" && principal.description && (
          <Text size="sm" c="dimmed">
            {principal.description}
          </Text>
        )}
      </div>

      <Paper withBorder p="md">
        <Title order={4} mb="sm">
          Global Permissions
        </Title>
        {principal.type === "group" ? (
          <Text size="sm" c="dimmed">
            Group permission editing — coming soon.
          </Text>
        ) : effective.data ? (
          <Text size="sm">{effective.data.global.length} permission(s)</Text>
        ) : (
          <Text size="sm" c="dimmed">
            Loading…
          </Text>
        )}
        {/* TODO: render permissions with direct/inherited source badges */}
      </Paper>

      {principal.type === "user" && (
        <Paper withBorder p="md">
          <Title order={4} mb="sm">
            Group Memberships
          </Title>
          {!effective.data ? (
            <Text size="sm" c="dimmed">
              Loading…
            </Text>
          ) : effective.data.memberships.length > 0 ? (
            <Stack gap={2}>
              {effective.data.memberships.map((m) => (
                <Text size="sm" key={m.groupId}>
                  {m.groupName}
                </Text>
              ))}
            </Stack>
          ) : (
            <Text size="sm" c="dimmed" fs="italic">
              Not in any groups
            </Text>
          )}
        </Paper>
      )}

      <Paper withBorder p="md">
        <Title order={4} mb="sm">
          Database-Scoped Roles
        </Title>
        {/* TODO: render effective db-role matrix with edit-direct semantics */}
        <Text size="sm" c="dimmed">
          Coming soon.
        </Text>
      </Paper>

      <Paper withBorder p="md">
        <Title order={4} mb="sm">
          Column Bypasses
        </Title>
        {/* TODO: render effective column-bypass matrix with edit-direct semantics */}
        <Text size="sm" c="dimmed">
          Coming soon.
        </Text>
      </Paper>
    </Stack>
  );
}
