<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { creditNotesApi } from '../api/creditNotes'
import { clientsApi } from '../api/clients'
import { referenceApi } from '../api/reference'
import { ApiError, downloadFile } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { CREDIT_NOTE_STATUS, label, type Client, type CreditNote, type Currency } from '../api/types'
import { creditNoteStatusTone, formatDate, formatMoney } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const creditNote = ref<CreditNote | null>(null)
const clients = ref<Client[]>([])
const currencies = ref<Currency[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('finance.creditnote.approve'))
const isDraft = computed(() => creditNote.value?.status === 0)
const clientName = computed(() => clients.value.find((c) => c.id === creditNote.value?.clientId)?.name ?? '')
const currencyCode = computed(() => currencies.value.find((c) => c.id === creditNote.value?.currencyId)?.code ?? '')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [creditNoteData, clientList, currencyList] = await Promise.all([
      creditNotesApi.get(props.id),
      clientsApi.list(),
      referenceApi.currencies(),
    ])
    creditNote.value = creditNoteData
    clients.value = clientList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this credit note.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function issue() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await creditNotesApi.issue(props.id)
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

async function voidCreditNote() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await creditNotesApi.void(props.id)
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

const pdfBusy = ref(false)

async function downloadPdf() {
  actionError.value = ''
  pdfBusy.value = true
  try {
    await downloadFile(`/credit-notes/${props.id}/pdf`, `${creditNote.value?.creditNoteNumber}.pdf`)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not download the PDF — please try again.'
  } finally {
    pdfBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="creditNote">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ creditNote.creditNoteNumber }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ clientName }} — {{ creditNote.reason }}</p>
        </div>
        <StatusBadge :text="label(CREDIT_NOTE_STATUS, creditNote.status)" :tone="creditNoteStatusTone(creditNote.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <p v-if="creditNote.pdfUrl" class="mt-4">
        <button type="button" :disabled="pdfBusy" class="text-sm font-medium text-slate-700 underline hover:text-slate-900 disabled:opacity-50" @click="downloadPdf">
          {{ pdfBusy ? 'Downloading…' : 'Download PDF' }}
        </button>
      </p>
      <p v-else-if="!isDraft" class="mt-4 text-sm text-slate-500">PDF not available for this credit note.</p>

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Issue date</dt>
          <dd class="text-slate-900">{{ formatDate(creditNote.issueDate) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Total</dt>
          <dd class="font-medium text-slate-900">{{ formatMoney(creditNote.totalAmount, currencyCode) }}</dd>
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
          @click="voidCreditNote"
        >
          Void
        </button>
      </div>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Lines</h2>
      <div class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Description</th>
              <th class="px-4 py-3">Amount</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="line in creditNote.lines" :key="line.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-900">{{ line.description }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatMoney(line.amount, currencyCode) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
