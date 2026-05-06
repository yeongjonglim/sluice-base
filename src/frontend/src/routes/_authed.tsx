import {
  ActionIcon,
  AppShell,
  Avatar,
  Burger,
  Group,
  Menu,
  NavLink,
  Title,
  useMantineColorScheme,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
  IconHeartRateMonitor,
  IconHome,
  IconLogout,
  IconMoon,
  IconServer,
  IconShieldLock,
  IconSun,
} from "@tabler/icons-react";
import { Link, Outlet, createFileRoute, useLocation } from "@tanstack/react-router";
import { useMe } from "@/api/hooks.ts";
import { AuthProvider } from "@/auth/AuthProvider.tsx";
import { useHasPermission } from "@/auth/permission.ts";

export const Route = createFileRoute("/_authed")({
  component: AuthedLayout,
});

function AuthedLayout() {
  const me = useMe();
  const [opened, { toggle }] = useDisclosure();
  const { colorScheme, toggleColorScheme } = useMantineColorScheme();
  const location = useLocation();
  const isAdmin = useHasPermission("permission:manage");
  const isServerAdmin = useHasPermission("server:manage");

  if (!me.data) {
    return null;
  }

  const displayName = me.data.name ?? me.data.email;

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
                  <Avatar
                    data-testid={"user-menu"}
                    name={displayName}
                    color={"initials"}
                    style={{ cursor: "pointer" }}
                  />
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
          {isServerAdmin && (
            <NavLink
              label="Servers"
              leftSection={<IconServer size={16} />}
              component={Link}
              to="/server"
              active={location.pathname === "/server"}
            />
          )}
          {isAdmin && (
            <NavLink
              label="Permission"
              leftSection={<IconShieldLock size={16} />}
              component={Link}
              to="/permission"
              active={location.pathname === "/permission"}
            />
          )}
        </AppShell.Navbar>

        <AppShell.Main>
          <Outlet />
        </AppShell.Main>
      </AppShell>
    </AuthProvider>
  );
}
