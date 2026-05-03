import type { ReactNode } from "react";
import type { MeResponse } from "@/api/hooks.ts";
import { AuthContext as AuthContext1 } from "@/auth/AuthContext.tsx";

export function AuthProvider({ user, children }: { user: MeResponse; children: ReactNode }) {
  return <AuthContext1 value={{ user }}>{children}</AuthContext1>;
}
