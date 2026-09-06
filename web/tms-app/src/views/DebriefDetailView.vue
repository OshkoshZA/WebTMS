<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { debriefsApi } from '../api/debriefs'
import { driversApi } from '../api/drivers'
import { vehiclesApi } from '../api/vehicles'
import { expenseTypesApi } from '../api/expenseTypes'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  CLAIMED_AGAINST, DEBRIEF_STATUS, INCIDENT_SEVERITY, INCIDENT_TYPE, label,
  type Currency, type Debrief, type Driver, type ExpenseType, type Vehicle,
} from '../api/types'
import { debriefStatusTone, formatDateTime, incidentSeverityTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const debrief = ref<Debrief | null>(null)
const drivers = ref<Driver[]>([])
const vehicles = ref<Vehicle[]>([])
const expenseTypes = ref<ExpenseType[]>([])
const currencies = ref<Currency[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canApprove = computed(() => auth.hasFunction('debrief.approve'))
const isPendingReview = computed(() => debrief.value?.status === 0)

const driverName = computed(() => drivers.value.find((d) => d.id === debrief.value?.driverId)?.name ?? '—')
const vehicleLabel = computed(() => vehicles.value.find((v) => v.id === debrief.value?.vehicleId)?.fleetNo ?? '—')

function expenseTypeName(id: string): string {
  return expenseTypes.value.find((t) => t.id === id)?.name ?? id.slice(0, 8)
}

function currencyCode(id: string): string {
  return currencies.value.find((c) => c.id === id)?.code ?? id.slice(0, 8)
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [debriefData, driverList, vehicleList, expenseTypeList, currencyList] = await Promise.all([
      debriefsApi.get(props.id),
      driversApi.list(),
      vehiclesApi.list(),
      expenseTypesApi.list(),
      referenceApi.currencies(),
    ])
    debrief.value = debriefData
    drivers.value = driverList
    vehicles.value = vehicleList
    expenseTypes.value = expenseTypeList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this debrief.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

const resolutionNote = ref('')

async function approve() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await debriefsApi.approve(props.id, resolutionNote.value || undefined)
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="debrief">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">Debrief — {{ driverName }}</h1>
          <p class="mt-1 font-mono text-sm text-slate-600">Leg {{ debrief.loadLegId.slice(0, 8) }}</p>
        </div>
        <StatusBadge :text="label(DEBRIEF_STATUS, debrief.status)" :tone="debriefStatusTone(debrief.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />
      <p v-if="debrief.exceptionReasons" class="mt-3 rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
        {{ debrief.exceptionReasons }}
      </p>

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Vehicle</dt>
          <dd class="text-slate-900">{{ vehicleLabel }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Submitted</dt>
          <dd class="text-slate-900">{{ formatDateTime(debrief.submittedAt) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Odometer</dt>
          <dd class="text-slate-900">{{ debrief.odometerStart ?? '—' }} → {{ debrief.odometerEnd ?? '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Driving hours</dt>
          <dd class="text-slate-900">{{ debrief.drivingHours ?? '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Fuel</dt>
          <dd class="text-slate-900">{{ debrief.fuelLitres ?? '—' }} L / {{ debrief.fuelCost ?? '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">POD received</dt>
          <dd class="text-slate-900">{{ debrief.podReceived ? 'Yes' : 'No' }}</dd>
        </div>
        <div v-if="debrief.resolvedAt">
          <dt class="text-slate-500">Resolved</dt>
          <dd class="text-slate-900">{{ formatDateTime(debrief.resolvedAt) }}</dd>
        </div>
        <div v-if="debrief.resolutionNote" class="col-span-2">
          <dt class="text-slate-500">Resolution note</dt>
          <dd class="text-slate-900">{{ debrief.resolutionNote }}</dd>
        </div>
      </dl>

      <div v-if="canApprove && isPendingReview" class="mt-4 max-w-lg rounded-lg border border-slate-200 bg-white p-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Resolution note (optional)
          <input v-model="resolutionNote" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <button
          type="button"
          :disabled="actionBusy"
          class="mt-3 rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          @click="approve"
        >
          {{ actionBusy ? 'Approving…' : 'Approve' }}
        </button>
      </div>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Incidents</h2>
      <p v-if="debrief.incidents.length === 0" class="mt-3 text-sm text-slate-500">No incidents logged.</p>
      <div v-else class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Type</th>
              <th class="px-4 py-3">Severity</th>
              <th class="px-4 py-3">Narrative</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="incident in debrief.incidents" :key="incident.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-900">{{ label(INCIDENT_TYPE, incident.type) }}</td>
              <td class="px-4 py-3">
                <StatusBadge :text="label(INCIDENT_SEVERITY, incident.severity)" :tone="incidentSeverityTone(incident.severity)" />
              </td>
              <td class="px-4 py-3 text-slate-600">{{ incident.narrative }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Expenses</h2>
      <p v-if="debrief.expenses.length === 0" class="mt-3 text-sm text-slate-500">No expenses claimed.</p>
      <div v-else class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Type</th>
              <th class="px-4 py-3">Description</th>
              <th class="px-4 py-3">Amount</th>
              <th class="px-4 py-3">Claimed against</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="expense in debrief.expenses" :key="expense.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-900">{{ expenseTypeName(expense.expenseTypeId) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ expense.description }}</td>
              <td class="px-4 py-3 text-slate-600">{{ expense.amount }} {{ currencyCode(expense.currencyId) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ label(CLAIMED_AGAINST, expense.claimedAgainst) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
