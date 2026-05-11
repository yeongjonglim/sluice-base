import { createContext } from "react";
import type { MeResponse } from "@/api/hooks.ts";

interface AuthContextValue {
  user: MeResponse;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
