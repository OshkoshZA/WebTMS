<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { creditNotesApi } from '../api/creditNotes'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { CREDIT_NOTE_STATUS, label, type CreditNote, type Currency } from '../api/types'
import { creditNoteStatusTone, formatDate, formatMoney } from '../lib/presentation'

const creditNotes = ref<CreditNote[]>([])
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
    const [creditNoteList, currencyList] = await Promise.all([creditNotesApi.list(), referenceApi.currencies()])
    creditNotes.value = creditNoteList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load your credit notes.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Credit notes</h1>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="creditNotes.length === 0" class="mt-6 text-sm text-slate-500">No credit notes yet.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Credit note no.</th>
            <th class="px-4 py-3">Reason</th>
            <th class="px-4 py-3">Issue date</th>
            <th class="px-4 py-3">Total</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="creditNote in creditNotes" :key="creditNote.id">
            <tr class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50" @click="toggleExpand(creditNote.id)">
              <td class="px-4 py-3 font-medium text-slate-900">{{ creditNote.creditNoteNumber }}</td>
              <td class="px-4 py-3 text-slate-600">{{ creditNote.reason }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDate(creditNote.issueDate) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatMoney(creditNote.totalAmount, currencyCode(creditNote.currencyId)) }}</td>
              <td class="px-4 py-3"><StatusBadge :text="label(CREDIT_NOTE_STATUS, creditNote.status)" :tone="creditNoteStatusTone(creditNote.status)" /></td>
            </tr>
            <tr v-if="expandedId === creditNote.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
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
  </AppLayout>
</template>
