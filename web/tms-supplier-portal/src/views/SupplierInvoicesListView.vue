<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { supplierInvoicesApi } from '../api/supplierInvoices'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { SUBCONTRACTOR_EXPENSE_STATUS, SUPPLIER_INVOICE_STATUS, label, type Currency, type SupplierInvoice } from '../api/types'
import { formatDate, formatMoney, supplierInvoiceStatusTone } from '../lib/presentation'

const invoices = ref<SupplierInvoice[]>([])
const currencies = ref<Currency[]>([])
const loading = ref(true)
const error = ref('')
const expandedId = ref<string | null>(null)

function currencyCode(currencyId: string): string {
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}

function toggleExpand(id: string) {
  expandedId.value = expandedId.value === id ? null : id
}

onMounted(async () => {
  try {
    const [invoiceList, currencyList] = await Promise.all([supplierInvoicesApi.list(), referenceApi.currencies()])
    invoices.value = invoiceList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load your supplier invoices.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Supplier invoices</h1>
    <p class="mt-1 text-sm text-slate-500">
      Read-only — invoices are captured and matched to accruals by our finance team, not submitted from here.
    </p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="invoices.length === 0" class="mt-6 text-sm text-slate-500">No supplier invoices yet.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Invoice no.</th>
            <th class="px-4 py-3">Invoice date</th>
            <th class="px-4 py-3">Received</th>
            <th class="px-4 py-3">Amount</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="invoice in invoices" :key="invoice.id">
            <tr class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50" @click="toggleExpand(invoice.id)">
              <td class="px-4 py-3 font-medium text-slate-900">{{ invoice.supplierInvoiceNumber }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDate(invoice.invoiceDate) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDate(invoice.receivedDate) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatMoney(invoice.amount, currencyCode(invoice.currencyId)) }}</td>
              <td class="px-4 py-3">
                <div class="flex flex-col gap-1">
                  <StatusBadge
                    :text="label(SUPPLIER_INVOICE_STATUS, invoice.status)"
                    :tone="supplierInvoiceStatusTone(invoice.status)"
                  />
                  <span v-if="invoice.disputeReason" class="text-xs text-rose-700">{{ invoice.disputeReason }}</span>
                </div>
              </td>
            </tr>
            <tr v-if="expandedId === invoice.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
              <td colspan="5" class="px-4 py-3">
                <table v-if="invoice.expenses.length" class="w-full text-left text-sm">
                  <thead class="text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                      <th class="py-1 pr-4">Amount</th>
                      <th class="py-1 pr-4">Status</th>
                      <th class="py-1">Finalized</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="expense in invoice.expenses" :key="expense.id">
                      <td class="py-1 pr-4 text-slate-700">{{ formatMoney(expense.amount, currencyCode(invoice.currencyId)) }}</td>
                      <td class="py-1 pr-4 text-slate-600">{{ label(SUBCONTRACTOR_EXPENSE_STATUS, expense.status) }}</td>
                      <td class="py-1 text-slate-600">{{ formatDate(expense.finalizedDate) }}</td>
                    </tr>
                  </tbody>
                </table>
                <p v-else class="text-sm text-slate-500">No matched expense lines yet.</p>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
