import { Stack, Text, Title } from "@mantine/core";
import { createFileRoute } from "@tanstack/react-router";

import { useAuth } from "@/auth/hooks/useAuth.tsx";

export const Route = createFileRoute("/_authed/")({
  component: HomePage,
});

function HomePage() {
  const { user } = useAuth();
  const displayName = user.name ?? user.email;

  return (
    <Stack gap="xs">
      <Title order={2}>Welcome, {displayName}</Title>
      <Text c="dimmed">Your secure gateway for database queries and controlled updates.</Text>
    </Stack>
  );
}
