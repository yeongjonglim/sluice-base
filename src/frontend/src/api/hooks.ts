import {
  QueryCache,
  QueryClient,
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { notifications } from "@mantine/notifications";
import type { SQLNamespace } from "@codemirror/lang-sql";
import type { paths } from "./schema.ts";
import { ApiError, apiRequest } from "@/api/client";
import { downloadTextFile } from "@/utils/download";
import { quoteSqlIdentifier } from "@/utils/quoteSqlIdentifier";

export function createAppQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({
      onError: (error) => {
        if (error instanceof ApiError && error.status === 401) {
          window.location.assign("/login");
        }
      },
    }),
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => {
          if (error instanceof ApiError && error.status === 401) {
            return false;
          }
          return failureCount < 2;
        },
        staleTime: 30_000,
      },
    },
  });
}

export type MeResponse = paths["/api/me"]["get"]["responses"][200]["content"]["application/json"];

export const meQueryOptions = queryOptions({
  queryKey: ["me"] as const,
  queryFn: () => apiRequest<void, MeResponse>("/api/me"),
});

export function useMe() {
  return useQuery(meQueryOptions);
}

export function useAuthedHealth() {
  return useQuery({
    queryKey: ["health-authed"] as const,
    queryFn: () =>
      apiRequest<
        void,
        paths["/api/health/authed"]["get"]["responses"][200]["content"]["application/json"]
      >("/api/health/authed"),
  });
}

export function useUsers() {
  return useQuery({
    queryKey: ["admin", "user"] as const,
    queryFn: () =>
      apiRequest<
        void,
        paths["/api/admin/user"]["get"]["responses"][200]["content"]["application/json"]
      >("/api/admin/user"),
  });
}

export function usePermissionCatalog() {
  return useQuery({
    queryKey: ["permission", "catalog"] as const,
    queryFn: () =>
      apiRequest<
        void,
        paths["/api/permission/catalog"]["get"]["responses"][200]["content"]["application/json"]
      >("/api/permission/catalog"),
    staleTime: Infinity,
  });
}

