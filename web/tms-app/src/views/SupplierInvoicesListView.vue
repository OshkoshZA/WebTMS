<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { supplierInvoicesApi } from '../api/supplierInvoices'
import { subcontractorsApi } from '../api/subcontractors'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { SUPPLIER_INVOICE_STATUS, label, type Currency, type Subcontractor, type SupplierInvoice } from '../api/types'
import { formatDate, formatMoney, supplierInvoiceStatusTone } from '../lib/presentation'

const router = useRouter()
const auth = useAuthStore()

const invoices = ref<SupplierInvoice[]>([])
const subcontractors = ref<Subcontractor[]>([])
const currencies = ref<Currency[]>([])
const subcontractorFilter = ref('')

const loading = ref(true)
const error = ref('')

const canManage = computed(() => auth.hasFunction('finance.subcontractorinvoice.process'))

function subcontractorName(id: string): string {
  return subcontractors.value.find((s) => s.id === id)?.name ?? id
}
function currencyCode(id: string): string {
  return currencies.value.find((c) => c.id === id)?.code ?? id
}

async function loadInvoices() {
  loading.value = true
  error.value = ''
  try {
    invoices.value = await supplierInvoicesApi.list(subcontractorFilter.value || undefined)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load supplier invoices.'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  try {
    const [subList, currencyList] = await Promise.all([subcontractorsApi.list(), referenceApi.currencies()])
    subcontractors.value = subList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load reference data.'
  }
  await loadInvoices()
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Supplier invoices</h1>
      <RouterLink
        v-if="canManage"
        to="/supplier-invoices/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        Capture invoice
      </RouterLink>
    </div>
    <p class="mt-1 text-sm text-slate-500">Buy-side payables (§10.2) — a subcontractor's own invoice, matched against its open accruals.</p>

    <div class="mt-6">
      <select v-model="subcontractorFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="loadInvoices">
        <option value="">All subcontractors</option>
        <option v-for="s in subcontractors" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="invoices.length === 0" class="mt-6 text-sm text-slate-500">No supplier invoices found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Invoice no.</th>
            <th class="px-4 py-3">Subcontractor</th>
            <th class="px-4 py-3">Received date</th>
            <th class="px-4 py-3">Amount</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="invoice in invoices"
            :key="invoice.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/supplier-invoices/${invoice.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ invoice.supplierInvoiceNumber }}</td>
            <td class="px-4 py-3 text-slate-600">{{ subcontractorName(invoice.subcontractorId) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDate(invoice.receivedDate) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatMoney(invoice.amount, currencyCode(invoice.currencyId)) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(SUPPLIER_INVOICE_STATUS, invoice.status)" :tone="supplierInvoiceStatusTone(invoice.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
