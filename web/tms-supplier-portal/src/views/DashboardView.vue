<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { legsApi } from '../api/legs'
import { accrualsApi } from '../api/accruals'
import { supplierInvoicesApi } from '../api/supplierInvoices'
import { exceptionsApi } from '../api/exceptions'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  EXCEPTION_SEVERITY,
  SUBCONTRACTOR_EXPENSE_STATUS,
  type ExceptionRecord,
  type SubcontractorAccrual,
  type SubcontractorLeg,
  type SupplierInvoice,
} from '../api/types'

const auth = useAuthStore()

const legs = ref<SubcontractorLeg[]>([])
const accruals = ref<SubcontractorAccrual[]>([])
const supplierInvoices = ref<SupplierInvoice[]>([])
const openExceptions = ref<ExceptionRecord[]>([])
const loading = ref(true)
const error = ref('')

// "When will I get paid" (§13.3/§16.4) spans two entities this portal already fetches
// on separate screens — an accrual is either still Accrued or has been Netted into a
// finalized SubcontractorExpense (nested under a matched SupplierInvoice), so the full
// pipeline is a combined tally over both, computed client-side, no new endpoint.
const accruedCount = computed(() => accruals.value.filter((a) => a.status === 0).length)
const expenseCountsByStatus = computed(() => {
  const counts = new Array(SUBCONTRACTOR_EXPENSE_STATUS.length).fill(0)
  for (const invoice of supplierInvoices.value) {
    for (const expense of invoice.expenses) counts[expense.status]++
  }
  return counts
})

const legsAwaitingAcknowledgement = computed(() => legs.value.filter((l) => l.confirmation?.status === 0).length)
const legsAwaitingPod = computed(() => legs.value.filter((l) => l.status === 3).length)

const exceptionCountsBySeverity = computed(() => {
  const counts = new Array(EXCEPTION_SEVERITY.length).fill(0)
  for (const exception of openExceptions.value) counts[exception.severity]++
  return counts
})
const totalOpenExceptions = computed(() => openExceptions.value.length)

onMounted(async () => {
  try {
    const [legList, accrualList, invoiceList, exceptionList] = await Promise.all([
      legsApi.listForSubcontractor(auth.subcontractorId),
      accrualsApi.list(),
      supplierInvoicesApi.list(),
      exceptionsApi.list(0),
    ])
    legs.value = legList
    accruals.value = accrualList
    supplierInvoices.value = invoiceList
    openExceptions.value = exceptionList
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
      <section class="mt-6">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Needs your action</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-4">
          <RouterLink
            to="/legs?confirmationStatus=0"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ legsAwaitingAcknowledgement }}</p>
            <p class="mt-1 text-sm text-slate-500">Awaiting acknowledgement</p>
          </RouterLink>
          <RouterLink
            to="/legs?status=3"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ legsAwaitingPod }}</p>
            <p class="mt-1 text-sm text-slate-500">Delivered — POD pending</p>
          </RouterLink>
        </div>
      </section>

      <section class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">When will I get paid</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-4">
          <RouterLink to="/accruals" class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm">
            <p class="text-2xl font-semibold text-slate-900">{{ accruedCount }}</p>
            <p class="mt-1 text-sm text-slate-500">Accrued</p>
          </RouterLink>
          <RouterLink
            v-for="(status, i) in SUBCONTRACTOR_EXPENSE_STATUS"
            :key="status"
            to="/supplier-invoices"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ expenseCountsByStatus[i] }}</p>
            <p class="mt-1 text-sm text-slate-500">{{ status }}</p>
          </RouterLink>
        </div>
      </section>

      <section class="mt-8">
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
    </template>
  </AppLayout>
</template>
