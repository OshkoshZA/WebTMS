<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { debriefsApi } from '../api/debriefs'
import { driversApi } from '../api/drivers'
import { vehiclesApi } from '../api/vehicles'
import { ApiError } from '../api/client'
import { DEBRIEF_STATUS, label, type Debrief, type Driver, type Vehicle } from '../api/types'
import { debriefStatusTone, formatDateTime } from '../lib/presentation'

const router = useRouter()

const debriefs = ref<Debrief[]>([])
const drivers = ref<Driver[]>([])
const vehicles = ref<Vehicle[]>([])
const loading = ref(true)
const error = ref('')
// The Debrief Clerk's own working queue is PendingReview — default to it rather than
// "all", since that's the actual job this screen exists for (§09).
const statusFilter = ref<number | ''>(0)

function driverName(id: string | null): string {
  if (!id) return '—'
  return drivers.value.find((d) => d.id === id)?.name ?? id.slice(0, 8)
}

function vehicleLabel(id: string | null): string {
  if (!id) return '—'
  return vehicles.value.find((v) => v.id === id)?.fleetNo ?? id.slice(0, 8)
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [debriefList, driverList, vehicleList] = await Promise.all([
      debriefsApi.list(statusFilter.value === '' ? undefined : statusFilter.value),
      drivers.value.length === 0 ? driversApi.list() : Promise.resolve(drivers.value),
      vehicles.value.length === 0 ? vehiclesApi.list() : Promise.resolve(vehicles.value),
    ])
    debriefs.value = debriefList
    drivers.value = driverList
    vehicles.value = vehicleList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load debriefs.'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Debriefs</h1>
    <p class="mt-1 text-sm text-slate-500">Post-trip reconciliation queue (§09) — the gate a leg clears before it can be billed or paid.</p>

    <div class="mt-6">
      <select v-model="statusFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="load">
        <option value="">All statuses</option>
        <option v-for="(s, i) in DEBRIEF_STATUS" :key="s" :value="i">{{ s }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="debriefs.length === 0" class="mt-6 text-sm text-slate-500">No debriefs found for this filter.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Leg</th>
            <th class="px-4 py-3">Driver</th>
            <th class="px-4 py-3">Vehicle</th>
            <th class="px-4 py-3">Submitted</th>
            <th class="px-4 py-3">Exception reasons</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="debrief in debriefs"
            :key="debrief.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/debriefs/${debrief.id}`)"
          >
            <td class="px-4 py-3 font-mono text-xs text-slate-600">{{ debrief.loadLegId.slice(0, 8) }}</td>
            <td class="px-4 py-3 text-slate-900">{{ driverName(debrief.driverId) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ vehicleLabel(debrief.vehicleId) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDateTime(debrief.submittedAt) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ debrief.exceptionReasons ?? '—' }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(DEBRIEF_STATUS, debrief.status)" :tone="debriefStatusTone(debrief.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
