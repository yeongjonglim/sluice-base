import { useMe } from "../api/hooks";

export function useHasPermission(permission: Permission): boolean {
  const me = useMe();
  return me.data?.permissions.includes(permission) ?? false;
}

export type Permission =
  | "permission:manage"
  | "server:manage"
  | "query:execute"
  | "update:submit"
  | "update:approve"
  | "update:execute"; // Permissions that we know of in the frontend
