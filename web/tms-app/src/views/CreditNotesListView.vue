<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { creditNotesApi } from '../api/creditNotes'
import { clientsApi } from '../api/clients'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { CREDIT_NOTE_STATUS, label, type Client, type CreditNote, type Currency } from '../api/types'
import { creditNoteStatusTone, formatDate, formatMoney } from '../lib/presentation'

const router = useRouter()
const auth = useAuthStore()

const creditNotes = ref<CreditNote[]>([])
const clients = ref<Client[]>([])
const currencies = ref<Currency[]>([])
const clientFilter = ref('')

const loading = ref(true)
const error = ref('')

const canManage = computed(() => auth.hasFunction('finance.creditnote.approve'))

function clientName(id: string): string {
  return clients.value.find((c) => c.id === id)?.name ?? id
}
function currencyCode(id: string): string {
  return currencies.value.find((c) => c.id === id)?.code ?? id
}

async function loadCreditNotes() {
  loading.value = true
  error.value = ''
  try {
    creditNotes.value = await creditNotesApi.list(clientFilter.value || undefined)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load credit notes.'
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
  await loadCreditNotes()
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Credit notes</h1>
      <RouterLink
        v-if="canManage"
        to="/credit-notes/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New credit note
      </RouterLink>
    </div>
    <p class="mt-1 text-sm text-slate-500">Sell-side adjustments (§10.1) — correcting an issued invoice, or a standalone note.</p>

    <div class="mt-6">
      <select v-model="clientFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="loadCreditNotes">
        <option value="">All clients</option>
        <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="creditNotes.length === 0" class="mt-6 text-sm text-slate-500">No credit notes found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Credit note no.</th>
            <th class="px-4 py-3">Client</th>
            <th class="px-4 py-3">Issue date</th>
            <th class="px-4 py-3">Reason</th>
            <th class="px-4 py-3">Total</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="creditNote in creditNotes"
            :key="creditNote.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/credit-notes/${creditNote.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ creditNote.creditNoteNumber }}</td>
            <td class="px-4 py-3 text-slate-600">{{ clientName(creditNote.clientId) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDate(creditNote.issueDate) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ creditNote.reason }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatMoney(creditNote.totalAmount, currencyCode(creditNote.currencyId)) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(CREDIT_NOTE_STATUS, creditNote.status)" :tone="creditNoteStatusTone(creditNote.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
