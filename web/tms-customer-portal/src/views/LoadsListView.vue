<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import CreditStatusWidget from '../components/CreditStatusWidget.vue'
import { loadsApi } from '../api/loads'
import { ApiError } from '../api/client'
import { LOAD_STATUS, label, type Load } from '../api/types'
import { loadStatusTone, formatDateTime } from '../lib/presentation'

const router = useRouter()

const loads = ref<Load[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredLoads = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return loads.value
  return loads.value.filter((load) => load.referenceNo.toLowerCase().includes(term))
})

onMounted(async () => {
  try {
    loads.value = await loadsApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load your loads.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <CreditStatusWidget />

    <div class="mt-8 flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Your loads</h1>
      <RouterLink
        to="/loads/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        Book a load
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by reference…"
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
            @click="router.push(`/loads/${load.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ load.referenceNo }}</td>
            <td class="px-4 py-3"><StatusBadge :text="label(LOAD_STATUS, load.status)" :tone="loadStatusTone(load.status)" /></td>
            <td class="px-4 py-3 text-slate-600">{{ formatDateTime(load.pickupWindowStart) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDateTime(load.deliveryWindowStart) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
