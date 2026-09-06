<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { vehiclesApi } from '../api/vehicles'
import { driversApi } from '../api/drivers'
import { complianceApi } from '../api/compliance'
import { ApiError } from '../api/client'
import type { Driver, Vehicle } from '../api/types'
import { formatDate } from '../lib/presentation'
import type { Tone } from '../lib/presentation'

const router = useRouter()

const vehicles = ref<Vehicle[]>([])
const drivers = ref<Driver[]>([])
const loading = ref(true)
const error = ref('')
// Best-effort, not on the critical path: this screen's own tiles are computed
// entirely client-side either way (see daysAhead below), so a caller lacking
// exception.manage, or any transient failure, still gets the full screen — the sync
// to §16.1's shared Exception mechanism just silently doesn't happen this visit.
const exceptionsSynced = ref(false)

// How far ahead "expiring soon" looks — purely a display threshold, recomputed
// client-side; there's no backend endpoint to filter by, so both full lists are
// fetched once and every date comparison happens here.
const daysAhead = ref(30)
const filter = ref<'all' | 'expired' | 'soon' | 'untracked'>('all')

interface ComplianceItem {
  key: string
  entityType: 'Vehicle' | 'Driver'
  entityLabel: string
  itemName: string
  expiryDate: string
  daysUntil: number
  route: string
}

interface UntrackedEntity {
  key: string
  entityType: 'Vehicle' | 'Driver'
  entityLabel: string
  missing: string[]
  route: string
}

function daysBetween(dateStr: string): number {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const target = new Date(dateStr)
  return Math.round((target.getTime() - today.getTime()) / 86400000)
}

// Active(0)/OnLeave(1) vehicles+drivers only — a Deactivated unit isn't being
// dispatched, so its compliance dates aren't operationally relevant here.
const activeVehicles = computed(() => vehicles.value.filter((v) => v.status === 0))
const activeDrivers = computed(() => drivers.value.filter((d) => d.status === 0 || d.status === 1))

const complianceItems = computed<ComplianceItem[]>(() => {
  const items: ComplianceItem[] = []
  for (const v of activeVehicles.value) {
    const label = `${v.fleetNo} — ${v.registration}`
    if (v.licenceExpiry) {
      items.push({ key: `v-${v.id}-lic`, entityType: 'Vehicle', entityLabel: label, itemName: 'Licence', expiryDate: v.licenceExpiry, daysUntil: daysBetween(v.licenceExpiry), route: `/vehicles/${v.id}` })
    }
    if (v.vehicleTestExpiry) {
      items.push({ key: `v-${v.id}-test`, entityType: 'Vehicle', entityLabel: label, itemName: 'Vehicle test', expiryDate: v.vehicleTestExpiry, daysUntil: daysBetween(v.vehicleTestExpiry), route: `/vehicles/${v.id}` })
    }
  }
  for (const d of activeDrivers.value) {
    const label = `${d.employeeNo} — ${d.name}`
    if (d.licenceExpiry) {
      items.push({ key: `d-${d.id}-lic`, entityType: 'Driver', entityLabel: label, itemName: 'Licence', expiryDate: d.licenceExpiry, daysUntil: daysBetween(d.licenceExpiry), route: `/drivers/${d.id}` })
    }
    if (d.pdpExpiry) {
      items.push({ key: `d-${d.id}-pdp`, entityType: 'Driver', entityLabel: label, itemName: 'PDP', expiryDate: d.pdpExpiry, daysUntil: daysBetween(d.pdpExpiry), route: `/drivers/${d.id}` })
    }
  }
  return items.sort((a, b) => a.daysUntil - b.daysUntil)
})

const untrackedEntities = computed<UntrackedEntity[]>(() => {
  const entities: UntrackedEntity[] = []
  for (const v of activeVehicles.value) {
    const missing: string[] = []
    if (!v.licenceExpiry) missing.push('Licence')
    if (!v.vehicleTestExpiry) missing.push('Vehicle test')
    if (missing.length > 0) entities.push({ key: `v-${v.id}`, entityType: 'Vehicle', entityLabel: `${v.fleetNo} — ${v.registration}`, missing, route: `/vehicles/${v.id}` })
  }
  for (const d of activeDrivers.value) {
    const missing: string[] = []
    if (!d.licenceExpiry) missing.push('Licence')
    if (!d.pdpExpiry) missing.push('PDP')
    if (missing.length > 0) entities.push({ key: `d-${d.id}`, entityType: 'Driver', entityLabel: `${d.employeeNo} — ${d.name}`, missing, route: `/drivers/${d.id}` })
  }
  return entities
})

const expiredItems = computed(() => complianceItems.value.filter((i) => i.daysUntil < 0))
const soonItems = computed(() => complianceItems.value.filter((i) => i.daysUntil >= 0 && i.daysUntil <= daysAhead.value))

const visibleItems = computed(() => {
  if (filter.value === 'expired') return expiredItems.value
  if (filter.value === 'soon') return soonItems.value
  return complianceItems.value
})

function itemTone(daysUntil: number): Tone {
  if (daysUntil < 0) return 'danger'
  if (daysUntil <= daysAhead.value) return 'warning'
  return 'success'
}

