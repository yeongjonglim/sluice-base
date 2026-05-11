import { useContext } from "react";
import { AuthContext } from "@/auth/AuthContext.tsx";

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an authenticated route");
  }
  return ctx;
}
