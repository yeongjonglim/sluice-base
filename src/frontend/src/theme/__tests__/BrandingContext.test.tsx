import { cleanup, render, renderHook, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { BrandingContext, useBranding } from '@/theme/BrandingContext';

afterEach(cleanup);

function TestConsumer() {
  const branding = useBranding();
  return <div data-testid="result">{JSON.stringify(branding)}</div>;
}

describe("BrandingContext", () => {
  it("returns defaults when no provider wraps the consumer", () => {
    render(<TestConsumer />);

    const result = JSON.parse(screen.getByTestId("result").textContent);
    expect(result.appName).toBe("SluiceBase");
    expect(result.logoUrl).toBeNull();
    expect(result.faviconUrl).toBeNull();
  });

  it("provides custom branding values", () => {
    const value = {
      appName: "Acme",
      logoUrl: "https://example.com/logo.png",
      faviconUrl: null,
      mcpEnabled: true,
      mcpServerName: "acme-db",
    };

    render(
      <BrandingContext value={value}>
        <TestConsumer />
      </BrandingContext>,
    );

    const result = JSON.parse(screen.getByTestId("result").textContent);
    expect(result.appName).toBe("Acme");
    expect(result.logoUrl).toBe("https://example.com/logo.png");
    expect(result.faviconUrl).toBeNull();
    expect(result.mcpEnabled).toBe(true);
    expect(result.mcpServerName).toBe("acme-db");
  });

  it("defaults mcpEnabled to false and mcpServerName to sluicebase", () => {
    const { result } = renderHook(() => useBranding());

    expect(result.current.mcpEnabled).toBe(false);
    expect(result.current.mcpServerName).toBe("sluicebase");
  });
});
