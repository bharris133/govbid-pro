// Entry module. Each Razor page declares data-page on <body>; only that page's module is loaded.

type PageInitializer = () => Promise<void>;

const pages: Record<string, () => Promise<PageInitializer>> = {
  dashboard: async () => (await import('./pages/dashboardPage.js')).initDashboardPage,
  'opportunity-detail': async () => (await import('./pages/opportunityDetailPage.js')).initOpportunityDetailPage,
  proposals: async () => (await import('./pages/proposalsPage.js')).initProposalsPage,
  'proposal-detail': async () => (await import('./pages/proposalDetailPage.js')).initProposalDetailPage,
  company: async () => (await import('./pages/companyPage.js')).initCompanyPage,
  opportunities: async () => (await import('./pages/opportunitiesPage.js')).initOpportunitiesPage,
  documents: async () => (await import('./pages/documentsPage.js')).initDocumentsPage,
  tasks: async () => (await import('./pages/tasksPage.js')).initTasksPage,
  notifications: async () => (await import('./pages/notificationsPage.js')).initNotificationsPage,
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
  if (document.querySelector('#unread-badge') && page !== 'notifications') {
    void import('./components/unreadBadge.js').then((m) => m.refreshUnreadBadge());
  }
}

boot().catch((error: unknown) => {
  document.documentElement.dataset['clientReady'] = 'error';
  console.error('GovBid Pro client failed to start', error);
});
