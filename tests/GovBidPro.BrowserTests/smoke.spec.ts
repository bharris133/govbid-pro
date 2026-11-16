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

  // P1.1: dashboard summarizes discovery and links through to a notice's provenance.
  await page.goto('/Dashboard');
  await expect(page.locator('html')).toHaveAttribute('data-client-ready', 'true');
  await expect(page.locator('#source-status')).toContainText('DEMO FIXTURE DATA');
  await expect(page.locator('#discovery-stats')).toContainText('Open for response');
  await expect(page.locator('#saved-search-summary')).toContainText('Sources sought watch');
  await page.locator('#saved-search-summary').getByRole('link', { name: 'Sources sought watch' }).click();
  await expect(page).toHaveURL(/savedSearch=/);
  await expect(page.locator('#results tbody tr')).toHaveCount(2);
  await page.locator('#results tbody tr a').first().click();
  await expect(page).toHaveURL(/Opportunities\/Detail/);
  await expect(page.locator('#opportunity-detail')).toContainText('Provenance');
  await expect(page.locator('#opportunity-detail')).toContainText('Demo fixture');
  await expect(page.locator('#opportunity-detail')).toContainText('Revision 1');

  // P1.3: the fit score is explained and a Go decision is recorded with a rationale.
  const detailUrl = page.url();
  await expect(page.locator('#fit-score')).toContainText('How the score was calculated');
  await page.goto('/Company');
  await expect(page.locator('html')).toHaveAttribute('data-client-ready', 'true');
  await page.getByLabel('NAICS codes (comma separated)').fill('561210, 541511');
  await page.getByLabel('Capability keywords (comma separated)').fill('facilities, maintenance');
  await page.getByLabel('Small business', { exact: true }).check();
  await page.getByRole('button', { name: 'Save company profile' }).click();
  await expect(page.locator('#profile-status')).toContainText('Company profile saved.');
  await page.goto(detailUrl);
  await expect(page.locator('#fit-score')).toContainText('/ 100');
  await page.getByLabel('Rationale (required for Go and No-go)').fill('Matches our facilities experience.');
  await page.getByRole('button', { name: 'Record decision' }).click();
  await expect(page.locator('#assessment-status')).toContainText('Decision recorded.');
  await expect(page.locator('#assessment-history')).toContainText('Go · score');
  await expect(page.locator('#assessment-history')).toContainText('Matches our facilities experience.');

  // P1.4: start a response, add a compliance requirement and move the proposal to work.
  await page.getByRole('button', { name: 'Start a response' }).click();
  await expect(page).toHaveURL(/Proposals\/Detail/);
  await expect(page.locator('html')).toHaveAttribute('data-client-ready', 'true');
  await expect(page.locator('#proposal-status')).toHaveText('');
  await expect(page.locator('#sections-table')).toBeVisible();
  await page.getByLabel('Reference (e.g. L.4.2)').fill('L.4.2');
  await page.getByLabel('Requirement text').fill('The offeror shall describe its staffing plan.');
  await page.getByRole('button', { name: 'Add requirement' }).click();
  await expect(page.locator('#compliance-matrix')).toContainText('L.4.2');
  await expect(page.locator('#compliance-summary')).toContainText('1');
  await expect(page.locator('#proposal-exports')).toContainText('Compliance matrix (CSV)');
  await expect(page.locator('#proposal-files')).toContainText('No files are linked');
  await page.locator('#status-actions').getByRole('button', { name: 'Move to In progress' }).click();
  await expect(page.locator('#proposal-history')).toContainText('In progress');
  await page.goto('/Proposals');
  await expect(page.locator('html')).toHaveAttribute('data-client-ready', 'true');
  await expect(page.locator('#proposals')).toContainText('In progress');

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

  // P1.5: a clean upload linked to the proposal gets version 1; a second version becomes current.
  const fileInput = page.getByLabel('File (PDF, DOCX, XLSX, TXT, CSV, PNG or JPG)');
  await fileInput.setInputFiles({ name: 'approach.txt', mimeType: 'text/plain', buffer: Buffer.from('Technical approach draft one.\n') });
  await page.getByLabel('Link to a proposal (optional)').selectOption({ index: 1 });
  await page.getByLabel('I am permitted to upload this file to GovBid Pro.').first().check();
  await page.getByLabel('This file contains no classified information, CUI or FCI that must be safeguarded.').first().check();
  await page.getByRole('button', { name: 'Upload', exact: true }).click();
  await expect(page.locator('#upload-status')).toContainText('Upload complete.');
  await expect(page.locator('#documents')).toContainText('approach.txt');
  await expect(page.locator('#documents')).toContainText('Proposal:');
  await page.getByRole('button', { name: 'Versions of approach.txt' }).click();
  await expect(page.locator('#documents')).toContainText('Version 1 (current)');
  await page.getByLabel('New version of approach.txt').setInputFiles({ name: 'approach.txt', mimeType: 'text/plain', buffer: Buffer.from('Technical approach draft two.\n') });
  const versionForm = page.locator('#documents form');
  await versionForm.getByLabel('I am permitted to upload this file to GovBid Pro.').check();
  await versionForm.getByLabel('This file contains no classified information, CUI or FCI that must be safeguarded.').check();
  await versionForm.getByRole('button', { name: 'Upload new version' }).click();
  await expect(page.locator('#upload-status')).toContainText('Version 2 of approach.txt uploaded.');
  await expect(page.locator('#documents')).toContainText('Version 2 (current)');
});

test('unauthenticated API access is rejected', async ({ request }) => {
  const response = await request.get('/api/v1/opportunities');
  expect(response.status()).toBe(401);
});
