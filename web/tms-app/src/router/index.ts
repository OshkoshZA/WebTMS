import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/LoginView.vue'), meta: { public: true } },
    { path: '/', redirect: '/dashboard' },
    { path: '/dashboard', name: 'dashboard', component: () => import('../views/DashboardView.vue') },
    { path: '/loads', name: 'loads-list', component: () => import('../views/LoadsListView.vue') },
    { path: '/loads/new', name: 'loads-new', component: () => import('../views/LoadCreateView.vue') },
    { path: '/loads/:id', name: 'loads-detail', component: () => import('../views/LoadDetailView.vue'), props: true },
    { path: '/clients', name: 'clients-list', component: () => import('../views/ClientsListView.vue') },
    { path: '/clients/new', name: 'clients-new', component: () => import('../views/ClientCreateView.vue') },
    { path: '/clients/:id', name: 'clients-detail', component: () => import('../views/ClientDetailView.vue'), props: true },
    { path: '/vehicles', name: 'vehicles-list', component: () => import('../views/VehiclesListView.vue') },
    { path: '/vehicles/new', name: 'vehicles-new', component: () => import('../views/VehicleCreateView.vue') },
    { path: '/vehicles/:id', name: 'vehicles-detail', component: () => import('../views/VehicleDetailView.vue'), props: true },
    { path: '/drivers', name: 'drivers-list', component: () => import('../views/DriversListView.vue') },
    { path: '/drivers/new', name: 'drivers-new', component: () => import('../views/DriverCreateView.vue') },
    { path: '/drivers/:id', name: 'drivers-detail', component: () => import('../views/DriverDetailView.vue'), props: true },
    { path: '/subcontractors', name: 'subcontractors-list', component: () => import('../views/SubcontractorsListView.vue') },
    { path: '/subcontractors/new', name: 'subcontractors-new', component: () => import('../views/SubcontractorCreateView.vue') },
    { path: '/subcontractors/:id', name: 'subcontractors-detail', component: () => import('../views/SubcontractorDetailView.vue'), props: true },
    { path: '/cost-centres', name: 'cost-centres-list', component: () => import('../views/CostCentresListView.vue') },
    { path: '/cost-centres/new', name: 'cost-centres-new', component: () => import('../views/CostCentreCreateView.vue') },
    { path: '/cost-centres/:id', name: 'cost-centres-detail', component: () => import('../views/CostCentreDetailView.vue'), props: true },
    { path: '/locations', name: 'locations-list', component: () => import('../views/LocationsListView.vue') },
    { path: '/locations/new', name: 'locations-new', component: () => import('../views/LocationCreateView.vue') },
    { path: '/locations/:id', name: 'locations-detail', component: () => import('../views/LocationDetailView.vue'), props: true },
    { path: '/commodities', name: 'commodities-list', component: () => import('../views/CommoditiesListView.vue') },
    { path: '/commodities/new', name: 'commodities-new', component: () => import('../views/CommodityCreateView.vue') },
    { path: '/commodities/:id', name: 'commodities-detail', component: () => import('../views/CommodityDetailView.vue'), props: true },
    { path: '/expense-types', name: 'expense-types-list', component: () => import('../views/ExpenseTypesListView.vue') },
    { path: '/expense-types/new', name: 'expense-types-new', component: () => import('../views/ExpenseTypeCreateView.vue') },
    { path: '/expense-types/:id', name: 'expense-types-detail', component: () => import('../views/ExpenseTypeDetailView.vue'), props: true },
    { path: '/load-types', name: 'load-types-list', component: () => import('../views/LoadTypesListView.vue') },
    { path: '/currencies', name: 'currencies-list', component: () => import('../views/CurrenciesListView.vue') },
    { path: '/financial-calendar', name: 'financial-calendar', component: () => import('../views/FinancialCalendarView.vue') },
    { path: '/exchange-rates', name: 'exchange-rates', component: () => import('../views/ExchangeRatesView.vue') },
    { path: '/users', name: 'users-list', component: () => import('../views/UsersListView.vue') },
    { path: '/users/new', name: 'users-new', component: () => import('../views/UserCreateView.vue') },
    { path: '/users/:id', name: 'users-detail', component: () => import('../views/UserDetailView.vue'), props: true },
    { path: '/roles', name: 'roles-list', component: () => import('../views/RolesListView.vue') },
    { path: '/roles/new', name: 'roles-new', component: () => import('../views/RoleCreateView.vue') },
    { path: '/roles/:id', name: 'roles-detail', component: () => import('../views/RoleDetailView.vue'), props: true },
    { path: '/compliance', name: 'compliance', component: () => import('../views/ComplianceView.vue') },
    { path: '/api-clients', name: 'api-clients-list', component: () => import('../views/ApiClientsListView.vue') },
    { path: '/api-clients/new', name: 'api-clients-new', component: () => import('../views/ApiClientCreateView.vue') },
    { path: '/api-clients/:id', name: 'api-clients-detail', component: () => import('../views/ApiClientDetailView.vue'), props: true },
    { path: '/webhooks', name: 'webhooks-list', component: () => import('../views/WebhookSubscriptionsListView.vue') },
    { path: '/webhooks/new', name: 'webhooks-new', component: () => import('../views/WebhookSubscriptionCreateView.vue') },
    { path: '/webhooks/:id', name: 'webhooks-detail', component: () => import('../views/WebhookSubscriptionDetailView.vue'), props: true },
    { path: '/retention-policies', name: 'retention-policies', component: () => import('../views/RetentionPoliciesView.vue') },
    { path: '/data-subject-requests', name: 'dsr-list', component: () => import('../views/DataSubjectRequestsListView.vue') },
    { path: '/data-subject-requests/new', name: 'dsr-new', component: () => import('../views/DataSubjectRequestCreateView.vue') },
    { path: '/data-subject-requests/:id', name: 'dsr-detail', component: () => import('../views/DataSubjectRequestDetailView.vue'), props: true },
    { path: '/audit-trail', name: 'audit-trail', component: () => import('../views/AuditTrailView.vue') },
    { path: '/company-settings', name: 'company-settings', component: () => import('../views/CompanySettingsView.vue') },
    { path: '/debriefs', name: 'debriefs-list', component: () => import('../views/DebriefsListView.vue') },
    { path: '/debriefs/:id', name: 'debriefs-detail', component: () => import('../views/DebriefDetailView.vue'), props: true },
    { path: '/invoices', name: 'invoices-list', component: () => import('../views/InvoicesListView.vue') },
    { path: '/invoices/:id', name: 'invoices-detail', component: () => import('../views/InvoiceDetailView.vue'), props: true },
    { path: '/credit-notes', name: 'credit-notes-list', component: () => import('../views/CreditNotesListView.vue') },
    { path: '/credit-notes/new', name: 'credit-notes-new', component: () => import('../views/CreditNoteCreateView.vue') },
    { path: '/credit-notes/:id', name: 'credit-notes-detail', component: () => import('../views/CreditNoteDetailView.vue'), props: true },
    { path: '/exceptions', name: 'exceptions-list', component: () => import('../views/ExceptionsListView.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/dashboard' },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.name === 'login' && auth.isAuthenticated) {
    return { path: '/dashboard' }
  }
  return true
})
