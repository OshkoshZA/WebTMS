<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { creditNotesApi } from '../api/creditNotes'
import { clientsApi } from '../api/clients'
import { invoicesApi } from '../api/invoices'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { Client, ClientCurrency, Currency, Invoice } from '../api/types'
import { formatMoney } from '../lib/presentation'

const route = useRoute()
const router = useRouter()

const clients = ref<Client[]>([])
const currencies = ref<Currency[]>([])
const clientAllowedCurrencies = ref<ClientCurrency[]>([])
const clientInvoices = ref<Invoice[]>([])

const loadingReferenceData = ref(true)
const error = ref('')
const submitting = ref(false)

const mode = ref<'correct' | 'standalone'>('standalone')
const clientId = ref('')
const reason = ref('')
const issueDate = ref('')
const currencyId = ref('')

// --- Correct-an-invoice mode ---
const selectedInvoiceId = ref('')
const selectedInvoice = computed(() => clientInvoices.value.find((i) => i.id === selectedInvoiceId.value) ?? null)
// Amount to credit per InvoiceLineId — blank/zero means that line isn't included.
// The API itself caps each line at what's left uncredited; this form doesn't
// pre-compute that cap (it would need every existing credit note's own lines summed
// per line first) and just surfaces the API's own rejection if a cap is exceeded.
const lineAmounts = ref<Record<string, number>>({})

// --- Standalone mode ---
const standaloneLines = ref<{ description: string; amount: number }[]>([{ description: '', amount: 0 }])

function addStandaloneLine() {
  standaloneLines.value.push({ description: '', amount: 0 })
}
function removeStandaloneLine(index: number) {
  standaloneLines.value.splice(index, 1)
}

const currencyOptions = computed(() => {
  const client = clients.value.find((c) => c.id === clientId.value)
  if (!client) return []
  const ids = new Set([client.currencyId, ...clientAllowedCurrencies.value.map((cc) => cc.currencyId)])
  return currencies.value.filter((c) => ids.has(c.id))
})

function currencyCode(id: string): string {
  return currencies.value.find((c) => c.id === id)?.code ?? id
}

watch(clientId, async (id) => {
  currencyId.value = ''
  clientAllowedCurrencies.value = []
  selectedInvoiceId.value = ''
  clientInvoices.value = []
  lineAmounts.value = {}
  if (!id) return
  try {
    const [allowedCurrencies, invoices] = await Promise.all([clientsApi.currencies(id), invoicesApi.list(id)])
    clientAllowedCurrencies.value = allowedCurrencies
    // Only an Issued/PartPaid/Paid invoice can be credited — never Draft (nothing to
    // correct yet) or Void — matches CreditNotesController.Create's own rule.
    clientInvoices.value = invoices.filter((i) => [1, 2, 3].includes(i.status))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Could not load this client's invoices/currencies."
  }
})

