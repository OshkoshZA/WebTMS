<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { clientsApi } from '../api/clients'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  ACTIVE_DEACTIVATED, CREDIT_NOTE_STATUS, INVOICE_STATUS, label,
  type Client, type ClientCurrency, type CreditNote, type CreditStatus, type Currency, type Invoice,
} from '../api/types'
import { activeDeactivatedTone, creditNoteStatusTone, formatDate, formatMoney, invoiceStatusTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const client = ref<Client | null>(null)
const currencies = ref<Currency[]>([])
const clientCurrencies = ref<ClientCurrency[]>([])
const creditStatus = ref<CreditStatus | null>(null)
const invoices = ref<Invoice[]>([])
const creditNotes = ref<CreditNote[]>([])
const expandedInvoiceId = ref<string | null>(null)
const expandedCreditNoteId = ref<string | null>(null)

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('client.master.manage'))
const canChangeCurrency = computed(() => auth.hasFunction('client.currency.change'))

function currencyCode(currencyId: string): string {
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}
const primaryCurrencyCode = computed(() => (client.value ? currencyCode(client.value.currencyId) : ''))

// Every currency not already the primary and not already on the client's own
// allow-list — AddCurrency's own duplicate checks, mirrored here so the dropdown
// never even offers a choice the API would reject.
const availableCurrenciesToAdd = computed(() => {
  if (!client.value) return []
  const taken = new Set([client.value.currencyId, ...clientCurrencies.value.map((cc) => cc.currencyId)])
  return currencies.value.filter((c) => !taken.has(c.id))
})

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [clientData, currencyList] = await Promise.all([clientsApi.get(props.id), referenceApi.currencies()])
    client.value = clientData
    currencies.value = currencyList
    const [statusData, currencyRows, invoiceList, creditNoteList] = await Promise.all([
      clientsApi.creditStatus(props.id),
      clientsApi.currencies(props.id),
      clientsApi.invoices(props.id),
      clientsApi.creditNotes(props.id),
    ])
    creditStatus.value = statusData
    clientCurrencies.value = currencyRows
    invoices.value = invoiceList
    creditNotes.value = creditNoteList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this client.'
  } finally {
    loading.value = false
  }
}

onMounted(loadEverything)

async function runAction(action: () => Promise<void>) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await action()
    await loadEverything()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// --- Edit core fields ---
const editOpen = ref(false)
const editForm = ref({ name: '', registrationNo: '', creditLimit: 0, paymentTermsDays: 30 })

function openEdit() {
  if (!client.value) return
  editForm.value = {
    name: client.value.name,
    registrationNo: client.value.registrationNo,
    creditLimit: client.value.creditLimit,
    paymentTermsDays: client.value.paymentTermsDays,
  }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await clientsApi.update(props.id, editForm.value)
    editOpen.value = false
  })
}

function toggleActive() {
  if (!client.value) return
  runAction(() => (client.value!.status === 0 ? clientsApi.deactivate(props.id) : clientsApi.reactivate(props.id)))
}

// --- Add currency ---
const addCurrencyOpen = ref(false)
const newCurrency = ref({ currencyId: '', creditLimit: 0 })

function openAddCurrency() {
  newCurrency.value = { currencyId: '', creditLimit: 0 }
  addCurrencyOpen.value = true
}

function submitAddCurrency() {
  runAction(async () => {
    await clientsApi.addCurrency(props.id, newCurrency.value.currencyId, newCurrency.value.creditLimit)
    addCurrencyOpen.value = false
  })
}

// --- Edit one currency's limit (one row open at a time) ---
const editingCurrencyId = ref<string | null>(null)
const editingCreditLimit = ref(0)

function openEditCurrency(cc: ClientCurrency) {
  editingCurrencyId.value = cc.currencyId
  editingCreditLimit.value = cc.creditLimit
}

