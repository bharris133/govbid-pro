import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

// Automated accessibility checks (P1.7) with axe-core against WCAG 2.0/2.1/2.2 A and AA rules.
// Automated rules catch only part of WCAG; this is a regression guard, not a conformance claim.

const password = 'A11y-Test-Passw0rd!';
const tags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function expectNoViolations(page: Page, name: string): Promise<void> {
  await expect(page.locator('html')).toHaveAttribute('data-client-ready', 'true');
  const results = await new AxeBuilder({ page }).withTags(tags).analyze();
  const summary = results.violations.map((v) => `${v.id} (${v.impact ?? 'n/a'}): ${v.help} -> ${v.nodes.map((n) => n.target.join(' ')).slice(0, 5).join(' | ')}`);
  expect(summary, `Accessibility violations on ${name}`).toEqual([]);
}

test('main pages have no automatically detectable WCAG A/AA violations', async ({ page }) => {
  const email = `a11y-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`;

  await page.goto('/');
  await expectNoViolations(page, 'home');
  await page.goto('/DataPolicy');
  await expectNoViolations(page, 'data policy');

  await page.goto('/Identity/Account/Register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByLabel('Confirm Password').fill(password);
  await page.getByRole('button', { name: 'Register' }).click();
  await page.goto('/Onboarding/CreateCompany');
  await expectNoViolations(page, 'create company');
  await page.getByLabel('Company name').fill('Accessible Contractors LLC');
  await page.getByRole('button', { name: 'Create company' }).click();
  await expect(page).toHaveURL(/\/Opportunities/);
  await expect(page.locator('#results tbody tr').first()).toBeVisible();
  await expectNoViolations(page, 'opportunities');

  await page.locator('#results tbody tr a').first().click();
  await expect(page.locator('#fit-score')).toContainText('How the score was calculated');
  await expectNoViolations(page, 'opportunity detail');

  await page.getByRole('button', { name: 'Start a response' }).click();
  await expect(page).toHaveURL(/Proposals\/Detail/);
  await expect(page.locator('#sections-table')).toBeVisible();
  await page.getByLabel('Reference (e.g. L.4.2)').fill('L.1');
  await page.getByLabel('Requirement text').fill('The offeror shall describe its approach.');
  await page.getByRole('button', { name: 'Add requirement' }).click();
  await expect(page.locator('#compliance-matrix')).toContainText('L.1');
  await expect(page.locator('#comment-list')).toContainText('No comments yet.');
  await expectNoViolations(page, 'proposal detail');

  for (const [path, ready] of [
    ['/Dashboard', '#discovery-stats'],
    ['/Proposals', '#proposals table'],
    ['/Tasks', '#tasks'],
    ['/Documents', '#documents'],
    ['/Company', '#members table'],
    ['/Notifications', '#notifications'],
  ] as const) {
    await page.goto(path);
    await expect(page.locator(ready).first()).toBeVisible();
    await expectNoViolations(page, path);
  }
});
