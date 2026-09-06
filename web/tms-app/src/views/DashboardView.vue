<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { loadsApi } from '../api/loads'
import { exceptionsApi } from '../api/exceptions'
import { dashboardApi } from '../api/dashboard'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  EXCEPTION_SEVERITY, LOAD_STATUS,
  type AgedDebtorsSummary, type CreditExposureSummary, type Currency, type ExceptionRecord, type Load,
  type MarginSummary, type OnTimeDeliverySummary, type PayablesSummary,
} from '../api/types'
import { formatMoney } from '../lib/presentation'

const auth = useAuthStore()
// Role *names* are free text a company chooses for itself (§07), so there's no fixed
// "Dispatcher"/"Finance Clerk" identifier to key off — this reads the Function codes
// the session actually holds instead (§16.2's own "same underlying data, different
// emphasis"): any finance.* function promotes the finance-flavoured tiles (margin,
// credit exposure, payables) ahead of the dispatch-flavoured ones (exceptions, load
// status); everyone else — including a plain Dispatcher, who holds no function of
// their own at all for LoadsController's own design (§5.2) — gets the dispatch
// tiles first, unchanged from this screen's original order.
const financeEmphasis = computed(() => auth.hasAnyFunctionWithPrefix('finance.'))

const loads = ref<Load[]>([])
const openExceptions = ref<ExceptionRecord[]>([])
const currencies = ref<Currency[]>([])
const marginSummary = ref<MarginSummary | null>(null)
const creditExposureSummary = ref<CreditExposureSummary | null>(null)
const payablesSummary = ref<PayablesSummary | null>(null)
const agedDebtorsSummary = ref<AgedDebtorsSummary | null>(null)
const onTimeDeliverySummary = ref<OnTimeDeliverySummary | null>(null)
const loading = ref(true)
const error = ref('')

// Two tiles were originally built here — every other §16.2 tile (sell/buy margin,
// aged debtors, credit exposure across clients, subcontractor payables summary,
// on-time delivery rate) needed either a new backend aggregate that didn't exist yet
// or data this schema doesn't track at all. Three of those five are now built
// (DashboardController) — aged debtors and on-time delivery rate remain a known,
// bounded gap for the reasons docs/architecture.html §16.2 still documents.
const loadCountsByStatus = computed(() => {
  const counts = new Array(LOAD_STATUS.length).fill(0)
  for (const load of loads.value) counts[load.status]++
  return counts
})

const exceptionCountsBySeverity = computed(() => {
  const counts = new Array(EXCEPTION_SEVERITY.length).fill(0)
  for (const exception of openExceptions.value) counts[exception.severity]++
  return counts
})

const totalOpenExceptions = computed(() => openExceptions.value.length)

function currencyCode(currencyId: string): string {
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}

