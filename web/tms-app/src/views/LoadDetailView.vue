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
  type Client, type ClientCurrency, type Commodity, type CommodityLine, type CostCentre, type Currency, type Driver,
  type Load, type LoadMargin, type LoadType, type Location, type Subcontractor, type SubcontractorCurrency,
  type UnitOfMeasure, type Vehicle,
} from '../api/types'
import { loadLegStatusTone, loadStatusTone, formatDateTime, formatMoney } from '../lib/presentation'

const props = defineProps<{ id: string }>()

const load = ref<Load | null>(null)
const clients = ref<Client[]>([])
const loadTypes = ref<LoadType[]>([])
const locations = ref<Location[]>([])
const costCentres = ref<CostCentre[]>([])
const vehicles = ref<Vehicle[]>([])
const drivers = ref<Driver[]>([])
const subcontractors = ref<Subcontractor[]>([])
const currencies = ref<Currency[]>([])
const commodities = ref<Commodity[]>([])
const unitsOfMeasure = ref<UnitOfMeasure[]>([])
const clientCurrencies = ref<ClientCurrency[]>([])
const margin = ref<LoadMargin | null>(null)

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
function currencyCode(currencyId: string | null): string {
  if (!currencyId) return ''
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}
function commodityLabel(id: string): string {
  const c = commodities.value.find((cm) => cm.id === id)
  return c ? `${c.code} — ${c.name}` : id
}
function uomCode(id: string): string {
  return unitsOfMeasure.value.find((u) => u.id === id)?.code ?? id
}
function marginForLeg(legId: string) {
  return margin.value?.legs.find((l) => l.legId === legId) ?? null
}

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [
      loadData, clientList, loadTypeList, locationList, costCentreList, vehicleList, driverList, subcontractorList,
      currencyList, commodityList, unitOfMeasureList, marginData,
    ] = await Promise.all([
      loadsApi.get(props.id),
      referenceApi.clients(),
      referenceApi.loadTypes(),
      referenceApi.locations(),
      referenceApi.costCentres(),
      referenceApi.vehicles(),
      referenceApi.drivers(),
      referenceApi.subcontractors(),
      referenceApi.currencies(),
      referenceApi.commodities(),
      referenceApi.unitsOfMeasure(),
      loadsApi.margin(props.id),
    ])
    load.value = loadData
    clients.value = clientList
    loadTypes.value = loadTypeList
    locations.value = locationList
    costCentres.value = costCentreList
    vehicles.value = vehicleList
    drivers.value = driverList
    subcontractors.value = subcontractorList
    currencies.value = currencyList
    commodities.value = commodityList
    unitsOfMeasure.value = unitOfMeasureList
    margin.value = marginData
    clientCurrencies.value = await referenceApi.clientCurrencies(loadData.clientId)
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

// --- Per-leg commodity lines (one panel open at a time) ---
// Append-only, same as the API itself (§5.5) — there's no edit/remove action to wire up.
const commodityLegId = ref<string | null>(null)
const commodityLines = ref<CommodityLine[]>([])
const commodityLinesLoading = ref(false)
const subcontractorCurrencies = ref<SubcontractorCurrency[]>([])
const commodityActionError = ref('')
const commodityActionBusy = ref(false)
// Only a credit-limit breach (422) offers an override — every other error just needs
// the message shown and the form fixed, the same distinction LoadCreateView makes.
const commodityCreditBreach = ref(false)

const newCommodityLine = ref({
  commodityId: '', quantity: 1, unitOfMeasureId: '', sellRatePerUnit: 0, sellCurrencyId: '',
  buyRatePerUnit: 0, buyCurrencyId: '', creditOverrideReason: '',
})

function sellCurrencyOptionsFor(clientId: string): Currency[] {
  const client = clients.value.find((c) => c.id === clientId)
  if (!client) return []
  const ids = [client.currencyId, ...clientCurrencies.value.map((cc) => cc.currencyId)]
  return currencies.value.filter((c) => ids.includes(c.id))
}

// A leg not yet Allocated to a subcontractor has no allow-list to check against yet
// (LoadsController.AddCommodityLine's own rule) — every currency is offered instead,
// with no default preselected, forcing an explicit choice rather than guessing one.
const buyCurrencyOptions = computed(() => {
  const leg = load.value?.legs.find((l) => l.id === commodityLegId.value)
  if (!leg?.subcontractorId) return currencies.value
  const subcontractor = subcontractors.value.find((s) => s.id === leg.subcontractorId)
  if (!subcontractor) return currencies.value
  const ids = [subcontractor.currencyId, ...subcontractorCurrencies.value.map((sc) => sc.currencyId)]
  return currencies.value.filter((c) => ids.includes(c.id))
})

