<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { invoicesApi } from '../api/invoices'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { INVOICE_STATUS, label, type Currency, type Invoice } from '../api/types'
import { formatDate, formatMoney, invoiceStatusTone } from '../lib/presentation'

const invoices = ref<Invoice[]>([])
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
    const [invoiceList, currencyList] = await Promise.all([invoicesApi.list(), referenceApi.currencies()])
    invoices.value = invoiceList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load your invoices.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Invoices</h1>
    <p class="mt-1 text-sm text-slate-500">PDF download isn't available yet — no document-rendering pipeline exists for this yet.</p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="invoices.length === 0" class="mt-6 text-sm text-slate-500">No invoices yet.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Invoice no.</th>
            <th class="px-4 py-3">Issue date</th>
            <th class="px-4 py-3">Due date</th>
            <th class="px-4 py-3">Total inc. VAT</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="invoice in invoices" :key="invoice.id">
            <tr class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50" @click="toggleExpand(invoice.id)">
              <td class="px-4 py-3 font-medium text-slate-900">{{ invoice.invoiceNumber }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDate(invoice.issueDate) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDate(invoice.dueDate) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatMoney(invoice.totalIncVat, currencyCode(invoice.currencyId)) }}</td>
              <td class="px-4 py-3">
                <div class="flex gap-2">
                  <StatusBadge :text="label(INVOICE_STATUS, invoice.status)" :tone="invoiceStatusTone(invoice.status)" />
                  <StatusBadge v-if="invoice.isOverdue" text="Overdue" tone="danger" />
                </div>
              </td>
            </tr>
            <tr v-if="expandedId === invoice.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
              <td colspan="5" class="px-4 py-3">
                <table class="w-full text-left text-sm">
                  <thead class="text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                      <th class="py-1 pr-4">Description</th>
                      <th class="py-1 pr-4">Quantity</th>
                      <th class="py-1 pr-4">Rate</th>
                      <th class="py-1">Amount</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="line in invoice.lines" :key="line.id">
                      <td class="py-1 pr-4 text-slate-700">{{ line.description }}</td>
                      <td class="py-1 pr-4 text-slate-600">{{ line.quantity }}</td>
                      <td class="py-1 pr-4 text-slate-600">{{ formatMoney(line.rate, currencyCode(invoice.currencyId)) }}</td>
                      <td class="py-1 text-slate-600">{{ formatMoney(line.amount, currencyCode(invoice.currencyId)) }}</td>
                    </tr>
                  </tbody>
                </table>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
