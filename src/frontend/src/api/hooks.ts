import {
  QueryCache,
  QueryClient,
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { notifications } from "@mantine/notifications";
import type { paths } from "./schema.ts";
import { ApiError, apiRequest } from "@/api/client";

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

// ── Server registry ───────────────────────────────────────────────────────

export type ServerListResponse =
  paths["/api/server"]["get"]["responses"][200]["content"]["application/json"];
export type ServerItem = ServerListResponse["servers"][0];
export type TestConnectionResponse =
  paths["/api/server/{id}/test"]["post"]["responses"][200]["content"]["application/json"];
export type CreateServerRequest =
  paths["/api/server"]["post"]["requestBody"]["content"]["application/json"];

export function useServers() {
  return useQuery({
    queryKey: ["server"] as const,
    queryFn: () => apiRequest<void, ServerListResponse>("/api/server"),
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

export function useTestConnection() {
  return useMutation({
    mutationFn: (id: string) =>
      apiRequest<void, TestConnectionResponse>(`/api/server/${id}/test`, { method: "POST" }),
  });
}

export type SchemaResponse =
  paths["/api/schema/{serverId}"]["get"]["responses"][200]["content"]["application/json"];

export function useSchema(serverId: string | null) {
  return useQuery({
    queryKey: ["schema", serverId] as const,
    queryFn: () => apiRequest<void, SchemaResponse>(`/api/schema/${serverId}`),
    enabled: serverId !== null,
    staleTime: 5 * 60 * 1000,
  });
}

export type ExecuteQueryRequest =
  paths["/api/query"]["post"]["requestBody"]["content"]["application/json"];
export type ExecuteQueryResponse =
  paths["/api/query"]["post"]["responses"][200]["content"]["application/json"];

export function useExecuteQuery() {
  return useMutation({
    mutationFn: (body: ExecuteQueryRequest) =>
      apiRequest<ExecuteQueryRequest, ExecuteQueryResponse>("/api/query", {
        method: "POST",
        body,
      }),
  });
}

// ── Update requests ───────────────────────────────────────────────────────

export type UpdateSummaryItem =
  paths["/api/update"]["get"]["responses"][200]["content"]["application/json"]["requests"][0];
export type UpdateRequestListResponse =
  paths["/api/update"]["get"]["responses"][200]["content"]["application/json"];
export type UpdateRequestDetail =
  paths["/api/update/{id}"]["get"]["responses"][200]["content"]["application/json"];
export type SubmitUpdateRequest =
  paths["/api/update"]["post"]["requestBody"]["content"]["application/json"];

export function useUpdateRequests() {
  return useQuery({
    queryKey: ["update"] as const,
    queryFn: () => apiRequest<void, UpdateRequestListResponse>("/api/update"),
  });
}

export function useUpdateRequest(id: string) {
  return useQuery({
    queryKey: ["update", id] as const,
    queryFn: () => apiRequest<void, UpdateRequestDetail>(`/api/update/${id}`),
  });
}

export function useSubmitUpdate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SubmitUpdateRequest) =>
      apiRequest<SubmitUpdateRequest, UpdateRequestDetail>("/api/update", {
        method: "POST",
        body,
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["update"] });
      notifications.show({ title: "Request submitted", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Submit failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
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
    mutationFn: (id: string) =>
      apiRequest<void, UpdateRequestDetail>(`/api/update/${id}/cancel`, { method: "POST" }),
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
