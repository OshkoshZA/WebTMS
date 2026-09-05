<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { financialCalendarApi } from '../api/financialCalendar'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  FINANCIAL_PERIOD_STATUS, FINANCIAL_YEAR_STATUS, label,
  type Client, type Currency, type DebtorsAgingSnapshot, type FinancialYear,
} from '../api/types'
import { financialCalendarStatusTone, formatDate, formatMoney } from '../lib/presentation'

const auth = useAuthStore()

const years = ref<FinancialYear[]>([])
const clients = ref<Client[]>([])
const currencies = ref<Currency[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManageCalendar = computed(() => auth.hasFunction('finance.calendar.manage'))
const canClosePeriod = computed(() => auth.hasFunction('finance.period.close'))

const sortedYears = computed(() => years.value.slice().sort((a, b) => a.startDate.localeCompare(b.startDate)))

function clientName(id: string): string {
  return clients.value.find((c) => c.id === id)?.name ?? id
}

function clientCurrencyCode(clientId: string): string {
  const client = clients.value.find((c) => c.id === clientId)
  if (!client) return ''
  return currencies.value.find((c) => c.id === client.currencyId)?.code ?? ''
}

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [yearList, clientList, currencyList] = await Promise.all([
      financialCalendarApi.listYears(),
      referenceApi.clients(),
      referenceApi.currencies(),
    ])
    years.value = yearList
    clients.value = clientList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the financial calendar.'
  } finally {
    loading.value = false
  }
}

onMounted(loadEverything)

// --- New financial year ---
const newYearOpen = ref(false)
const newYear = ref({ yearLabel: '', startDate: '', endDate: '', periodCount: 12 })

function openNewYear() {
  // Defaults StartDate to the day after the latest year's own EndDate — CreateFinancialYearRequest's
  // own contiguity rule, so the form doesn't even offer a date the API would reject.
  const latest = sortedYears.value.at(-1)
  const suggestedStart = latest ? new Date(new Date(latest.endDate).getTime() + 86400000).toISOString().slice(0, 10) : ''
  newYear.value = { yearLabel: '', startDate: suggestedStart, endDate: '', periodCount: 12 }
  newYearOpen.value = true
}

async function submitNewYear() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await financialCalendarApi.createYear(newYear.value)
    newYearOpen.value = false
    await loadEverything()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// --- Close a period ---
async function closePeriod(id: string) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await financialCalendarApi.closePeriod(id)
    await loadEverything()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not close this period.'
  } finally {
    actionBusy.value = false
  }
}

// --- Debtors aging (one period expanded at a time) ---
const expandedPeriodId = ref<string | null>(null)
const debtorsAging = ref<DebtorsAgingSnapshot[]>([])
const debtorsAgingLoading = ref(false)

