<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { supplierInvoicesApi } from '../api/supplierInvoices'
import { accrualsApi } from '../api/accruals'
import { subcontractorsApi } from '../api/subcontractors'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  SUBCONTRACTOR_EXPENSE_STATUS, SUPPLIER_INVOICE_STATUS, label,
  type Currency, type Subcontractor, type SubcontractorAccrual, type SupplierInvoice,
} from '../api/types'
import { formatDate, formatDateTime, formatMoney, subcontractorExpenseStatusTone, supplierInvoiceStatusTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const invoice = ref<SupplierInvoice | null>(null)
const subcontractors = ref<Subcontractor[]>([])
const currencies = ref<Currency[]>([])
const matchableAccruals = ref<SubcontractorAccrual[]>([])
const selectedAccrualIds = ref<string[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)
const varianceResult = ref<number | null>(null)

const canManage = computed(() => auth.hasFunction('finance.subcontractorinvoice.process'))
const isReceived = computed(() => invoice.value?.status === 0)
const subcontractorName = computed(() => subcontractors.value.find((s) => s.id === invoice.value?.subcontractorId)?.name ?? '')
const currencyCode = computed(() => currencies.value.find((c) => c.id === invoice.value?.currencyId)?.code ?? '')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [invoiceData, subList, currencyList] = await Promise.all([
      supplierInvoicesApi.get(props.id),
      subcontractorsApi.list(),
      referenceApi.currencies(),
    ])
    invoice.value = invoiceData
    subcontractors.value = subList
    currencies.value = currencyList

    if (invoiceData.status === 0) {
      // Only Accrued accruals for this same subcontractor and currency can ever be
      // matched — AccrualsController itself has no currency filter, so that half is
      // done client-side.
      const accruals = await accrualsApi.list(invoiceData.subcontractorId, 0)
      matchableAccruals.value = accruals.filter((a) => a.currencyId === invoiceData.currencyId)
    }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this supplier invoice.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function match() {
  actionError.value = ''
  actionBusy.value = true
  try {
    const result = await supplierInvoicesApi.match(props.id, selectedAccrualIds.value)
    invoice.value = result.invoice
    varianceResult.value = result.varianceAmount
    selectedAccrualIds.value = []
    matchableAccruals.value = []
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// --- Dispute ---
const disputeOpen = ref(false)
const disputeReason = ref('')

async function submitDispute() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await supplierInvoicesApi.dispute(props.id, disputeReason.value)
    disputeOpen.value = false
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

    <template v-else-if="invoice">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ invoice.supplierInvoiceNumber }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ subcontractorName }}</p>
        </div>
        <StatusBadge :text="label(SUPPLIER_INVOICE_STATUS, invoice.status)" :tone="supplierInvoiceStatusTone(invoice.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />
      <p v-if="invoice.disputeReason" class="mt-3 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-800">Disputed: {{ invoice.disputeReason }}</p>
      <p v-if="varianceResult !== null && varianceResult !== 0" class="mt-3 rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
        Matched with a variance of {{ formatMoney(varianceResult, currencyCode) }} against the accrued estimate — flagged for review.
      </p>
      <p v-else-if="varianceResult === 0" class="mt-3 rounded-md bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
        Matched exactly — no variance against the accrued estimate.
      </p>

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Invoice date</dt>
          <dd class="text-slate-900">{{ formatDate(invoice.invoiceDate) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Received date</dt>
          <dd class="text-slate-900">{{ formatDate(invoice.receivedDate) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Amount</dt>
          <dd class="font-medium text-slate-900">{{ formatMoney(invoice.amount, currencyCode) }}</dd>
        </div>
      </dl>

      <template v-if="canManage && isReceived">
        <div v-if="!disputeOpen" class="mt-4 flex flex-wrap gap-3">
          <button type="button" class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100" @click="disputeOpen = true">
            Dispute
          </button>
        </div>
        <form v-else class="mt-4 flex max-w-lg flex-col gap-3 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitDispute">
          <label class="flex flex-col gap-1 text-sm text-slate-700">
            Dispute reason
            <input v-model="disputeReason" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
          </label>
          <div class="flex gap-3">
            <button type="submit" :disabled="actionBusy" class="rounded-md bg-rose-800 px-3 py-1.5 text-sm text-white disabled:opacity-50">
              {{ actionBusy ? 'Disputing…' : 'Dispute' }}
            </button>
            <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="disputeOpen = false">Cancel</button>
          </div>
        </form>

        <h2 class="mt-8 text-lg font-semibold text-slate-900">Match against accruals</h2>
        <p v-if="matchableAccruals.length === 0" class="mt-3 text-sm text-slate-500">
          No Accrued accrual for this subcontractor in {{ currencyCode }} is available to match.
        </p>
        <template v-else>
          <div class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
            <table class="w-full text-left text-sm">
              <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                <tr>
                  <th class="px-4 py-3"></th>
                  <th class="px-4 py-3">Accrual date</th>
                  <th class="px-4 py-3">Estimated amount</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="accrual in matchableAccruals" :key="accrual.id" class="border-b border-slate-100 last:border-0">
                  <td class="px-4 py-3">
                    <input v-model="selectedAccrualIds" type="checkbox" :value="accrual.id" class="h-4 w-4 rounded border-slate-300" />
                  </td>
                  <td class="px-4 py-3 text-slate-600">{{ formatDate(accrual.accrualDate) }}</td>
                  <td class="px-4 py-3 text-slate-600">{{ formatMoney(accrual.estimatedAmount, currencyCode) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <button
            type="button"
            :disabled="actionBusy || selectedAccrualIds.length === 0"
            class="mt-3 rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
            @click="match"
          >
            {{ actionBusy ? 'Matching…' : 'Match selected' }}
          </button>
        </template>
      </template>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Finalized expenses</h2>
      <p v-if="invoice.expenses.length === 0" class="mt-3 text-sm text-slate-500">No expenses finalized yet.</p>
      <div v-else class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Amount</th>
              <th class="px-4 py-3">Status</th>
              <th class="px-4 py-3">Finalized</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="expense in invoice.expenses" :key="expense.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-900">{{ formatMoney(expense.amount, currencyCode) }}</td>
              <td class="px-4 py-3">
                <StatusBadge :text="label(SUBCONTRACTOR_EXPENSE_STATUS, expense.status)" :tone="subcontractorExpenseStatusTone(expense.status)" />
              </td>
              <td class="px-4 py-3 text-slate-600">{{ formatDateTime(expense.finalizedDate) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
