import { useMe } from "../api/hooks";

export function useHasPermission(permission: string): boolean {
  const me = useMe();
  return me.data?.permissions.includes(permission) ?? false;
}
