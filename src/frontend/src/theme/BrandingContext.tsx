import { createContext, useContext } from "react";

export interface BrandingValue {
  appName: string;
  logoUrl: string | null;
  faviconUrl: string | null;
}

const DEFAULT_BRANDING: BrandingValue = {
  appName: "SluiceBase",
  logoUrl: null,
  faviconUrl: null,
};

export const BrandingContext = createContext<BrandingValue>(DEFAULT_BRANDING);

export function useBranding(): BrandingValue {
  return useContext(BrandingContext);
}
