import type { ReactNode } from "react";
import type { MeResponse } from "@/api/hooks.ts";
import { AuthContext } from "@/auth/AuthContext.tsx";

export function AuthProvider({ user, children }: { user: MeResponse; children: ReactNode }) {
  return <AuthContext value={{ user }}>{children}</AuthContext>;
}
