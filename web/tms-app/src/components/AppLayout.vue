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
          <span class="text-lg font-semibold text-slate-900">TMS</span>
          <nav class="flex flex-wrap gap-4 text-sm">
            <router-link to="/dashboard" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Dashboard
            </router-link>
            <router-link to="/loads" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Loads
            </router-link>
            <router-link to="/clients" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Clients
            </router-link>
            <router-link to="/vehicles" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Vehicles
            </router-link>
            <router-link to="/drivers" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Drivers
            </router-link>
            <router-link to="/subcontractors" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Subcontractors
            </router-link>
            <router-link to="/cost-centres" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Cost centres
            </router-link>
            <router-link to="/locations" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Locations
            </router-link>
            <router-link to="/commodities" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Commodities
            </router-link>
            <router-link to="/expense-types" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Expense types
            </router-link>
            <router-link to="/load-types" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Load types
            </router-link>
            <router-link to="/currencies" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Currencies
            </router-link>
            <router-link to="/financial-calendar" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Financial calendar
            </router-link>
            <router-link to="/exchange-rates" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Exchange rates
            </router-link>
            <router-link to="/exceptions" class="text-slate-600 hover:text-slate-900" active-class="font-semibold text-slate-900">
              Exceptions
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
