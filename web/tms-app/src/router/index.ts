import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/LoginView.vue'), meta: { public: true } },
    { path: '/', redirect: '/loads' },
    { path: '/loads', name: 'loads-list', component: () => import('../views/LoadsListView.vue') },
    { path: '/loads/new', name: 'loads-new', component: () => import('../views/LoadCreateView.vue') },
    { path: '/loads/:id', name: 'loads-detail', component: () => import('../views/LoadDetailView.vue'), props: true },
    { path: '/clients', name: 'clients-list', component: () => import('../views/ClientsListView.vue') },
    { path: '/clients/new', name: 'clients-new', component: () => import('../views/ClientCreateView.vue') },
    { path: '/clients/:id', name: 'clients-detail', component: () => import('../views/ClientDetailView.vue'), props: true },
    { path: '/:pathMatch(.*)*', redirect: '/loads' },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.name === 'login' && auth.isAuthenticated) {
    return { path: '/loads' }
  }
  return true
})