async function openCommodityPanel(leg: { id: string; subcontractorId: string | null }) {
  commodityLegId.value = leg.id
  commodityActionError.value = ''
  commodityCreditBreach.value = false
  const client = clients.value.find((c) => c.id === load.value?.clientId)
  newCommodityLine.value = {
    commodityId: '', quantity: 1, unitOfMeasureId: '', sellRatePerUnit: 0, sellCurrencyId: client?.currencyId ?? '',
    buyRatePerUnit: 0, buyCurrencyId: leg.subcontractorId ? (subcontractors.value.find((s) => s.id === leg.subcontractorId)?.currencyId ?? '') : '',
    creditOverrideReason: '',
  }

  commodityLinesLoading.value = true
  try {
    const [lines, subCurrencies] = await Promise.all([
      loadsApi.commodityLines(props.id, leg.id),
      leg.subcontractorId ? referenceApi.subcontractorCurrencies(leg.subcontractorId) : Promise.resolve([]),
    ])
    commodityLines.value = lines
    subcontractorCurrencies.value = subCurrencies
  } catch (e) {
    commodityActionError.value = e instanceof ApiError ? e.message : 'Could not load this leg\'s commodity lines.'
  } finally {
    commodityLinesLoading.value = false
  }
}

function onCommodityChange() {
  const commodity = commodities.value.find((c) => c.id === newCommodityLine.value.commodityId)
  if (commodity) newCommodityLine.value.unitOfMeasureId = commodity.defaultUnitOfMeasureId
}

