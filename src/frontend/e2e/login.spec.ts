import { expect, test } from "@playwright/test";

test.describe("BFF login flow", () => {
  test("alice can log in, see the shell, and log out", async ({ page }) => {
    // 1. Land on the SPA. Auth bootstrap should redirect us to Keycloak.
    await page.goto("/");

    await expect(page).toHaveURL(/\/realms\/sluicebase\/login-actions\/authenticate/, {
      timeout: 15_000,
    });

    // 2. Sign in as alice.
    await page.getByLabel(/username/i).fill("alice");
    await page.locator('[id="password"]').fill("dev");
    await page.getByRole("button", { name: /sign in/i }).click();

    // 3. Land back on the SPA's authed shell.
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, {
      timeout: 15_000,
    });
    await expect(page.getByRole("heading", { level: 2 })).toContainText(/Welcome,/i);
    await expect(page.getByRole("heading", { level: 2 })).toContainText(/alice/i);

    // 4. Open the user menu and log out.
    await page.getByTestId("user-menu").click();
    await page.getByRole("menuitem", { name: /log out/i }).click();

    // 5. After logout, landing on the SPA should redirect us back to Keycloak login.
    await page.goto("/");
    await expect(page).toHaveURL(/\/realms\/sluicebase\/login-actions\/authenticate/, {
      timeout: 15_000,
    });
  });
});
