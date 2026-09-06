<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { invoicesApi } from '../api/invoices'
import { clientsApi } from '../api/clients'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { INVOICE_STATUS, label, type Client, type ClientCurrency, type Currency, type Invoice } from '../api/types'
import { formatDate, formatMoney, invoiceStatusTone } from '../lib/presentation'

const router = useRouter()
const auth = useAuthStore()

const invoices = ref<Invoice[]>([])
const clients = ref<Client[]>([])
const currencies = ref<Currency[]>([])
const clientFilter = ref('')

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('finance.invoice.manage'))

function clientName(id: string): string {
  return clients.value.find((c) => c.id === id)?.name ?? id
}
function currencyCode(id: string): string {
  return currencies.value.find((c) => c.id === id)?.code ?? id
}

async function loadInvoices() {
  loading.value = true
  error.value = ''
  try {
    invoices.value = await invoicesApi.list(clientFilter.value || undefined)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load invoices.'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  try {
    const [clientList, currencyList] = await Promise.all([clientsApi.list(), referenceApi.currencies()])
    clients.value = clientList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load reference data.'
  }
  await loadInvoices()
})

// --- Generate a new invoice ---
const generateOpen = ref(false)
const generateForm = ref({ clientId: '', currencyId: '', issueDate: '' })
const clientAllowedCurrencies = ref<ClientCurrency[]>([])

const currencyOptionsForGenerate = computed(() => {
  const client = clients.value.find((c) => c.id === generateForm.value.clientId)
  if (!client) return []
  const ids = new Set([client.currencyId, ...clientAllowedCurrencies.value.map((cc) => cc.currencyId)])
  return currencies.value.filter((c) => ids.has(c.id))
})

watch(
  () => generateForm.value.clientId,
  async (clientId) => {
    generateForm.value.currencyId = ''
    clientAllowedCurrencies.value = []
    if (!clientId) return
    try {
      clientAllowedCurrencies.value = await clientsApi.currencies(clientId)
    } catch {
      // Non-fatal — the currency picker just falls back to the client's own primary.
    }
  },
)

function openGenerate() {
  generateForm.value = { clientId: '', currencyId: '', issueDate: '' }
  generateOpen.value = true
}

async function submitGenerate() {
  actionError.value = ''
  actionBusy.value = true
  try {
    const invoice = await invoicesApi.generate({
      clientId: generateForm.value.clientId,
      currencyId: generateForm.value.currencyId || undefined,
      issueDate: generateForm.value.issueDate || undefined,
    })
    generateOpen.value = false
    await router.push(`/invoices/${invoice.id}`)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not generate an invoice.'
  } finally {
    actionBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Invoices</h1>
      <button
        v-if="canManage"
        type="button"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
        @click="openGenerate"
      >
        Generate invoice
      </button>
    </div>
    <p class="mt-1 text-sm text-slate-500">Sell-side invoicing (§10.1) — one line per approved commodity line from a PodReceived load.</p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <ErrorAlert v-else-if="actionError" :message="actionError" class="mt-4" />

    <form
      v-if="generateOpen"
      class="mt-4 grid max-w-xl grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4"
      @submit.prevent="submitGenerate"
    >
      <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
        Client
        <select v-model="generateForm.clientId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select…</option>
          <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
        </select>
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Currency
        <select v-model="generateForm.currencyId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="">Client's primary</option>
          <option v-for="c in currencyOptionsForGenerate" :key="c.id" :value="c.id">{{ c.code }}</option>
        </select>
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Issue date
        <input v-model="generateForm.issueDate" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <div class="col-span-2 flex gap-3">
        <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
          {{ actionBusy ? 'Generating…' : 'Generate' }}
        </button>
        <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="generateOpen = false">Cancel</button>
      </div>
    </form>

    <div class="mt-6">
      <select v-model="clientFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="loadInvoices">
        <option value="">All clients</option>
        <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
      </select>
    </div>

    <p v-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="invoices.length === 0" class="mt-6 text-sm text-slate-500">No invoices found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Invoice no.</th>
            <th class="px-4 py-3">Client</th>
            <th class="px-4 py-3">Issue date</th>
            <th class="px-4 py-3">Due date</th>
            <th class="px-4 py-3">Total inc. VAT</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="invoice in invoices"
            :key="invoice.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/invoices/${invoice.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ invoice.invoiceNumber }}</td>
            <td class="px-4 py-3 text-slate-600">{{ clientName(invoice.clientId) }}</td>
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
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
