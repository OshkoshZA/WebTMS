<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { loadsApi } from '../api/loads'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import {
  LOAD_LEG_EXECUTION_TYPE, LOAD_LEG_STATUS, LOAD_STATUS, label,
  type Client, type CostCentre, type Driver, type Load, type LoadType, type Location, type Subcontractor, type Vehicle,
} from '../api/types'
import { loadLegStatusTone, loadStatusTone, formatDateTime } from '../lib/presentation'

const props = defineProps<{ id: string }>()

const load = ref<Load | null>(null)
const clients = ref<Client[]>([])
const loadTypes = ref<LoadType[]>([])
const locations = ref<Location[]>([])
const costCentres = ref<CostCentre[]>([])
const vehicles = ref<Vehicle[]>([])
const drivers = ref<Driver[]>([])
const subcontractors = ref<Subcontractor[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')

const clientName = computed(() => clients.value.find((c) => c.id === load.value?.clientId)?.name ?? '—')
const loadTypeLabel = computed(() => {
  const t = loadTypes.value.find((lt) => lt.id === load.value?.loadTypeId)
  return t ? `${t.code} — ${t.description}` : '—'
})
function locationName(id: string): string {
  return locations.value.find((l) => l.id === id)?.name ?? id
}
function costCentreLabel(id: string): string {
  const cc = costCentres.value.find((c) => c.id === id)
  return cc ? `${cc.code} — ${cc.name}` : id
}
function resourceLabel(legVehicleId: string | null, legDriverId: string | null, legSubcontractorId: string | null): string {
  if (legSubcontractorId) return subcontractors.value.find((s) => s.id === legSubcontractorId)?.name ?? legSubcontractorId
  if (legVehicleId && legDriverId) {
    const vehicle = vehicles.value.find((v) => v.id === legVehicleId)?.fleetNo ?? legVehicleId
    const driver = drivers.value.find((d) => d.id === legDriverId)?.name ?? legDriverId
    return `${vehicle} / ${driver}`
  }
  return 'Unassigned'
}

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [loadData, clientList, loadTypeList, locationList, costCentreList, vehicleList, driverList, subcontractorList] =
      await Promise.all([
        loadsApi.get(props.id),
        referenceApi.clients(),
        referenceApi.loadTypes(),
        referenceApi.locations(),
        referenceApi.costCentres(),
        referenceApi.vehicles(),
        referenceApi.drivers(),
        referenceApi.subcontractors(),
      ])
    load.value = loadData
    clients.value = clientList
    loadTypes.value = loadTypeList
    locations.value = locationList
    costCentres.value = costCentreList
    vehicles.value = vehicleList
    drivers.value = driverList
    subcontractors.value = subcontractorList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this load.'
  } finally {
    loading.value = false
  }
}

onMounted(loadEverything)

// --- Load-level actions ---
const holdReasonOpen = ref(false)
const holdReason = ref('')
const actionBusy = ref(false)

async function runAction(action: () => Promise<void>) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await action()
    await loadEverything()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

function submitHold() {
  if (!holdReason.value.trim()) return
  runAction(async () => {
    await loadsApi.hold(props.id, holdReason.value)
    holdReasonOpen.value = false
    holdReason.value = ''
  })
}
function releaseHold() {
  runAction(() => loadsApi.releaseHold(props.id))
}
function cancelLoad() {
  runAction(() => loadsApi.cancel(props.id))
}

// --- Add-leg form ---
const addLegOpen = ref(false)
const newLeg = ref({
  originLocationId: '',
  destinationLocationId: '',
  executionType: 0,
  costCentreId: '',
})

function openAddLeg() {
  addLegOpen.value = true
  newLeg.value = { originLocationId: '', destinationLocationId: '', executionType: 0, costCentreId: '' }
}

function submitAddLeg() {
  const nextSequenceNo = 1 + Math.max(0, ...(load.value?.legs.map((l) => l.sequenceNo) ?? [0]))
  runAction(async () => {
    await loadsApi.addLeg(props.id, {
      sequenceNo: nextSequenceNo,
      originLocationId: newLeg.value.originLocationId,
      destinationLocationId: newLeg.value.destinationLocationId,
      executionType: newLeg.value.executionType,
      costCentreId: newLeg.value.costCentreId,
    })
    addLegOpen.value = false
  })
}

// --- Per-leg allocate form (one open at a time) ---
const allocatingLegId = ref<string | null>(null)
const allocation = ref({ vehicleId: '', driverId: '', subcontractorId: '' })

function openAllocate(legId: string) {
  allocatingLegId.value = legId
  allocation.value = { vehicleId: '', driverId: '', subcontractorId: '' }
}

function submitAllocate(legId: string, executionType: number) {
  runAction(async () => {
    await loadsApi.allocateLeg(props.id, legId, {
      vehicleId: executionType === 0 ? allocation.value.vehicleId : undefined,
      driverId: executionType === 0 ? allocation.value.driverId : undefined,
      subcontractorId: executionType === 1 ? allocation.value.subcontractorId : undefined,
    })
    allocatingLegId.value = null
  })
}

