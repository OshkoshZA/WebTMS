<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { clientsApi } from '../api/clients'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { ACTIVE_DEACTIVATED, label, type Client, type Currency } from '../api/types'
import { activeDeactivatedTone, formatMoney } from '../lib/presentation'

const router = useRouter()

const clients = ref<Client[]>([])
const currenciesById = ref<Map<string, Currency>>(new Map())
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredClients = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return clients.value
  return clients.value.filter((c) => c.name.toLowerCase().includes(term) || c.registrationNo.toLowerCase().includes(term))
})

function currencyCode(currencyId: string): string {
  return currenciesById.value.get(currencyId)?.code ?? currencyId
}

onMounted(async () => {
  try {
    const [clientList, currencyList] = await Promise.all([clientsApi.list(), referenceApi.currencies()])
    clients.value = clientList
    currenciesById.value = new Map(currencyList.map((c) => [c.id, c]))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of clients.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Clients</h1>
      <RouterLink
        to="/clients/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New client
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by name or registration no.…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredClients.length === 0" class="mt-6 text-sm text-slate-500">No clients found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Registration no.</th>
            <th class="px-4 py-3">Currency</th>
            <th class="px-4 py-3">Credit limit</th>
            <th class="px-4 py-3">Payment terms</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="client in filteredClients"
            :key="client.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/clients/${client.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ client.name }}</td>
            <td class="px-4 py-3 text-slate-600">{{ client.registrationNo }}</td>
            <td class="px-4 py-3 text-slate-600">{{ currencyCode(client.currencyId) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatMoney(client.creditLimit, currencyCode(client.currencyId)) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ client.paymentTermsDays }} days</td>
            <td class="px-4 py-3"><StatusBadge :text="label(ACTIVE_DEACTIVATED, client.status)" :tone="activeDeactivatedTone(client.status)" /></td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
