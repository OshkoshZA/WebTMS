<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { driversApi } from '../api/drivers'
import { ApiError } from '../api/client'
import { DRIVER_STATUS, label, type Driver } from '../api/types'
import { driverStatusTone } from '../lib/presentation'

const router = useRouter()

const drivers = ref<Driver[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredDrivers = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return drivers.value
  return drivers.value.filter(
    (d) => d.name.toLowerCase().includes(term) || d.employeeNo.toLowerCase().includes(term),
  )
})

onMounted(async () => {
  try {
    drivers.value = await driversApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of drivers.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Drivers</h1>
      <RouterLink
        to="/drivers/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New driver
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by name or employee no.…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredDrivers.length === 0" class="mt-6 text-sm text-slate-500">No drivers found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Employee no.</th>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Licence code</th>
            <th class="px-4 py-3">Licence expiry</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="driver in filteredDrivers"
            :key="driver.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/drivers/${driver.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ driver.employeeNo }}</td>
            <td class="px-4 py-3 text-slate-600">{{ driver.name }}</td>
            <td class="px-4 py-3 text-slate-600">{{ driver.licenceCode }}</td>
            <td class="px-4 py-3 text-slate-600">{{ driver.licenceExpiry ?? '—' }}</td>
            <td class="px-4 py-3"><StatusBadge :text="label(DRIVER_STATUS, driver.status)" :tone="driverStatusTone(driver.status)" /></td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