function itemStatusText(daysUntil: number): string {
  if (daysUntil < 0) return `Expired ${Math.abs(daysUntil)}d ago`
  if (daysUntil === 0) return 'Expires today'
  return `${daysUntil}d remaining`
}

onMounted(async () => {
  // No background job exists anywhere in this codebase to run this on a schedule
  // (§4.3/§11.3's own documented gap) — so it runs on demand instead, once per visit
  // to this screen, alongside (not blocking) the reads below.
  complianceApi.reconcileExceptions().then(() => {
    exceptionsSynced.value = true
  }).catch(() => {
    // Silently ignored — see exceptionsSynced's own comment above.
  })

  try {
    const [vehicleList, driverList] = await Promise.all([vehiclesApi.list(), driversApi.list()])
    vehicles.value = vehicleList
    drivers.value = driverList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load compliance data.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Compliance</h1>
    <p class="mt-1 text-sm text-slate-500">
      Vehicle licence/vehicle-test and driver licence/PDP expiry, computed from the fleet and driver masters — no separate tracking of its own.
    </p>
    <p v-if="exceptionsSynced" class="mt-1 text-xs text-slate-400">
      Synced to <RouterLink to="/exceptions" class="underline hover:text-slate-600">Exceptions</RouterLink> just now.
    </p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>

    <template v-else>
      <div class="mt-6 grid grid-cols-2 gap-4 sm:grid-cols-3">
        <button
          type="button"
          class="rounded-lg border p-4 text-left hover:shadow-sm"
          :class="filter === 'expired' ? 'border-rose-400 bg-rose-50' : 'border-slate-200 bg-white hover:border-slate-300'"
          @click="filter = 'expired'"
        >
          <p class="text-2xl font-semibold text-rose-700">{{ expiredItems.length }}</p>
          <p class="mt-1 text-sm text-slate-500">Expired</p>
        </button>
        <button
          type="button"
          class="rounded-lg border p-4 text-left hover:shadow-sm"
          :class="filter === 'soon' ? 'border-amber-400 bg-amber-50' : 'border-slate-200 bg-white hover:border-slate-300'"
          @click="filter = 'soon'"
        >
          <p class="text-2xl font-semibold text-amber-700">{{ soonItems.length }}</p>
          <p class="mt-1 text-sm text-slate-500">Expiring within {{ daysAhead }} days</p>
        </button>
        <button
          type="button"
          class="rounded-lg border p-4 text-left hover:shadow-sm"
          :class="filter === 'untracked' ? 'border-slate-400 bg-slate-100' : 'border-slate-200 bg-white hover:border-slate-300'"
          @click="filter = 'untracked'"
        >
          <p class="text-2xl font-semibold text-slate-900">{{ untrackedEntities.length }}</p>
          <p class="mt-1 text-sm text-slate-500">Not tracked</p>
        </button>
      </div>

      <div class="mt-4 flex items-center gap-3">
        <button
          type="button"
          class="text-sm font-medium text-slate-600 hover:text-slate-900"
          :class="filter === 'all' && 'underline'"
          @click="filter = 'all'"
        >
          Show all tracked items
        </button>
        <label class="ml-auto flex items-center gap-2 text-sm text-slate-600">
          "Soon" threshold
          <input v-model.number="daysAhead" type="number" min="1" class="w-20 rounded-md border border-slate-300 px-2 py-1 text-sm" />
          days
        </label>
      </div>

      <div v-if="filter !== 'untracked'" class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Type</th>
              <th class="px-4 py-3">Identifier</th>
              <th class="px-4 py-3">Item</th>
              <th class="px-4 py-3">Expiry date</th>
              <th class="px-4 py-3">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in visibleItems"
              :key="item.key"
              class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
              @click="router.push(item.route)"
            >
              <td class="px-4 py-3 text-slate-600">{{ item.entityType }}</td>
              <td class="px-4 py-3 font-medium text-slate-900">{{ item.entityLabel }}</td>
              <td class="px-4 py-3 text-slate-600">{{ item.itemName }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDate(item.expiryDate) }}</td>
              <td class="px-4 py-3"><StatusBadge :text="itemStatusText(item.daysUntil)" :tone="itemTone(item.daysUntil)" /></td>
            </tr>
            <tr v-if="visibleItems.length === 0">
              <td colspan="5" class="px-4 py-6 text-center text-slate-500">Nothing to show for this filter.</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Type</th>
              <th class="px-4 py-3">Identifier</th>
              <th class="px-4 py-3">Missing</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="entity in untrackedEntities"
              :key="entity.key"
              class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
              @click="router.push(entity.route)"
            >
              <td class="px-4 py-3 text-slate-600">{{ entity.entityType }}</td>
              <td class="px-4 py-3 font-medium text-slate-900">{{ entity.entityLabel }}</td>
              <td class="px-4 py-3 text-slate-600">{{ entity.missing.join(', ') }}</td>
            </tr>
            <tr v-if="untrackedEntities.length === 0">
              <td colspan="3" class="px-4 py-6 text-center text-slate-500">Every active vehicle and driver has both dates captured.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
