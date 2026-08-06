import { Avatar, Group, Stack, Text } from "@mantine/core";

/**
 * Identity block shown at the top of the avatar menu dropdown: the signed-in
 * user's avatar next to their name (prominent) above their email (dimmed).
 * Renders nothing when both name and email are absent so the dropdown falls
 * back to just its actions.
 */
export function UserMenuHeader({ name, email }: { name?: string | null; email?: string | null }) {
  if (!name && !email) {
    return null;
  }
  return (
    <Group gap="sm" wrap="nowrap" px="sm" py={6} maw={240}>
      <Avatar name={name ?? email ?? undefined} color="initials" radius="xl" />
      <Stack gap={0} style={{ minWidth: 0 }}>
        {name && (
          <Text size="sm" fw={500} lh={1.2} truncate title={name}>
            {name}
          </Text>
        )}
        {email && (
          <Text size="xs" c="dimmed" truncate title={email}>
            {email}
          </Text>
        )}
      </Stack>
    </Group>
  );
}
