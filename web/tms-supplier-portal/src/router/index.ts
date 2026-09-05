import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/LoginView.vue'), meta: { public: true } },
    { path: '/', redirect: '/dashboard' },
    { path: '/dashboard', name: 'dashboard', component: () => import('../views/DashboardView.vue') },
    { path: '/legs', name: 'legs-list', component: () => import('../views/LegsListView.vue') },
    { path: '/legs/:id', name: 'legs-detail', component: () => import('../views/LegDetailView.vue'), props: true },
    { path: '/accruals', name: 'accruals-list', component: () => import('../views/AccrualsListView.vue') },
    {
      path: '/supplier-invoices',
      name: 'supplier-invoices-list',
      component: () => import('../views/SupplierInvoicesListView.vue'),
    },
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
