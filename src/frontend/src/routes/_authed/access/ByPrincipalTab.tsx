import {
  Avatar,
  Badge,
  Flex,
  NavLink,
  Paper,
  ScrollArea,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
} from "@mantine/core";
import { useState } from "react";
import PrincipalDetail from "./PrincipalDetail";
import { useGroups, useUsers } from "@/api/hooks";

export type Principal =
  | { type: "user"; id: string; email: string | null; name: string | null }
  | {
      type: "group";
      id: string;
      name: string;
      description: string | null;
      memberCount: number;
    };

export default function ByPrincipalTab({
  initialSegment,
}: {
  initialSegment: "users" | "groups";
}) {
  const users = useUsers();
  const groups = useGroups();
  const [search, setSearch] = useState("");
  const [segment, setSegment] = useState<"users" | "groups">(initialSegment);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const userList: Array<Principal> = (users.data?.users ?? []).map((u) => ({
    type: "user",
    id: u.id,
    email: u.email ?? null,
    name: u.name ?? null,
  }));

  const groupList: Array<Principal> = (groups.data?.groups ?? []).map((g) => ({
    type: "group",
    id: g.id,
    name: g.name,
    description: g.description,
    memberCount: g.memberCount,
  }));

  const principals = segment === "users" ? userList : groupList;
  const term = search.toLowerCase();
  const filtered = principals.filter((p) =>
    p.type === "user"
      ? (p.email ?? "").toLowerCase().includes(term) ||
        (p.name ?? "").toLowerCase().includes(term)
      : p.name.toLowerCase().includes(term),
  );

  const selected =
    filtered.find((p) => p.id === selectedId) ?? filtered.at(0) ?? null;

  return (
    <Flex gap="md" align="flex-start">
      <Paper withBorder style={{ flex: "0 0 260px" }}>
        <Stack gap="xs" p="xs">
          <SegmentedControl
            value={segment}
            onChange={(val) => {
              setSegment(val);
              setSelectedId(null);
            }}
            data={[
              { label: "Users", value: "users" },
              { label: "Groups", value: "groups" },
            ]}
            fullWidth
            size="xs"
          />
          <TextInput
            placeholder="Search…"
            size="xs"
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
          />
        </Stack>
        <ScrollArea.Autosize mah={520}>
          {filtered.map((p) => (
            <NavLink
              key={p.id}
              label={p.type === "user" ? (p.email ?? p.id) : p.name}
              description={
                p.type === "user"
                  ? (p.name ?? undefined)
                  : (p.description ?? "No description")
              }
              leftSection={
                <Avatar
                  size="sm"
                  name={p.type === "user" ? (p.email ?? "?") : p.name}
                  color="initials"
                />
              }
              rightSection={
                p.type === "group" ? (
                  <Badge size="xs" variant="light">
                    {p.memberCount}
                  </Badge>
                ) : undefined
              }
              active={selected?.id === p.id}
              onClick={() => setSelectedId(p.id)}
            />
          ))}
          {filtered.length === 0 && (
            <Text size="sm" c="dimmed" p="xs">
              No {segment} found.
            </Text>
          )}
        </ScrollArea.Autosize>
      </Paper>

      {selected ? (
        <PrincipalDetail principal={selected} />
      ) : (
        <Paper withBorder p="xl" style={{ flex: 1 }}>
          <Text c="dimmed" ta="center">
            Select a principal to view details
          </Text>
        </Paper>
      )}
    </Flex>
  );
}