onMounted(async () => {
  try {
    const [loadList, exceptionList, currencyList, margin, exposure, payables, agedDebtors, onTimeDelivery] = await Promise.all([
      loadsApi.list(),
      exceptionsApi.list(0),
      referenceApi.currencies(),
      dashboardApi.marginSummary(),
      dashboardApi.creditExposureSummary(),
      dashboardApi.payablesSummary(),
      dashboardApi.agedDebtorsSummary(),
      dashboardApi.onTimeDeliverySummary(),
    ])
    loads.value = loadList
    openExceptions.value = exceptionList
    currencies.value = currencyList
    marginSummary.value = margin
    creditExposureSummary.value = exposure
    payablesSummary.value = payables
    agedDebtorsSummary.value = agedDebtors
    onTimeDeliverySummary.value = onTimeDelivery
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the dashboard.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Dashboard</h1>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>

    <template v-else>
      <div class="flex flex-col">
      <div :class="financeEmphasis ? 'order-2' : 'order-1'">
      <section class="mt-6">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Open exceptions</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-4">
          <RouterLink
            to="/exceptions?status=0"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ totalOpenExceptions }}</p>
            <p class="mt-1 text-sm text-slate-500">All open</p>
          </RouterLink>
          <RouterLink
            v-for="(severity, i) in EXCEPTION_SEVERITY"
            :key="severity"
            :to="`/exceptions?status=0&severity=${i}`"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold" :class="i === 2 ? 'text-rose-700' : i === 1 ? 'text-amber-700' : 'text-slate-900'">
              {{ exceptionCountsBySeverity[i] }}
            </p>
            <p class="mt-1 text-sm text-slate-500">{{ severity }}</p>
          </RouterLink>
        </div>
      </section>

      <section class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Loads by status</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-5">
          <RouterLink
            v-for="(status, i) in LOAD_STATUS"
            :key="status"
            :to="`/loads?status=${i}`"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ loadCountsByStatus[i] }}</p>
            <p class="mt-1 text-sm text-slate-500">{{ status }}</p>
          </RouterLink>
        </div>
      </section>
      </div>

      <div :class="financeEmphasis ? 'order-1' : 'order-2'">
      <section v-if="marginSummary" class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Sell/buy margin</h2>
        <div class="mt-3 max-w-md rounded-lg border border-slate-200 bg-white p-4">
          <p
            class="text-2xl font-semibold"
            :class="marginSummary.margin < 0 ? 'text-rose-700' : 'text-slate-900'"
          >
            {{ formatMoney(marginSummary.margin, currencyCode(marginSummary.reportingCurrencyId)) }}
          </p>
          <p class="mt-1 text-sm text-slate-500">
            Sell {{ formatMoney(marginSummary.sellTotal, currencyCode(marginSummary.reportingCurrencyId)) }}
            · Buy {{ formatMoney(marginSummary.buyTotal, currencyCode(marginSummary.reportingCurrencyId)) }}
          </p>
          <p v-if="marginSummary.unconverted.length" class="mt-2 text-xs text-amber-700">
            Excludes {{ marginSummary.unconverted.length }} currency amount(s) with no captured exchange rate to
            {{ currencyCode(marginSummary.reportingCurrencyId) }}.
          </p>
        </div>
      </section>

      <section v-if="creditExposureSummary && creditExposureSummary.byCurrency.length" class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Credit exposure across clients</h2>
        <RouterLink
          to="/clients"
          class="mt-3 grid max-w-2xl grid-cols-1 gap-4 rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm sm:grid-cols-2"
        >
          <div v-for="row in creditExposureSummary.byCurrency" :key="row.currencyId">
            <p class="text-2xl font-semibold text-slate-900">
              {{ formatMoney(row.totalExposure, currencyCode(row.currencyId)) }}
              <span class="text-base font-normal text-slate-400">/ {{ formatMoney(row.totalCreditLimit, currencyCode(row.currencyId)) }}</span>
            </p>
            <p class="mt-1 text-sm text-slate-500">
              Total exposure vs. credit limit ({{ currencyCode(row.currencyId) }}) — AR
              {{ formatMoney(row.totalArOutstanding, currencyCode(row.currencyId)) }}, WIP
              {{ formatMoney(row.totalWip, currencyCode(row.currencyId)) }}
            </p>
          </div>
        </RouterLink>
      </section>

      <section v-if="payablesSummary" class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Subcontractor payables</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-4">
          <RouterLink
            to="/accruals?status=0"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ payablesSummary.accrued }}</p>
            <p class="mt-1 text-sm text-slate-500">Accrued</p>
          </RouterLink>
          <RouterLink
            to="/supplier-invoices"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ payablesSummary.availableToExport }}</p>
            <p class="mt-1 text-sm text-slate-500">Available to export</p>
          </RouterLink>
          <RouterLink
            to="/supplier-invoices"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ payablesSummary.exported }}</p>
            <p class="mt-1 text-sm text-slate-500">Exported</p>
          </RouterLink>
          <RouterLink
            to="/supplier-invoices"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ payablesSummary.paid }}</p>
            <p class="mt-1 text-sm text-slate-500">Paid</p>
          </RouterLink>
        </div>
      </section>

      <section v-if="agedDebtorsSummary && agedDebtorsSummary.byCurrency.length" class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Aged debtors</h2>
        <RouterLink
          to="/clients"
          class="mt-3 grid max-w-2xl grid-cols-1 gap-4 rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm sm:grid-cols-2"
        >
          <div v-for="row in agedDebtorsSummary.byCurrency" :key="row.currencyId">
            <p class="text-2xl font-semibold text-slate-900">
              {{ formatMoney(row.totalOutstanding, currencyCode(row.currencyId)) }}
            </p>
            <p class="mt-1 text-sm text-slate-500">
              Current {{ formatMoney(row.currentAmount, currencyCode(row.currencyId)) }} · 30d {{ formatMoney(row.days30, currencyCode(row.currencyId)) }}
              · 60d {{ formatMoney(row.days60, currencyCode(row.currencyId)) }} · 90d {{ formatMoney(row.days90, currencyCode(row.currencyId)) }}
              · 90d+ {{ formatMoney(row.days90Plus, currencyCode(row.currencyId)) }}
            </p>
          </div>
        </RouterLink>
      </section>

      <section v-if="onTimeDeliverySummary" class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">On-time delivery rate</h2>
        <div class="mt-3 max-w-md rounded-lg border border-slate-200 bg-white p-4">
          <p v-if="onTimeDeliverySummary.onTimeRatePercent === null" class="text-sm text-slate-500">
            No delivered load has a promised delivery window to judge yet.
          </p>
          <template v-else>
            <p class="text-2xl font-semibold text-slate-900">{{ onTimeDeliverySummary.onTimeRatePercent }}%</p>
            <p class="mt-1 text-sm text-slate-500">
              {{ onTimeDeliverySummary.onTimeCount }} on time · {{ onTimeDeliverySummary.lateCount }} late
            </p>
          </template>
        </div>
      </section>
      </div>
      </div>
    </template>
  </AppLayout>
</template>
