import { Stack, Text, Title } from "@mantine/core";
import { createFileRoute } from "@tanstack/react-router";
import { useAuth } from "@/auth/AuthProvider.tsx";

export const Route = createFileRoute("/_authed/")({
  component: HomePage,
});

function HomePage() {
  const { user } = useAuth();
  const displayName = user.name ?? user.preferredUsername ?? user.email ?? "stranger";

  return (
    <Stack gap="xs">
      <Title order={2}>Welcome, {displayName}</Title>
      <Text c="dimmed">
        SluiceBase Foundations is up. Server registry, schema browser, query workspace, and approval
        workflow ship in later sub-projects.
      </Text>
    </Stack>
  );
}