export function useGrantPermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, permission }: { userId: string; permission: string }) =>
      apiRequest<
        paths["/api/admin/user/{userId}/permission"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/user/${userId}/permission`, {
        method: "POST",
        body: { permission },
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      notifications.show({ title: "Permission granted", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Grant failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRevokePermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, permission }: { userId: string; permission: string }) =>
      apiRequest(`/api/admin/user/${userId}/permission/${permission}`, {
        method: "DELETE",
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      notifications.show({ title: "Permission revoked", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Revoke failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

function formatApiError(error: ApiError): string {
  const body = error.body as Record<string, unknown> | null;
  if (body && typeof body === "object" && "errors" in body) {
    const errors = body["errors"] as Record<string, string>;
    return Object.values(errors).flat().join(" ");
  }
  return error.message;
}

// ── Catalog ───────────────────────────────────────────────────────────────

export type CatalogServersResponse =
  paths["/api/catalog/server"]["get"]["responses"][200]["content"]["application/json"];

export function useCatalogServer() {
  return useQuery({
    queryKey: ["catalog", "server"] as const,
    queryFn: () => apiRequest<void, CatalogServersResponse>("/api/catalog/server"),
    select: (data) => ({
      servers: [...data.servers]
        .sort((a, b) => a.name.localeCompare(b.name))
        .map((s) => ({
          ...s,
          databases: [...s.databases].sort((a, b) => a.displayName.localeCompare(b.displayName)),
        })),
    }),
  });
}

// ── Server registry ───────────────────────────────────────────────────────

export type ServerListResponse =
  paths["/api/server"]["get"]["responses"][200]["content"]["application/json"];
export type ServerItem = ServerListResponse["servers"][0];
export type TestConnectionResponse =
  paths["/api/server/{serverId}/database/{databaseId}/test"]["post"]["responses"][200]["content"]["application/json"];
export type CreateServerRequest =
  paths["/api/server"]["post"]["requestBody"]["content"]["application/json"];
export type ExecuteQueryResponse =
  paths["/api/query"]["post"]["responses"][200]["content"]["application/json"];

export function useServers() {
  return useQuery({
    queryKey: ["server"] as const,
    queryFn: () => apiRequest<void, ServerListResponse>("/api/server"),
    select: (data) => ({
      servers: [...data.servers]
        .sort((a, b) => a.name.localeCompare(b.name))
        .map((s) => ({
          ...s,
          databases: [...s.databases].sort((a, b) => a.displayName.localeCompare(b.displayName)),
        })),
    }),
  });
}

export function useCreateServer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateServerRequest) =>
      apiRequest<CreateServerRequest, ServerItem>("/api/server", { method: "POST", body }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["server"] });
      notifications.show({ title: "Server created", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Create failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useUpdateServer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      body,
    }: {
      id: string;
      body: paths["/api/server/{id}"]["put"]["requestBody"]["content"]["application/json"];
    }) =>
      apiRequest<
        paths["/api/server/{id}"]["put"]["requestBody"]["content"]["application/json"],
        ServerItem
      >(`/api/server/${id}`, { method: "PUT", body }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["server"] });
      notifications.show({ title: "Server updated", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Update failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useDeleteServer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiRequest(`/api/server/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["server"] });
      notifications.show({ title: "Server deleted", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Delete failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

// Add after useDeleteServer:

export function useCreateCredential(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: paths["/api/server/{serverId}/credential"]["post"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/credential"]["post"]["responses"][201]["content"]["application/json"]>(
        `/api/server/${serverId}/credential`,
        { method: "POST", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useUpdateCredential(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ credentialId, ...req }: { credentialId: string } & paths["/api/server/{serverId}/credential/{credentialId}"]["put"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/credential/{credentialId}"]["put"]["responses"][200]["content"]["application/json"]>(
        `/api/server/${serverId}/credential/${credentialId}`,
        { method: "PUT", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useDeleteCredential(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (credentialId: string) =>
      apiRequest<void, void>(`/api/server/${serverId}/credential/${credentialId}`, { method: "DELETE" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useCreateDatabase(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: paths["/api/server/{serverId}/database"]["post"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/database"]["post"]["responses"][201]["content"]["application/json"]>(
        `/api/server/${serverId}/database`,
        { method: "POST", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useUpdateDatabase(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ databaseId, ...req }: { databaseId: string } & paths["/api/server/{serverId}/database/{databaseId}"]["put"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/database/{databaseId}"]["put"]["responses"][200]["content"]["application/json"]>(
        `/api/server/${serverId}/database/${databaseId}`,
        { method: "PUT", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useDeleteDatabase(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (databaseId: string) =>
      apiRequest<void, void>(`/api/server/${serverId}/database/${databaseId}`, { method: "DELETE" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useTestDatabaseConnection(serverId: string) {
  return useMutation({
    mutationFn: (databaseId: string) =>
      apiRequest<void, paths["/api/server/{serverId}/database/{databaseId}/test"]["post"]["responses"][200]["content"]["application/json"]>(
        `/api/server/${serverId}/database/${databaseId}/test`,
        { method: "POST" },
      ),
  });
}

// hooks.ts — update useServers return type annotation (auto-inferred from new schema.ts)
// useSchema: change parameter name and path

type SchemaTreeResponse = paths["/api/schema/{databaseId}"]["get"]["responses"][200]["content"]["application/json"];

// Wraps an identifier in a lang-sql completion whose inserted text (`apply`) is
// quoted via quoteSqlIdentifier only when it needs it. lang-sql already quotes
// mixed-case/special identifiers on its own, but it is blind to reserved words —
// a bare `group` passes its lowercase check and inserts unquoted — so we make
// quoteSqlIdentifier authoritative across every level, matching the identifier
// quoting the generate-query snippet applies (see buildSelectSnippet).
function identifierCompletion(name: string, type: "type" | "property") {
  const quoted = quoteSqlIdentifier(name);
  return quoted === name ? { label: name, type } : { label: name, type, apply: quoted };
}

export function schemaToCompletions(
  tree: {
    schemas: Array<{
      name: string;
      tables: Array<{
        name: string;
        columns: Array<{ name: string; isRestricted: boolean }>;
      }>;
      views?: Array<{
        name: string;
        columns: Array<{ name: string; isRestricted: boolean }>;
      }>;
    }>;
  },
): SQLNamespace {
  const schemas: Record<string, SQLNamespace> = {};
  for (const schema of tree.schemas) {
    const relations = [...schema.tables, ...(schema.views ?? [])];
    const tables: Record<string, SQLNamespace> = {};
    for (const relation of relations) {
      tables[relation.name] = {
        self: identifierCompletion(relation.name, "type"),
        children: relation.columns
          .filter((c) => !c.isRestricted)
          .map((c) => identifierCompletion(c.name, "property")),
      };
    }
    schemas[schema.name] = {
      self: identifierCompletion(schema.name, "type"),
      children: tables,
    };
  }
  return schemas;
}

export function useSchema(databaseId: string | null) {
  return useQuery({
    queryKey: ["schema", databaseId] as const,
    enabled: databaseId !== null,
    queryFn: () =>
      apiRequest<void, SchemaTreeResponse>(
        `/api/schema/${databaseId}`,
      ),
  });
}

export function useSchemaCompletions(databaseId: string | null) {
  return useQuery({
    queryKey: ["schema", databaseId] as const,
    enabled: databaseId !== null,
    queryFn: () =>
      apiRequest<void, SchemaTreeResponse>(
        `/api/schema/${databaseId}`,
      ),
    select: schemaToCompletions,
  });
}

export function useExportSchemaDdl() {
  return useMutation({
    mutationFn: async ({ databaseId, filename }: { databaseId: string; filename: string }) => {
      const ddl = await apiRequest<void, string>(`/api/schema/${databaseId}/ddl`);
      downloadTextFile(ddl, filename, "application/sql");
    },
    onError: (error) => {
      notifications.show({
        title: "Export failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useTestConnection() {
  return useMutation({
    mutationFn: (id: string) =>
      apiRequest<void, TestConnectionResponse>(`/api/server/${id}/test`, { method: "POST" }),
  });
}

// ── Update requests ───────────────────────────────────────────────────────

export type UpdateRequestListResponse =
  paths["/api/update"]["get"]["responses"][200]["content"]["application/json"];
export type UpdateRequestDetail =
  paths["/api/update/{id}"]["get"]["responses"][200]["content"]["application/json"];

export interface UpdateRequestFilters {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
}

export function useUpdateRequests(filters: UpdateRequestFilters) {
  return useQuery({
    queryKey: ["update", "list", filters] as const,
    queryFn: () => {
      const params = new URLSearchParams();
      if (filters.from) params.set("from", filters.from);
      if (filters.to) params.set("to", filters.to);
      if (filters.databaseId) params.set("databaseId", filters.databaseId);
      if (filters.status) params.set("status", filters.status);
      const qs = params.toString();
      return apiRequest<void, UpdateRequestListResponse>(
        qs ? `/api/update?${qs}` : "/api/update",
      );
    },
  });
}

export function useUpdateRequest(id: string) {
  return useQuery({
    queryKey: ["update", id] as const,
    queryFn: () => apiRequest<void, UpdateRequestDetail>(`/api/update/${id}`),
    enabled: !!id,
  });
}

// useSubmitUpdate: change body field name
export function useSubmitUpdate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ databaseId, sqlText, reason, sourceRequestId }: { databaseId: string; sqlText: string; reason: string; sourceRequestId?: string }) =>
      apiRequest<
        paths["/api/update"]["post"]["requestBody"]["content"]["application/json"],
        paths["/api/update"]["post"]["responses"][201]["content"]["application/json"]
      >("/api/update", { method: "POST", body: { databaseId, sqlText, reason, sourceRequestId } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["update"] }),
  });
}


export function useApproveUpdate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note: string }) =>
      apiRequest<{ note: string }, UpdateRequestDetail>(`/api/update/${id}/approve`, {
        method: "POST",
        body: { note },
      }),
    onSuccess: (data) => {
      void qc.invalidateQueries({ queryKey: ["update"] });
      void qc.invalidateQueries({ queryKey: ["update", data.id] });
      notifications.show({ title: "Request approved", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Approve failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRejectUpdate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note: string }) =>
      apiRequest<{ note: string }, UpdateRequestDetail>(`/api/update/${id}/reject`, {
        method: "POST",
        body: { note },
      }),
    onSuccess: (data) => {
      void qc.invalidateQueries({ queryKey: ["update"] });
      void qc.invalidateQueries({ queryKey: ["update", data.id] });
      notifications.show({ title: "Request rejected", message: "", color: "red" });
    },
    onError: (error) => {
      notifications.show({
        title: "Reject failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useCancelUpdate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note: string }) =>
      apiRequest<{ note: string }, UpdateRequestDetail>(`/api/update/${id}/cancel`, {
        method: "POST",
        body: { note },
      }),
    onSuccess: (data) => {
      void qc.invalidateQueries({ queryKey: ["update"] });
      void qc.invalidateQueries({ queryKey: ["update", data.id] });
      notifications.show({ title: "Request cancelled", message: "", color: "gray" });
    },
    onError: (error) => {
      notifications.show({
        title: "Cancel failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useExecuteUpdate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiRequest<void, UpdateRequestDetail>(`/api/update/${id}/execute`, { method: "POST" }),
    onSuccess: (data) => {
      void qc.invalidateQueries({ queryKey: ["update"] });
      void qc.invalidateQueries({ queryKey: ["update", data.id] });
      notifications.show({ title: "Executed", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Execute failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

// ── Database role management ──────────────────────────────────────────────

export type AdminServerListResponse =
  paths["/api/admin/server"]["get"]["responses"][200]["content"]["application/json"];
export type AdminServerItem = AdminServerListResponse["servers"][0];
export type AdminDatabaseItem = AdminServerItem["databases"][0];
export type DatabaseRoleListResponse =
  paths["/api/admin/database/{databaseId}/role"]["get"]["responses"][200]["content"]["application/json"];
export type UserRoleListResponse =
  paths["/api/admin/user/{userId}/role"]["get"]["responses"][200]["content"]["application/json"];

export function useAdminServers() {
  return useQuery({
    queryKey: ["admin", "server"] as const,
    queryFn: () => apiRequest<void, AdminServerListResponse>("/api/admin/server"),
  });
}

export function useDatabaseRoles(databaseId: string | null) {
  return useQuery({
    queryKey: ["admin", "database", databaseId, "role"] as const,
    enabled: databaseId !== null,
    queryFn: () =>
      apiRequest<void, DatabaseRoleListResponse>(`/api/admin/database/${databaseId}/role`),
  });
}

export function useUserRoles(userId: string | null) {
  return useQuery({
    queryKey: ["admin", "user", userId, "role"] as const,
    enabled: userId !== null,
    queryFn: () =>
      apiRequest<void, UserRoleListResponse>(`/api/admin/user/${userId}/role`),
  });
}

export function useAssignDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      userId,
      permission,
    }: {
      databaseId: string;
      userId: string;
      permission: string;
    }) =>
      apiRequest<
        paths["/api/admin/database/{databaseId}/role"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/database/${databaseId}/role`, {
        method: "POST",
        body: { userId, permission },
      }),
    onSuccess: (_data, { databaseId, userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Assign failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useAssignUserRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      userId,
      databaseId,
      permission,
    }: {
      userId: string;
      databaseId: string;
      permission: string;
    }) =>
      apiRequest<
        paths["/api/admin/user/{userId}/role"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/user/${userId}/role`, {
        method: "POST",
        body: { databaseId, permission },
      }),
    onSuccess: (_data, { databaseId, userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Assign failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRemoveDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      userId,
      permission,
    }: {
      databaseId: string;
      userId: string;
      permission: string;
    }) =>
      apiRequest(`/api/admin/database/${databaseId}/role/${userId}/${permission}`, {
        method: "DELETE",
      }),
    onSuccess: (_data, { databaseId, userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Remove failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

// ── Access groups ─────────────────────────────────────────────────────────

export type GroupListResponse =
  paths["/api/admin/group"]["get"]["responses"][200]["content"]["application/json"];
export type GroupDetailResponse =
  paths["/api/admin/group/{groupId}"]["get"]["responses"][200]["content"]["application/json"];
export type GroupRef = GroupDetailResponse["members"][0];
export type EffectivePermission =
  paths["/api/admin/user"]["get"]["responses"][200]["content"]["application/json"]["users"][0]["permissions"][0];

export function useGroups() {
  return useQuery({
    queryKey: ["admin", "groups"] as const,
    queryFn: () =>
      apiRequest<void, GroupListResponse>("/api/admin/group"),
  });
}

export function useGroup(groupId: string | null) {
  return useQuery({
    queryKey: ["admin", "group", groupId] as const,
    enabled: groupId !== null,
    queryFn: () =>
      apiRequest<void, GroupDetailResponse>(`/api/admin/group/${groupId}`),
  });
}

export function useCreateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: paths["/api/admin/group"]["post"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<
        paths["/api/admin/group"]["post"]["requestBody"]["content"]["application/json"],
        void
      >("/api/admin/group", { method: "POST", body }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "groups"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Create group failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useUpdateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      groupId,
      ...body
    }: { groupId: string } & paths["/api/admin/group/{groupId}"]["patch"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<
        paths["/api/admin/group/{groupId}"]["patch"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/group/${groupId}`, { method: "PATCH", body }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "groups"] });
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId] });
    },
    onError: (error) => {
      notifications.show({
        title: "Update group failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useDeleteGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (groupId: string) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}`, { method: "DELETE" }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "groups"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Delete group failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useAddGroupMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}/member/${userId}`, { method: "POST" }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId] });
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      void qc.invalidateQueries({ queryKey: ["admin", "database"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Add member failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRemoveGroupMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}/member/${userId}`, { method: "DELETE" }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId] });
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      void qc.invalidateQueries({ queryKey: ["admin", "database"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Remove member failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useAssignGroupPermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, permission }: { groupId: string; permission: string }) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}/permission/${permission}`, { method: "POST" }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId] });
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      void qc.invalidateQueries({ queryKey: ["admin", "database"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Assign permission failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRemoveGroupPermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, permission }: { groupId: string; permission: string }) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}/permission/${permission}`, { method: "DELETE" }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId] });
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      void qc.invalidateQueries({ queryKey: ["admin", "database"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Remove permission failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useAssignGroupDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      groupId,
      databaseId,
      permission,
    }: {
      groupId: string;
      databaseId: string;
      permission: string;
    }) =>
      apiRequest<void, void>(
        `/api/admin/group/${groupId}/database/${databaseId}/role/${permission}`,
        { method: "POST" },
      ),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId] });
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      void qc.invalidateQueries({ queryKey: ["admin", "database"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Assign database role failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRemoveGroupDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      groupId,
      databaseId,
      permission,
    }: {
      groupId: string;
      databaseId: string;
      permission: string;
    }) =>
      apiRequest<void, void>(
        `/api/admin/group/${groupId}/database/${databaseId}/role/${permission}`,
        { method: "DELETE" },
      ),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId] });
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      void qc.invalidateQueries({ queryKey: ["admin", "database"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Remove database role failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

// ── Query history ─────────────────────────────────────────────────────────

export interface QueryHistoryItem {
  id: string;
  databaseId: string | null;
  databaseDisplayName: string | null;
  serverName: string | null;
  queryText: string;
  status: string;
  executedAt: string;
  durationMs: number | null;
  rowCount: number | null;
  error: string | null;
  userId: string | null;
  userName: string | null;
  sensitiveColumns: Array<string>;
  source: string;
}

export interface QueryHistoryFilters {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
  source?: string;
  sensitiveColumn?: Array<string>;
}

export function useQueryHistory(filters: QueryHistoryFilters) {
  return useQuery({
    queryKey: ["query", "history", filters] as const,
    queryFn: () => {
      const params = new URLSearchParams();
      if (filters.from) params.set("from", filters.from);
      if (filters.to) params.set("to", filters.to);
      if (filters.databaseId) params.set("databaseId", filters.databaseId);
      if (filters.status) params.set("status", filters.status);
      if (filters.source) params.set("source", filters.source);
      if (filters.sensitiveColumn) {
        for (const sc of filters.sensitiveColumn) {
          params.append("sensitiveColumn", sc);
        }
      }
      const qs = params.toString();
      return apiRequest<void, { items: Array<QueryHistoryItem> }>(
        qs ? `/api/query/history?${qs}` : "/api/query/history",
      );
    },
    refetchInterval: 10_000,
  });
}

// ── Sensitive columns ─────────────────────────────────────────────────────

export type SensitiveColumnListResponse =
  paths["/api/admin/database/{databaseId}/sensitive-column"]["get"]["responses"][200]["content"]["application/json"];

export function useSensitiveColumns(databaseId: string | null) {
  return useQuery({
    queryKey: ["admin", "database", databaseId, "sensitive-column"] as const,
    enabled: databaseId !== null,
    queryFn: () =>
      apiRequest<void, SensitiveColumnListResponse>(
        `/api/admin/database/${databaseId}/sensitive-column`,
      ),
  });
}

export function useMarkSensitiveColumn() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      schemaName,
      tableName,
      columnName,
    }: {
      databaseId: string;
      schemaName: string;
      tableName: string;
      columnName: string;
    }) =>
      apiRequest<
        paths["/api/admin/database/{databaseId}/sensitive-column"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/database/${databaseId}/sensitive-column`, {
        method: "POST",
        body: { schemaName, tableName, columnName },
      }),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}

export function useUnmarkSensitiveColumn() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      sensitiveColumnId,
    }: {
      databaseId: string;
      sensitiveColumnId: string;
    }) =>
      apiRequest<void, void>(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}`,
        { method: "DELETE" },
      ),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}

export function useGrantColumnBypass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      sensitiveColumnId,
      userId,
    }: {
      databaseId: string;
      sensitiveColumnId: string;
      userId: string;
    }) =>
      apiRequest<
        paths["/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}/bypass`,
        { method: "POST", body: { userId } },
      ),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}

export function useRevokeColumnBypass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      sensitiveColumnId,
      userId,
    }: {
      databaseId: string;
      sensitiveColumnId: string;
      userId: string;
    }) =>
      apiRequest<void, void>(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}/bypass/${userId}`,
        { method: "DELETE" },
      ),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}