async function submitCommodityLine(legExecutionType: number) {
  if (!commodityLegId.value) return
  commodityActionError.value = ''
  commodityActionBusy.value = true
  try {
    await loadsApi.addCommodityLine(props.id, commodityLegId.value, {
      commodityId: newCommodityLine.value.commodityId,
      quantity: newCommodityLine.value.quantity,
      unitOfMeasureId: newCommodityLine.value.unitOfMeasureId,
      sellRatePerUnit: newCommodityLine.value.sellRatePerUnit,
      sellCurrencyId: newCommodityLine.value.sellCurrencyId || undefined,
      buyRatePerUnit: legExecutionType === 1 ? newCommodityLine.value.buyRatePerUnit : undefined,
      buyCurrencyId: legExecutionType === 1 ? newCommodityLine.value.buyCurrencyId || undefined : undefined,
      creditOverrideReason: newCommodityLine.value.creditOverrideReason || undefined,
    })
    commodityCreditBreach.value = false
    newCommodityLine.value.creditOverrideReason = ''
    const [lines, marginData] = await Promise.all([
      loadsApi.commodityLines(props.id, commodityLegId.value),
      loadsApi.margin(props.id),
    ])
    commodityLines.value = lines
    margin.value = marginData
  } catch (e) {
    if (e instanceof ApiError) {
      commodityActionError.value = e.message
      commodityCreditBreach.value = e.status === 422
    } else {
      commodityActionError.value = 'Something went wrong — please try again.'
      commodityCreditBreach.value = false
    }
  } finally {
    commodityActionBusy.value = false
  }
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
              <th class="px-4 py-3">Margin</th>
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
                <td class="px-4 py-3 text-slate-600">
                  <template v-if="marginForLeg(leg.id)">
                    <div>Sell {{ formatMoney(marginForLeg(leg.id)!.sellTotal, currencyCode(marginForLeg(leg.id)!.sellCurrencyId)) }}</div>
                    <div v-if="marginForLeg(leg.id)!.buyCurrencyId">Buy {{ formatMoney(marginForLeg(leg.id)!.buyTotal, currencyCode(marginForLeg(leg.id)!.buyCurrencyId)) }}</div>
                    <div v-if="marginForLeg(leg.id)!.margin !== null" class="font-medium" :class="marginForLeg(leg.id)!.margin! < 0 ? 'text-rose-700' : 'text-emerald-700'">
                      Margin {{ formatMoney(marginForLeg(leg.id)!.margin!, currencyCode(marginForLeg(leg.id)!.sellCurrencyId)) }}
                    </div>
                    <div v-else-if="marginForLeg(leg.id)!.note" class="text-xs text-amber-700">{{ marginForLeg(leg.id)!.note }}</div>
                  </template>
                  <span v-else class="text-slate-400">—</span>
                </td>
                <td class="px-4 py-3">
                  <div class="flex flex-col gap-1.5">
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
                    <button
                      type="button"
                      class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100"
                      @click="commodityLegId === leg.id ? (commodityLegId = null) : openCommodityPanel(leg)"
                    >
                      {{ commodityLegId === leg.id ? 'Hide commodities' : 'Commodities' }}
                    </button>
                  </div>
                </td>
              </tr>
              <tr v-if="allocatingLegId === leg.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
                <td colspan="8" class="px-4 py-3">
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
              <tr v-if="commodityLegId === leg.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
                <td colspan="8" class="px-4 py-3">
                  <ErrorAlert v-if="commodityActionError" :message="commodityActionError" class="mb-3" />
                  <p v-if="commodityLinesLoading" class="text-sm text-slate-500">Loading…</p>
                  <template v-else>
                    <table v-if="commodityLines.length" class="w-full text-left text-sm">
                      <thead class="text-xs uppercase tracking-wide text-slate-500">
                        <tr>
                          <th class="py-1 pr-4">Commodity</th>
                          <th class="py-1 pr-4">Qty</th>
                          <th class="py-1 pr-4">UoM</th>
                          <th class="py-1 pr-4">Sell</th>
                          <th class="py-1">Buy</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="line in commodityLines" :key="line.id">
                          <td class="py-1 pr-4 text-slate-900">{{ commodityLabel(line.commodityId) }}</td>
                          <td class="py-1 pr-4 text-slate-600">{{ line.quantity }}</td>
                          <td class="py-1 pr-4 text-slate-600">{{ uomCode(line.unitOfMeasureId) }}</td>
                          <td class="py-1 pr-4 text-slate-600">
                            {{ formatMoney(line.sellRatePerUnit, currencyCode(line.sellCurrencyId)) }}/unit ({{ formatMoney(line.sellAmount, currencyCode(line.sellCurrencyId)) }})
                          </td>
                          <td class="py-1 text-slate-600">
                            <template v-if="line.buyRatePerUnit !== null">
                              {{ formatMoney(line.buyRatePerUnit, currencyCode(line.buyCurrencyId)) }}/unit ({{ formatMoney(line.buyAmount!, currencyCode(line.buyCurrencyId)) }})
                            </template>
                            <span v-else class="text-slate-400">—</span>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                    <p v-else class="text-sm text-slate-500">No commodity lines yet.</p>

                    <form
                      class="mt-4 flex flex-wrap items-end gap-3 border-t border-slate-200 pt-4"
                      @submit.prevent="submitCommodityLine(leg.executionType)"
                    >
                      <label class="flex flex-col gap-1 text-sm text-slate-700">
                        Commodity
                        <select
                          v-model="newCommodityLine.commodityId"
                          required
                          class="rounded-md border border-slate-300 px-3 py-2 text-sm"
                          @change="onCommodityChange"
                        >
                          <option value="" disabled>Select…</option>
                          <option v-for="c in commodities" :key="c.id" :value="c.id">{{ c.code }} — {{ c.name }}</option>
                        </select>
                      </label>
                      <label class="flex flex-col gap-1 text-sm text-slate-700">
                        Quantity
                        <input
                          v-model.number="newCommodityLine.quantity"
                          type="number" step="any" min="0" required
                          class="w-24 rounded-md border border-slate-300 px-3 py-2 text-sm"
                        />
                      </label>
                      <label class="flex flex-col gap-1 text-sm text-slate-700">
                        Unit
                        <select v-model="newCommodityLine.unitOfMeasureId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                          <option value="" disabled>Select…</option>
                          <option v-for="u in unitsOfMeasure" :key="u.id" :value="u.id">{{ u.code }}</option>
                        </select>
                      </label>
                      <label class="flex flex-col gap-1 text-sm text-slate-700">
                        Sell rate/unit
                        <input
                          v-model.number="newCommodityLine.sellRatePerUnit"
                          type="number" step="any" min="0" required
                          class="w-28 rounded-md border border-slate-300 px-3 py-2 text-sm"
                        />
                      </label>
                      <label class="flex flex-col gap-1 text-sm text-slate-700">
                        Sell currency
                        <select v-model="newCommodityLine.sellCurrencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                          <option v-for="c in sellCurrencyOptionsFor(load.clientId)" :key="c.id" :value="c.id">{{ c.code }}</option>
                        </select>
                      </label>
                      <template v-if="leg.executionType === 1">
                        <label class="flex flex-col gap-1 text-sm text-slate-700">
                          Buy rate/unit
                          <input
                            v-model.number="newCommodityLine.buyRatePerUnit"
                            type="number" step="any" min="0" required
                            class="w-28 rounded-md border border-slate-300 px-3 py-2 text-sm"
                          />
                        </label>
                        <label class="flex flex-col gap-1 text-sm text-slate-700">
                          Buy currency
                          <select v-model="newCommodityLine.buyCurrencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                            <option value="" disabled>Select…</option>
                            <option v-for="c in buyCurrencyOptions" :key="c.id" :value="c.id">{{ c.code }}</option>
                          </select>
                        </label>
                      </template>
                      <label v-if="commodityCreditBreach" class="flex flex-col gap-1 text-sm text-slate-700">
                        Reason for credit-limit override
                        <input
                          v-model="newCommodityLine.creditOverrideReason"
                          type="text" required
                          class="rounded-md border border-slate-300 px-3 py-2 text-sm"
                        />
                      </label>
                      <button
                        type="submit"
                        :disabled="commodityActionBusy"
                        class="rounded-md bg-slate-900 px-3 py-2 text-sm text-white disabled:opacity-50"
                      >
                        {{ commodityActionBusy ? 'Adding…' : commodityCreditBreach ? 'Retry with override' : 'Add line' }}
                      </button>
                    </form>
                  </template>
                </td>
              </tr>
            </template>
            <tr v-if="load.legs.length === 0">
              <td colspan="8" class="px-4 py-6 text-center text-slate-500">No legs yet.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
