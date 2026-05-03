import {
  ActionIcon,
  AppShell,
  Burger,
  Group,
  Menu,
  NavLink,
  Text,
  Title,
  useMantineColorScheme,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
  IconChevronDown,
  IconHeartRateMonitor,
  IconHome,
  IconLogout,
  IconMoon,
  IconSun,
} from "@tabler/icons-react";
import { createFileRoute, Link, Outlet, useLocation } from "@tanstack/react-router";
import { useMe } from "../api/hooks";
import { AuthProvider } from "../auth/AuthProvider";

export const Route = createFileRoute("/_authed")({
  component: AuthedLayout,
});

function AuthedLayout() {
  const me = useMe();
  const [opened, { toggle }] = useDisclosure();
  const { colorScheme, toggleColorScheme } = useMantineColorScheme();
  const location = useLocation();

  if (!me.data) {
    // useMe is prefetched by __root's beforeLoad, so this should never render.
    return null;
  }

  const displayName = me.data.name ?? me.data.preferredUsername ?? me.data.email ?? "user";

  return (
    <AuthProvider user={me.data}>
      <AppShell
        header={{ height: 56 }}
        navbar={{
          width: 240,
          breakpoint: "sm",
          collapsed: { mobile: !opened },
        }}
        padding="md"
      >
        <AppShell.Header>
          <Group h="100%" px="md" justify="space-between">
            <Group gap="sm">
              <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
              <Title order={3}>SluiceBase</Title>
            </Group>
            <Group gap="xs">
              <ActionIcon
                variant="subtle"
                onClick={() => toggleColorScheme()}
                aria-label="Toggle color scheme"
              >
                {colorScheme === "dark" ? <IconSun size={18} /> : <IconMoon size={18} />}
              </ActionIcon>
              <Menu position="bottom-end" withinPortal>
                <Menu.Target>
                  <ActionIcon variant="subtle" aria-label="User menu">
                    <Group gap={4}>
                      <Text size="sm">{displayName}</Text>
                      <IconChevronDown size={14} />
                    </Group>
                  </ActionIcon>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item component="a" href="/logout" leftSection={<IconLogout size={14} />}>
                    Log out
                  </Menu.Item>
                </Menu.Dropdown>
              </Menu>
            </Group>
          </Group>
        </AppShell.Header>

        <AppShell.Navbar p="sm">
          <NavLink
            label="Home"
            leftSection={<IconHome size={16} />}
            component={Link}
            to="/"
            active={location.pathname === "/"}
          />
          <NavLink
            label="Health"
            leftSection={<IconHeartRateMonitor size={16} />}
            component={Link}
            to="/health"
            active={location.pathname === "/health"}
          />
        </AppShell.Navbar>

        <AppShell.Main>
          <Outlet />
        </AppShell.Main>
      </AppShell>
    </AuthProvider>
  );
}
