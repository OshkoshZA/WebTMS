<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { invoicesApi } from '../api/invoices'
import { clientsApi } from '../api/clients'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { INVOICE_STATUS, label, type Client, type Currency, type Invoice } from '../api/types'
import { formatDate, formatMoney, invoiceStatusTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const invoice = ref<Invoice | null>(null)
const clients = ref<Client[]>([])
const currencies = ref<Currency[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('finance.invoice.manage'))
const canCreateCreditNote = computed(() => auth.hasFunction('finance.creditnote.approve'))
const isDraft = computed(() => invoice.value?.status === 0)
// A credit note can only ever correct an Issued/PartPaid/Paid invoice — never a Draft
// (nothing to correct yet) or a Void one (CreditNotesController.Create's own rule).
const canBeCredited = computed(() => invoice.value !== null && [1, 2, 3].includes(invoice.value.status))
const clientName = computed(() => clients.value.find((c) => c.id === invoice.value?.clientId)?.name ?? '')
const currencyCode = computed(() => currencies.value.find((c) => c.id === invoice.value?.currencyId)?.code ?? '')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [invoiceData, clientList, currencyList] = await Promise.all([
      invoicesApi.get(props.id),
      clientsApi.list(),
      referenceApi.currencies(),
    ])
    invoice.value = invoiceData
    clients.value = clientList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this invoice.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function issue() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await invoicesApi.issue(props.id)
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

async function voidInvoice() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await invoicesApi.void(props.id)
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
          <h1 class="text-xl font-semibold text-slate-900">{{ invoice.invoiceNumber }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ clientName }}</p>
        </div>
        <div class="flex gap-2">
          <StatusBadge :text="label(INVOICE_STATUS, invoice.status)" :tone="invoiceStatusTone(invoice.status)" />
          <StatusBadge v-if="invoice.isOverdue" text="Overdue" tone="danger" />
        </div>
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Issue date</dt>
          <dd class="text-slate-900">{{ formatDate(invoice.issueDate) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Due date</dt>
          <dd class="text-slate-900">{{ formatDate(invoice.dueDate) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Total ex. VAT</dt>
          <dd class="text-slate-900">{{ formatMoney(invoice.totalExVat, currencyCode) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Total inc. VAT</dt>
          <dd class="font-medium text-slate-900">{{ formatMoney(invoice.totalIncVat, currencyCode) }}</dd>
        </div>
      </dl>

      <div v-if="canManage && isDraft" class="mt-4 flex flex-wrap gap-3">
        <button type="button" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50" @click="issue">
          {{ actionBusy ? 'Issuing…' : 'Issue' }}
        </button>
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
          @click="voidInvoice"
        >
          Void
        </button>
      </div>

      <div v-if="canCreateCreditNote && canBeCredited" class="mt-4">
        <RouterLink
          :to="`/credit-notes/new?clientId=${invoice.clientId}&invoiceId=${invoice.id}`"
          class="text-sm font-medium text-slate-700 underline hover:text-slate-900"
        >
          Raise a credit note against this invoice
        </RouterLink>
      </div>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Lines</h2>
      <div class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Description</th>
              <th class="px-4 py-3">Quantity</th>
              <th class="px-4 py-3">Rate</th>
              <th class="px-4 py-3">Amount</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="line in invoice.lines" :key="line.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-900">{{ line.description }}</td>
              <td class="px-4 py-3 text-slate-600">{{ line.quantity }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatMoney(line.rate, currencyCode) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatMoney(line.amount, currencyCode) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
