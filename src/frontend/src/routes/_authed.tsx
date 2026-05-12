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
  IconArrowsExchange,
  IconHistory,
  IconLayoutSidebarLeftCollapse,
  IconLayoutSidebarLeftExpand,
  IconLogout,
  IconMoon,
  IconServer,
  IconShieldLock,
  IconSparkles,
  IconSun,
  IconTerminal2,
} from "@tabler/icons-react";
import { Link, Outlet, createFileRoute, useLocation } from "@tanstack/react-router";
import { useState } from "react";
import { useMe } from "@/api/hooks.ts";
import { AuthProvider } from "@/auth/AuthProvider.tsx";
import { useHasPermission } from "@/auth/permission.ts";
import { useBranding } from "@/theme/BrandingContext";

export const Route = createFileRoute("/_authed")({
  component: AuthedLayout,
});

function AuthedLayout() {
  const me = useMe();
  const [opened, { toggle, close: closeMobileNav }] = useDisclosure();
  const [openedQuery, { toggle: toggleQuery }] = useDisclosure(true);
  const [sidebarCollapsed, { toggle: toggleSidebar }] = useDisclosure(false);
  const { colorScheme, toggleColorScheme } = useMantineColorScheme();
  const location = useLocation();
  const isAdmin = useHasPermission("permission:manage");
  const isServerAdmin = useHasPermission("server:manage");
  const canQuery = useHasPermission("query:execute");
  const canSubmitUpdates = useHasPermission("update:submit");
  const canApproveUpdates = useHasPermission("update:approve");
  const canExecuteUpdates = useHasPermission("update:execute");
  const canSeeUpdates = canSubmitUpdates || canApproveUpdates || canExecuteUpdates;
  const { appName, logoUrl } = useBranding();

  if (!me.data) {
    return null;
  }

  const displayName = me.data.name ?? me.data.email ?? me.data.id;

  return (
    <AuthProvider user={me.data}>
      <AppShell
        header={{ height: 44 }}
        navbar={{
          width: 150,
          breakpoint: "sm",
          collapsed: { desktop: sidebarCollapsed, mobile: !opened },
        }}
        padding="sm"
      >
        <AppShell.Header>
          <Group h="100%" px="sm" justify="space-between">
            <Group gap="xs">
              <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
              <ActionIcon
                variant="subtle"
                onClick={toggleSidebar}
                visibleFrom="sm"
                aria-label="Toggle sidebar"
              >
                {sidebarCollapsed ? (
                  <IconLayoutSidebarLeftExpand size={18} />
                ) : (
                  <IconLayoutSidebarLeftCollapse size={18} />
                )}
              </ActionIcon>
              <Link to="/" style={{ textDecoration: "none", color: "inherit" }}>
                {logoUrl ? (
                  <BrandingLogo appName={appName} logoUrl={logoUrl} />
                ) : (
                  <Title order={4}>{appName}</Title>
                )}
              </Link>
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

        <AppShell.Navbar p="xs">
          {/* <NavLink*/}
          {/*  label="Home"*/}
          {/*  leftSection={<IconHome size={16} />}*/}
          {/*  component={Link}*/}
          {/*  to="/"*/}
          {/*  active={location.pathname === "/"}*/}
          {/* />*/}
          {/* <NavLink*/}
          {/*  label="Health"*/}
          {/*  leftSection={<IconHeartRateMonitor size={16} />}*/}
          {/*  component={Link}*/}
          {/*  to="/health"*/}
          {/*  active={location.pathname === "/health"}*/}
          {/* />*/}
          {canQuery && (
            <NavLink
              label="Query"
              leftSection={<IconTerminal2 size={16} />}
              defaultOpened
              opened={openedQuery}
              onClick={toggleQuery}
            >
              <NavLink
                label="Editor"
                leftSection={<IconSparkles size={16} />}
                component={Link}
                to="/query"
                activeOptions={{ exact: true }}
                active={location.pathname === "/query"}
                pl="md"
                onClick={closeMobileNav}
              />
              <NavLink
                label="History"
                leftSection={<IconHistory size={16} />}
                component={Link}
                to="/query/history"
                active={location.pathname === "/query/history"}
                pl="md"
                onClick={closeMobileNav}
              />
            </NavLink>
          )}
          {canSeeUpdates && (
            <NavLink
              label="Updates"
              leftSection={<IconArrowsExchange size={16} />}
              component={Link}
              to="/update"
              active={location.pathname.startsWith("/update")}
              onClick={closeMobileNav}
            />
          )}
          {isServerAdmin && (
            <NavLink
              label="Servers"
              leftSection={<IconServer size={16} />}
              component={Link}
              to="/server"
              active={location.pathname === "/server"}
              onClick={closeMobileNav}
            />
          )}
          {isAdmin && (
            <NavLink
              label="Permission"
              leftSection={<IconShieldLock size={16} />}
              component={Link}
              to="/permission"
              active={location.pathname === "/permission"}
              onClick={closeMobileNav}
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

function BrandingLogo({ appName, logoUrl }: { appName: string; logoUrl: string }) {
  const [imgError, setImgError] = useState(false);
  if (imgError) return <Title order={4}>{appName}</Title>;
  return (
    <img src={logoUrl} alt={appName} style={{ maxHeight: 24 }} onError={() => setImgError(true)} />
  );
}
