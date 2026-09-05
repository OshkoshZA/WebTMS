<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { loadsApi } from '../api/loads'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { LOAD_LEG_STATUS, LOAD_STATUS, label, type Load, type LoadType, type LoadTracking } from '../api/types'
import { formatDateTime, loadStatusTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()

const load = ref<Load | null>(null)
const tracking = ref<LoadTracking | null>(null)
const loadTypes = ref<LoadType[]>([])
const loading = ref(true)
const error = ref('')

const loadTypeLabel = computed(() => {
  const t = loadTypes.value.find((lt) => lt.id === load.value?.loadTypeId)
  return t ? `${t.code} — ${t.description}` : '—'
})

onMounted(async () => {
  try {
    const [loadData, trackingData, loadTypeList] = await Promise.all([
      loadsApi.get(props.id),
      loadsApi.tracking(props.id),
      referenceApi.loadTypes(),
    ])
    load.value = loadData
    tracking.value = trackingData
    loadTypes.value = loadTypeList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this load.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="load">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ load.referenceNo }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ loadTypeLabel }}</p>
        </div>
        <StatusBadge :text="label(LOAD_STATUS, load.status)" :tone="loadStatusTone(load.status)" />
      </div>

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Pickup window</dt>
          <dd class="text-slate-900">{{ formatDateTime(load.pickupWindowStart) }} – {{ formatDateTime(load.pickupWindowEnd) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Delivery window</dt>
          <dd class="text-slate-900">{{ formatDateTime(load.deliveryWindowStart) }} – {{ formatDateTime(load.deliveryWindowEnd) }}</dd>
        </div>
      </dl>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Legs</h2>
      <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">#</th>
              <th class="px-4 py-3">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="leg in tracking?.legs.slice().sort((a, b) => a.sequenceNo - b.sequenceNo)"
              :key="leg.id"
              class="border-b border-slate-100 last:border-0"
            >
              <td class="px-4 py-3 text-slate-600">{{ leg.sequenceNo }}</td>
              <td class="px-4 py-3"><StatusBadge :text="label(LOAD_LEG_STATUS, leg.status)" tone="info" /></td>
            </tr>
            <tr v-if="!tracking?.legs.length">
              <td colspan="2" class="px-4 py-6 text-center text-slate-500">No legs yet.</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Status history</h2>
      <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">From</th>
              <th class="px-4 py-3">To</th>
              <th class="px-4 py-3">Changed</th>
              <th class="px-4 py-3">Reason</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(entry, i) in tracking?.history" :key="i" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-600">{{ label(LOAD_STATUS, entry.fromStatus) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ label(LOAD_STATUS, entry.toStatus) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDateTime(entry.changedAt) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ entry.reason ?? '—' }}</td>
            </tr>
            <tr v-if="!tracking?.history.length">
              <td colspan="4" class="px-4 py-6 text-center text-slate-500">No status changes yet.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