async function toggleDebtorsAging(periodId: string) {
  if (expandedPeriodId.value === periodId) {
    expandedPeriodId.value = null
    return
  }
  expandedPeriodId.value = periodId
  debtorsAgingLoading.value = true
  try {
    debtorsAging.value = await financialCalendarApi.debtorsAging(periodId)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not load debtors aging for this period.'
  } finally {
    debtorsAgingLoading.value = false
  }
}
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Financial calendar</h1>
      <button
        v-if="canManageCalendar"
        type="button"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
        @click="openNewYear"
      >
        New financial year
      </button>
    </div>
    <p class="mt-1 text-sm text-slate-500">Exactly one period is ever Open at a time (§10.3) — closing it opens the next in sequence.</p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <ErrorAlert v-else-if="actionError" :message="actionError" class="mt-4" />
    <p v-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form
      v-if="newYearOpen"
      class="mt-4 grid max-w-2xl grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4"
      @submit.prevent="submitNewYear"
    >
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Year label
        <input v-model="newYear.yearLabel" type="text" required placeholder="e.g. FY2027" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Period count
        <input v-model.number="newYear.periodCount" type="number" min="1" max="13" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Start date
        <input v-model="newYear.startDate" type="date" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        End date
        <input v-model="newYear.endDate" type="date" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <div class="col-span-2 flex gap-3">
        <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
          {{ actionBusy ? 'Creating…' : 'Create year' }}
        </button>
        <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="newYearOpen = false">Cancel</button>
      </div>
    </form>

    <template v-if="!loading">
      <div v-for="year in sortedYears" :key="year.id" class="mt-6 rounded-lg border border-slate-200 bg-white p-4">
        <div class="flex items-center justify-between">
          <div>
            <h2 class="text-lg font-semibold text-slate-900">{{ year.yearLabel }}</h2>
            <p class="text-sm text-slate-500">{{ formatDate(year.startDate) }} – {{ formatDate(year.endDate) }}</p>
          </div>
          <StatusBadge :text="label(FINANCIAL_YEAR_STATUS, year.status)" :tone="financialCalendarStatusTone(year.status)" />
        </div>

        <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200">
          <table class="w-full text-left text-sm">
            <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
              <tr>
                <th class="px-4 py-3">#</th>
                <th class="px-4 py-3">Name</th>
                <th class="px-4 py-3">Start</th>
                <th class="px-4 py-3">End</th>
                <th class="px-4 py-3">Status</th>
                <th class="px-4 py-3">Action</th>
              </tr>
            </thead>
            <tbody>
              <template v-for="period in year.periods" :key="period.id">
                <tr class="border-b border-slate-100 last:border-0">
                  <td class="px-4 py-3 text-slate-600">{{ period.periodNumber }}</td>
                  <td class="px-4 py-3 font-medium text-slate-900">{{ period.name }}</td>
                  <td class="px-4 py-3 text-slate-600">{{ formatDate(period.startDate) }}</td>
                  <td class="px-4 py-3 text-slate-600">{{ formatDate(period.endDate) }}</td>
                  <td class="px-4 py-3">
                    <StatusBadge :text="label(FINANCIAL_PERIOD_STATUS, period.status)" :tone="financialCalendarStatusTone(period.status)" />
                  </td>
                  <td class="px-4 py-3">
                    <div class="flex gap-2">
                      <button
                        v-if="period.status === 1 && canClosePeriod"
                        type="button"
                        :disabled="actionBusy"
                        class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                        @click="closePeriod(period.id)"
                      >
                        Close
                      </button>
                      <button
                        v-if="period.status === 2"
                        type="button"
                        class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100"
                        @click="toggleDebtorsAging(period.id)"
                      >
                        {{ expandedPeriodId === period.id ? 'Hide aging' : 'Debtors aging' }}
                      </button>
                    </div>
                  </td>
                </tr>
                <tr v-if="expandedPeriodId === period.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
                  <td colspan="6" class="px-4 py-3">
                    <p v-if="debtorsAgingLoading" class="text-sm text-slate-500">Loading…</p>
                    <table v-else-if="debtorsAging.length" class="w-full text-left text-sm">
                      <thead class="text-xs uppercase tracking-wide text-slate-500">
                        <tr>
                          <th class="py-1 pr-4">Client</th>
                          <th class="py-1 pr-4">Current</th>
                          <th class="py-1 pr-4">30 days</th>
                          <th class="py-1 pr-4">60 days</th>
                          <th class="py-1 pr-4">90 days</th>
                          <th class="py-1 pr-4">90+ days</th>
                          <th class="py-1">Total</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="snapshot in debtorsAging" :key="snapshot.id">
                          <td class="py-1 pr-4 text-slate-900">{{ clientName(snapshot.clientId) }}</td>
                          <td class="py-1 pr-4 text-slate-600">{{ formatMoney(snapshot.currentAmount, clientCurrencyCode(snapshot.clientId)) }}</td>
                          <td class="py-1 pr-4 text-slate-600">{{ formatMoney(snapshot.days30, clientCurrencyCode(snapshot.clientId)) }}</td>
                          <td class="py-1 pr-4 text-slate-600">{{ formatMoney(snapshot.days60, clientCurrencyCode(snapshot.clientId)) }}</td>
                          <td class="py-1 pr-4 text-slate-600">{{ formatMoney(snapshot.days90, clientCurrencyCode(snapshot.clientId)) }}</td>
                          <td class="py-1 pr-4 text-slate-600">{{ formatMoney(snapshot.days90Plus, clientCurrencyCode(snapshot.clientId)) }}</td>
                          <td class="py-1 font-medium text-slate-900">{{ formatMoney(snapshot.totalOutstanding, clientCurrencyCode(snapshot.clientId)) }}</td>
                        </tr>
                      </tbody>
                    </table>
                    <p v-else class="text-sm text-slate-500">No debtors aging snapshots for this period.</p>
                  </td>
                </tr>
              </template>
            </tbody>
          </table>
        </div>
      </div>

      <p v-if="sortedYears.length === 0" class="mt-6 text-sm text-slate-500">No financial years captured yet.</p>
    </template>
  </AppLayout>
</template>