onMounted(async () => {
  try {
    const [clientList, currencyList] = await Promise.all([clientsApi.list(), referenceApi.currencies()])
    clients.value = clientList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load reference data.'
  } finally {
    loadingReferenceData.value = false
  }

  const prefillClientId = route.query.clientId
  const prefillInvoiceId = route.query.invoiceId
  if (typeof prefillClientId === 'string') {
    clientId.value = prefillClientId
    mode.value = 'correct'
    // Wait for the clientId watcher above to finish loading this client's invoices
    // before trying to preselect one.
    await new Promise((resolve) => setTimeout(resolve, 0))
  }
  if (typeof prefillInvoiceId === 'string') selectedInvoiceId.value = prefillInvoiceId
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const lines =
      mode.value === 'correct'
        ? Object.entries(lineAmounts.value)
            .filter(([, amount]) => amount > 0)
            .map(([invoiceLineId, amount]) => ({
              invoiceLineId,
              description: selectedInvoice.value?.lines.find((l) => l.id === invoiceLineId)?.description ?? 'Correction',
              amount,
            }))
        : standaloneLines.value.filter((l) => l.amount > 0).map((l) => ({ description: l.description, amount: l.amount }))

    if (lines.length === 0) {
      error.value = 'At least one line with a positive amount is required.'
      return
    }

    const creditNote = await creditNotesApi.create({
      clientId: clientId.value,
      originalInvoiceId: mode.value === 'correct' ? selectedInvoiceId.value : undefined,
      reason: reason.value,
      currencyId: mode.value === 'standalone' ? currencyId.value || undefined : undefined,
      lines,
      issueDate: issueDate.value || undefined,
    })
    await router.push(`/credit-notes/${creditNote.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New credit note</h1>

    <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form v-else class="mt-6 flex max-w-2xl flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Client
        <select v-model="clientId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select…</option>
          <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
        </select>
      </label>

      <div class="flex gap-4 text-sm text-slate-700">
        <label class="flex items-center gap-2">
          <input v-model="mode" type="radio" value="correct" class="h-4 w-4" />
          Correct an invoice
        </label>
        <label class="flex items-center gap-2">
          <input v-model="mode" type="radio" value="standalone" class="h-4 w-4" />
          Standalone note
        </label>
      </div>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Reason
        <input v-model="reason" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <template v-if="mode === 'correct'">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Invoice
          <select v-model="selectedInvoiceId" required :disabled="!clientId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="inv in clientInvoices" :key="inv.id" :value="inv.id">{{ inv.invoiceNumber }}</option>
          </select>
        </label>
        <p v-if="clientId && clientInvoices.length === 0" class="text-xs text-slate-500">
          This client has no Issued/PartPaid/Paid invoice to correct.
        </p>

        <div v-if="selectedInvoice" class="rounded-lg border border-slate-200 bg-white p-4">
          <p class="text-xs font-semibold uppercase tracking-wide text-slate-500">Lines to credit</p>
          <div v-for="line in selectedInvoice.lines" :key="line.id" class="mt-3 flex items-center justify-between gap-4 text-sm">
            <span class="text-slate-700">
              {{ line.description }} — {{ formatMoney(line.amount, currencyCode(selectedInvoice.currencyId)) }} original
            </span>
            <input
              v-model.number="lineAmounts[line.id]"
              type="number"
              min="0"
              step="0.01"
              placeholder="0.00"
              class="w-32 rounded-md border border-slate-300 px-2 py-1 text-sm"
            />
          </div>
        </div>
      </template>

      <template v-else>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Currency
          <select v-model="currencyId" required :disabled="!clientId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="c in currencyOptions" :key="c.id" :value="c.id">{{ c.code }}</option>
          </select>
        </label>

        <div class="rounded-lg border border-slate-200 bg-white p-4">
          <p class="text-xs font-semibold uppercase tracking-wide text-slate-500">Lines</p>
          <div v-for="(line, index) in standaloneLines" :key="index" class="mt-3 flex items-end gap-3">
            <label class="flex flex-1 flex-col gap-1 text-sm text-slate-700">
              Description
              <input v-model="line.description" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
            </label>
            <label class="flex w-32 flex-col gap-1 text-sm text-slate-700">
              Amount
              <input v-model.number="line.amount" type="number" min="0.01" step="0.01" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
            </label>
            <button
              v-if="standaloneLines.length > 1"
              type="button"
              class="rounded-md border border-slate-300 px-2.5 py-2 text-xs text-slate-600 hover:bg-slate-100"
              @click="removeStandaloneLine(index)"
            >
              Remove
            </button>
          </div>
          <button type="button" class="mt-3 text-sm font-medium text-slate-700 underline" @click="addStandaloneLine">Add line</button>
        </div>
      </template>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Issue date
        <input v-model="issueDate" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : 'Create credit note' }}
        </button>
        <RouterLink to="/credit-notes" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
