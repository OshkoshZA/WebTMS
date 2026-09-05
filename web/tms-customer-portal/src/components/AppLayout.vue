<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()

async function logout() {
  await auth.logout()
  await router.push({ name: 'login' })
}
</script>

<template>
  <div class="min-h-screen bg-slate-50">
    <header class="border-b border-slate-200 bg-white">
      <div class="mx-auto flex max-w-6xl items-center justify-between px-6 py-3">
        <div class="flex items-center gap-6">
          <span class="text-lg font-semibold text-slate-900">TMS Customer Portal</span>
          <nav class="flex gap-4 text-sm">
            <router-link to="/loads" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Loads
            </router-link>
            <router-link to="/invoices" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Invoices
            </router-link>
            <router-link to="/credit-notes" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Credit notes
            </router-link>
          </nav>
        </div>
        <div class="flex items-center gap-4 text-sm text-slate-600">
          <span>{{ auth.email }}</span>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1 hover:bg-slate-100" @click="logout">
            Log out
          </button>
        </div>
      </div>
    </header>
    <main class="mx-auto max-w-6xl px-6 py-8">
      <slot />
    </main>
  </div>
</template>
