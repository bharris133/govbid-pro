// Entry module. Each Razor page declares data-page on <body>; only that page's module is loaded.

type PageInitializer = () => Promise<void>;

const pages: Record<string, () => Promise<PageInitializer>> = {
  opportunities: async () => (await import('./pages/opportunitiesPage.js')).initOpportunitiesPage,
  documents: async () => (await import('./pages/documentsPage.js')).initDocumentsPage,
};

async function boot(): Promise<void> {
  document.documentElement.dataset['clientReady'] = 'loading';
  const page = document.body.dataset['page'] ?? 'static';
  const loader = pages[page];
  if (loader) {
    const init = await loader();
    await init();
  }
  document.documentElement.dataset['clientReady'] = 'true';
}

boot().catch((error: unknown) => {
  document.documentElement.dataset['clientReady'] = 'error';
  console.error('GovBid Pro client failed to start', error);
});
