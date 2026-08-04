import { Stack, Text } from "@mantine/core";

/**
 * Identity block shown at the top of the avatar menu dropdown: the signed-in
 * user's name (prominent) above their email (dimmed). Renders nothing when both
 * are absent so the dropdown falls back to just its actions.
 */
export function UserMenuHeader({ name, email }: { name?: string | null; email?: string | null }) {
  if (!name && !email) {
    return null;
  }
  return (
    <Stack gap={0} px="sm" py={6}>
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
  );
}