function submitEditCurrency(currencyId: string) {
  runAction(async () => {
    await clientsApi.updateCurrency(props.id, currencyId, editingCreditLimit.value)
    editingCurrencyId.value = null
  })
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="client">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ client.name }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ client.registrationNo }}</p>
        </div>
        <StatusBadge :text="label(ACTIVE_DEACTIVATED, client.status)" :tone="activeDeactivatedTone(client.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Primary currency</dt>
          <dd class="text-slate-900">{{ primaryCurrencyCode }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Credit limit</dt>
          <dd class="text-slate-900">{{ formatMoney(client.creditLimit, primaryCurrencyCode) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Payment terms</dt>
          <dd class="text-slate-900">{{ client.paymentTermsDays }} days</dd>
        </div>
      </dl>

      <div v-if="canManage" class="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="openEdit"
        >
          Edit details
        </button>
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border px-3 py-1.5 text-sm font-medium disabled:opacity-50"
          :class="client.status === 0 ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ client.status === 0 ? 'Deactivate' : 'Reactivate' }}
        </button>
      </div>

      <form v-if="editOpen" class="mt-3 grid max-w-lg grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Name
          <input v-model="editForm.name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Registration no.
          <input v-model="editForm.registrationNo" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Credit limit
          <input v-model.number="editForm.creditLimit" type="number" min="0" step="0.01" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Payment terms (days)
          <input v-model.number="editForm.paymentTermsDays" type="number" min="0" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <div class="col-span-2 flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Save</button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editOpen = false">Cancel</button>
        </div>
      </form>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Credit status</h2>
      <dl v-if="creditStatus" class="mt-3 grid grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">AR outstanding</dt>
          <dd class="text-slate-900">{{ formatMoney(creditStatus.arOutstanding, primaryCurrencyCode) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">WIP</dt>
          <dd class="text-slate-900">{{ formatMoney(creditStatus.wip, primaryCurrencyCode) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Total exposure</dt>
          <dd class="text-slate-900">{{ formatMoney(creditStatus.totalExposure, primaryCurrencyCode) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Available credit</dt>
          <dd class="font-medium" :class="creditStatus.availableCredit < 0 ? 'text-rose-700' : 'text-slate-900'">
            {{ formatMoney(creditStatus.availableCredit, primaryCurrencyCode) }}
          </dd>
        </div>
      </dl>

      <div class="mt-8 flex items-center justify-between">
        <h2 class="text-lg font-semibold text-slate-900">Additional currencies</h2>
        <button
          v-if="canChangeCurrency"
          type="button"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-100"
          @click="openAddCurrency"
        >
          Add currency
        </button>
      </div>

      <form
        v-if="addCurrencyOpen"
        class="mt-3 flex max-w-xl flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white p-4"
        @submit.prevent="submitAddCurrency"
      >
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Currency
          <select v-model="newCurrency.currencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="c in availableCurrenciesToAdd" :key="c.id" :value="c.id">{{ c.code }} — {{ c.name }}</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Credit limit
          <input v-model.number="newCurrency.creditLimit" type="number" min="0" step="0.01" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Add</button>
        <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="addCurrencyOpen = false">Cancel</button>
      </form>

      <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Currency</th>
              <th class="px-4 py-3">Credit limit</th>
              <th v-if="canChangeCurrency" class="px-4 py-3">Action</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="cc in clientCurrencies" :key="cc.id">
              <tr class="border-b border-slate-100 last:border-0">
                <td class="px-4 py-3 text-slate-900">{{ currencyCode(cc.currencyId) }}</td>
                <td class="px-4 py-3 text-slate-600">{{ formatMoney(cc.creditLimit, currencyCode(cc.currencyId)) }}</td>
                <td v-if="canChangeCurrency" class="px-4 py-3">
                  <button
                    type="button"
                    class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100"
                    @click="openEditCurrency(cc)"
                  >
                    Edit limit
                  </button>
                </td>
              </tr>
              <tr v-if="editingCurrencyId === cc.currencyId" class="border-b border-slate-100 bg-slate-50 last:border-0">
                <td :colspan="canChangeCurrency ? 3 : 2" class="px-4 py-3">
                  <form class="flex items-end gap-3" @submit.prevent="submitEditCurrency(cc.currencyId)">
                    <label class="flex flex-col gap-1 text-sm text-slate-700">
                      Credit limit
                      <input v-model.number="editingCreditLimit" type="number" min="0" step="0.01" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
                    </label>
                    <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Save</button>
                    <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editingCurrencyId = null">Cancel</button>
                  </form>
                </td>
              </tr>
            </template>
            <tr v-if="clientCurrencies.length === 0">
              <td :colspan="canChangeCurrency ? 3 : 2" class="px-4 py-6 text-center text-slate-500">No additional currencies.</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Invoices</h2>
      <p v-if="invoices.length === 0" class="mt-3 text-sm text-slate-500">No invoices yet.</p>
      <div v-else class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
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
              <tr
                class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
                @click="expandedInvoiceId = expandedInvoiceId === invoice.id ? null : invoice.id"
              >
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
              <tr v-if="expandedInvoiceId === invoice.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
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

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Credit notes</h2>
      <p v-if="creditNotes.length === 0" class="mt-3 text-sm text-slate-500">No credit notes yet.</p>
      <div v-else class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Credit note no.</th>
              <th class="px-4 py-3">Issue date</th>
              <th class="px-4 py-3">Reason</th>
              <th class="px-4 py-3">Total</th>
              <th class="px-4 py-3">Status</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="creditNote in creditNotes" :key="creditNote.id">
              <tr
                class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
                @click="expandedCreditNoteId = expandedCreditNoteId === creditNote.id ? null : creditNote.id"
              >
                <td class="px-4 py-3 font-medium text-slate-900">{{ creditNote.creditNoteNumber }}</td>
                <td class="px-4 py-3 text-slate-600">{{ formatDate(creditNote.issueDate) }}</td>
                <td class="px-4 py-3 text-slate-600">{{ creditNote.reason }}</td>
                <td class="px-4 py-3 text-slate-600">{{ formatMoney(creditNote.totalAmount, currencyCode(creditNote.currencyId)) }}</td>
                <td class="px-4 py-3">
                  <StatusBadge :text="label(CREDIT_NOTE_STATUS, creditNote.status)" :tone="creditNoteStatusTone(creditNote.status)" />
                </td>
              </tr>
              <tr v-if="expandedCreditNoteId === creditNote.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
                <td colspan="5" class="px-4 py-3">
                  <table class="w-full text-left text-sm">
                    <thead class="text-xs uppercase tracking-wide text-slate-500">
                      <tr>
                        <th class="py-1 pr-4">Description</th>
                        <th class="py-1">Amount</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="line in creditNote.lines" :key="line.id">
                        <td class="py-1 pr-4 text-slate-700">{{ line.description }}</td>
                        <td class="py-1 text-slate-600">{{ formatMoney(line.amount, currencyCode(creditNote.currencyId)) }}</td>
                      </tr>
                    </tbody>
                  </table>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
