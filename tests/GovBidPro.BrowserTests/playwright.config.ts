import { defineConfig, devices } from '@playwright/test';

// Runs against an already-running GovBid Pro instance (CI starts it with fixture data).
const baseURL = process.env['GOVBID_BASE_URL'] ?? 'http://localhost:5080';

export default defineConfig({
  testDir: '.',
  testMatch: '**/*.spec.ts',
  timeout: 60_000,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never', outputFolder: '../../playwright-report' }]] : 'list',
  outputDir: '../../test-results',
  use: {
    baseURL,
    trace: 'off',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
