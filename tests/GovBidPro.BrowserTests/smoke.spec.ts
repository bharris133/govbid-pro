import { expect, test } from '@playwright/test';

// Critical journey smoke test (handoff §10): sign up, create company, see labeled fixture data,
// filter, save a search, and confirm uploads with security markings are rejected.

const password = 'Smoke-Test-Passw0rd!';

test('company user can search fixture opportunities and save a search', async ({ page }) => {
  const email = `smoke-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`;

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'GovBid Pro', exact: true })).toBeVisible();
  await expect(page.getByText('demo fixture data', { exact: false })).toBeVisible();

  await page.goto('/Identity/Account/Register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByLabel('Confirm Password').fill(password);
  await page.getByRole('button', { name: 'Register' }).click();

  await page.goto('/Opportunities');
  await expect(page).toHaveURL(/Onboarding\/CreateCompany/);
  await page.getByLabel('Company name').fill('Smoke Test Contractors LLC');
  await page.getByRole('button', { name: 'Create company' }).click();

  await expect(page).toHaveURL(/\/Opportunities/);
  await expect(page.locator('html')).toHaveAttribute('data-client-ready', 'true');
  await expect(page.locator('#source-status')).toContainText('DEMO FIXTURE DATA');
  await expect(page.locator('#results-summary')).toContainText('matching');

  await page.getByLabel('Response type').selectOption('SourcesSought');
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(page.locator('#results tbody tr')).toHaveCount(2);
  await expect(page.locator('#results')).toContainText('Demo fixture');

  await page.getByLabel('Name', { exact: true }).fill('Sources sought watch');
  await page.getByRole('button', { name: 'Save search' }).click();
  await expect(page.locator('#saved-searches')).toContainText('Sources sought watch');

  await page.goto('/Documents');
  await expect(page.locator('html')).toHaveAttribute('data-client-ready', 'true');
  await page.getByLabel('File (PDF, DOCX, XLSX, TXT, CSV, PNG or JPG)').setInputFiles({
    name: 'marked.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('CUI//SP-PRVCY\nTest marking only.\n'),
  });
  await page.getByLabel('I am permitted to upload this file to GovBid Pro.').check();
  await page.getByLabel('This file contains no classified information, CUI or FCI that must be safeguarded.').check();
  await page.getByRole('button', { name: 'Upload' }).click();
  await expect(page.locator('#upload-status')).toContainText('was rejected');
  await expect(page.locator('#documents')).toContainText('No documents uploaded yet.');
});

test('unauthenticated API access is rejected', async ({ request }) => {
  const response = await request.get('/api/v1/opportunities');
  expect(response.status()).toBe(401);
});
