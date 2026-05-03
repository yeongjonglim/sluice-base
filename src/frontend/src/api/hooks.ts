import { QueryCache, QueryClient, useQuery } from "@tanstack/react-query";
import { ApiError, apiRequest } from "./client";

export interface MeResponse {
  sub: string | null;
  email: string | null;
  name: string | null;
  preferredUsername: string | null;
  roles: Array<string>;
}

export interface AuthedHealthResponse {
  status: string;
  user: string | null;
}

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

export const meQueryOptions = {
  queryKey: ["me"] as const,
  queryFn: () => apiRequest<MeResponse>("/api/me"),
};

export function useMe() {
  return useQuery(meQueryOptions);
}

export function useAuthedHealth() {
  return useQuery({
    queryKey: ["health-authed"] as const,
    queryFn: () => apiRequest<AuthedHealthResponse>("/api/health/authed"),
  });
}
