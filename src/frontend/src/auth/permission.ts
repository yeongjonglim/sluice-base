import { useMe } from "../api/hooks";

export function useHasPermission(permission: Permission): boolean {
  const me = useMe();
  return me.data?.permissions.includes(permission) ?? false;
}

export type Permission =
  | "permission:manage"
  | "server:manage"
  | "group:manage"
  | "query:execute"
  | "query:audit"
  | "update:submit"
  | "update:approve"
  | "update:execute"; // Permissions that we know of in the frontend

// Record<Permission, …> forces a label for every known permission at compile time.
export const PERMISSION_LABELS: Record<Permission, { short: string; full: string }> = {
  "group:manage": { short: "Group", full: "Manage groups" },
  "permission:manage": { short: "Permission", full: "Manage permissions" },
  "query:audit": { short: "Audit", full: "Audit read queries" },
  "query:execute": { short: "Query", full: "Run read queries" },
  "server:manage": { short: "Server", full: "Manage servers" },
  "update:approve": { short: "Approve", full: "Approve update requests" },
  "update:execute": { short: "Execute", full: "Execute approved updates" },
  "update:submit": { short: "Submit", full: "Submit update requests" },
};

// Safe accessor: backend catalog values are plain strings, so fall back to the
// raw permission name rather than crashing on an unknown key. The cast widens the
// lookup to include `undefined`, which the project's tsconfig otherwise hides.
export function permissionLabel(permission: string): { short: string; full: string } {
  const labels = PERMISSION_LABELS as Record<
    string,
    { short: string; full: string } | undefined
  >;
  return labels[permission] ?? { short: permission, full: permission };
}
