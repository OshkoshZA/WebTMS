<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { loadsApi } from '../api/loads'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { LOAD_STATUS, label, type Client, type Load } from '../api/types'
import { loadStatusTone, formatDateTime } from '../lib/presentation'

const loads = ref<Load[]>([])
const clientsById = ref<Map<string, Client>>(new Map())
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredLoads = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return loads.value
  return loads.value.filter((load) => {
    const clientName = clientsById.value.get(load.clientId)?.name ?? ''
    return load.referenceNo.toLowerCase().includes(term) || clientName.toLowerCase().includes(term)
  })
})

function clientName(clientId: string): string {
  return clientsById.value.get(clientId)?.name ?? clientId
}

onMounted(async () => {
  try {
    const [loadList, clientList] = await Promise.all([loadsApi.list(), referenceApi.clients()])
    loads.value = loadList
    clientsById.value = new Map(clientList.map((c) => [c.id, c]))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of loads.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Loads</h1>
      <RouterLink
        to="/loads/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New load
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by reference or client…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredLoads.length === 0" class="mt-6 text-sm text-slate-500">No loads found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Reference</th>
            <th class="px-4 py-3">Client</th>
            <th class="px-4 py-3">Status</th>
            <th class="px-4 py-3">Pickup</th>
            <th class="px-4 py-3">Delivery</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="load in filteredLoads"
            :key="load.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="$router.push(`/loads/${load.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ load.referenceNo }}</td>
            <td class="px-4 py-3 text-slate-600">{{ clientName(load.clientId) }}</td>
            <td class="px-4 py-3"><StatusBadge :text="label(LOAD_STATUS, load.status)" :tone="loadStatusTone(load.status)" /></td>
            <td class="px-4 py-3 text-slate-600">{{ formatDateTime(load.pickupWindowStart) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDateTime(load.deliveryWindowStart) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
