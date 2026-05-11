import { cleanup, render, screen } from "@testing-library/react";
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
    const value = { appName: "Acme", logoUrl: "https://example.com/logo.png", faviconUrl: null };

    render(
      <BrandingContext value={value}>
        <TestConsumer />
      </BrandingContext>,
    );

    const result = JSON.parse(screen.getByTestId("result").textContent);
    expect(result.appName).toBe("Acme");
    expect(result.logoUrl).toBe("https://example.com/logo.png");
    expect(result.faviconUrl).toBeNull();
  });
});