function startLeg(legId: string) {
  runAction(() => loadsApi.startLeg(props.id, legId))
}
function deliverLeg(legId: string) {
  runAction(() => loadsApi.deliverLeg(props.id, legId))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="load">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ load.referenceNo }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ clientName }} · {{ loadTypeLabel }}</p>
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

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <div class="mt-4 flex flex-wrap gap-3">
        <button
          v-if="load.status === 3"
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-amber-300 bg-amber-50 px-3 py-1.5 text-sm font-medium text-amber-800 hover:bg-amber-100 disabled:opacity-50"
          @click="holdReasonOpen = true"
        >
          Put on hold
        </button>
        <button
          v-if="load.status === 7"
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-sky-300 bg-sky-50 px-3 py-1.5 text-sm font-medium text-sky-800 hover:bg-sky-100 disabled:opacity-50"
          @click="releaseHold"
        >
          Release hold
        </button>
        <button
          v-if="load.status === 1 || load.status === 2"
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
          @click="cancelLoad"
        >
          Cancel load
        </button>
      </div>

      <div v-if="holdReasonOpen" class="mt-3 flex max-w-md gap-2">
        <input
          v-model="holdReason"
          type="text"
          placeholder="Reason for hold…"
          class="flex-1 rounded-md border border-slate-300 px-3 py-1.5 text-sm"
        />
        <button type="button" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white" @click="submitHold">Confirm</button>
        <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="holdReasonOpen = false">Cancel</button>
      </div>

      <div class="mt-8 flex items-center justify-between">
        <h2 class="text-lg font-semibold text-slate-900">Legs</h2>
        <button
          type="button"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-100"
          @click="openAddLeg"
        >
          Add leg
        </button>
      </div>

      <form
        v-if="addLegOpen"
        class="mt-3 grid max-w-2xl grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4"
        @submit.prevent="submitAddLeg"
      >
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Origin
          <select v-model="newLeg.originLocationId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="loc in locations" :key="loc.id" :value="loc.id">{{ loc.name }}</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Destination
          <select v-model="newLeg.destinationLocationId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="loc in locations" :key="loc.id" :value="loc.id">{{ loc.name }}</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Cost centre
          <select v-model="newLeg.costCentreId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="cc in costCentres" :key="cc.id" :value="cc.id">{{ cc.code }} — {{ cc.name }}</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Execution
          <select v-model.number="newLeg.executionType" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option :value="0">Own fleet</option>
            <option :value="1">Subcontracted</option>
          </select>
        </label>
        <div class="col-span-2 flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
            Add leg
          </button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="addLegOpen = false">
            Cancel
          </button>
        </div>
      </form>

      <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">#</th>
              <th class="px-4 py-3">Route</th>
              <th class="px-4 py-3">Cost centre</th>
              <th class="px-4 py-3">Execution</th>
              <th class="px-4 py-3">Resource</th>
              <th class="px-4 py-3">Status</th>
              <th class="px-4 py-3">Action</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="leg in load.legs.slice().sort((a, b) => a.sequenceNo - b.sequenceNo)" :key="leg.id">
              <tr class="border-b border-slate-100 last:border-0">
                <td class="px-4 py-3 text-slate-600">{{ leg.sequenceNo }}</td>
                <td class="px-4 py-3 text-slate-900">{{ locationName(leg.originLocationId) }} → {{ locationName(leg.destinationLocationId) }}</td>
                <td class="px-4 py-3 text-slate-600">{{ costCentreLabel(leg.costCentreId) }}</td>
                <td class="px-4 py-3 text-slate-600">{{ label(LOAD_LEG_EXECUTION_TYPE, leg.executionType) }}</td>
                <td class="px-4 py-3 text-slate-600">{{ resourceLabel(leg.vehicleId, leg.driverId, leg.subcontractorId) }}</td>
                <td class="px-4 py-3"><StatusBadge :text="label(LOAD_LEG_STATUS, leg.status)" :tone="loadLegStatusTone(leg.status)" /></td>
                <td class="px-4 py-3">
                  <button
                    v-if="leg.status === 0"
                    type="button"
                    :disabled="actionBusy"
                    class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                    @click="openAllocate(leg.id)"
                  >
                    Allocate
                  </button>
                  <button
                    v-else-if="leg.status === 1"
                    type="button"
                    :disabled="actionBusy"
                    class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                    @click="startLeg(leg.id)"
                  >
                    Start
                  </button>
                  <button
                    v-else-if="leg.status === 2"
                    type="button"
                    :disabled="actionBusy"
                    class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                    @click="deliverLeg(leg.id)"
                  >
                    Deliver
                  </button>
                </td>
              </tr>
              <tr v-if="allocatingLegId === leg.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
                <td colspan="7" class="px-4 py-3">
                  <form class="flex flex-wrap items-end gap-3" @submit.prevent="submitAllocate(leg.id, leg.executionType)">
                    <template v-if="leg.executionType === 0">
                      <label class="flex flex-col gap-1 text-sm text-slate-700">
                        Vehicle
                        <select v-model="allocation.vehicleId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                          <option value="" disabled>Select…</option>
                          <option v-for="v in vehicles" :key="v.id" :value="v.id">{{ v.fleetNo }}</option>
                        </select>
                      </label>
                      <label class="flex flex-col gap-1 text-sm text-slate-700">
                        Driver
                        <select v-model="allocation.driverId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                          <option value="" disabled>Select…</option>
                          <option v-for="d in drivers" :key="d.id" :value="d.id">{{ d.name }}</option>
                        </select>
                      </label>
                    </template>
                    <label v-else class="flex flex-col gap-1 text-sm text-slate-700">
                      Subcontractor
                      <select v-model="allocation.subcontractorId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                        <option value="" disabled>Select…</option>
                        <option v-for="s in subcontractors" :key="s.id" :value="s.id">{{ s.name }}</option>
                      </select>
                    </label>
                    <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
                      Confirm
                    </button>
                    <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="allocatingLegId = null">
                      Cancel
                    </button>
                  </form>
                </td>
              </tr>
            </template>
            <tr v-if="load.legs.length === 0">
              <td colspan="7" class="px-4 py-6 text-center text-slate-500">No legs yet.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
