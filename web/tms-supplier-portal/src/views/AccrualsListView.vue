<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { accrualsApi } from '../api/accruals'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { ACCRUAL_STATUS, label, type Currency, type SubcontractorAccrual } from '../api/types'
import { accrualStatusTone, formatDate, formatMoney } from '../lib/presentation'

const accruals = ref<SubcontractorAccrual[]>([])
const currencies = ref<Currency[]>([])
const loading = ref(true)
const error = ref('')

function currencyCode(currencyId: string): string {
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}

onMounted(async () => {
  try {
    const [accrualList, currencyList] = await Promise.all([accrualsApi.list(), referenceApi.currencies()])
    accruals.value = accrualList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load your accruals.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Accruals</h1>
    <p class="mt-1 text-sm text-slate-500">Estimated amounts owed for delivered legs, before a supplier invoice matches them.</p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="accruals.length === 0" class="mt-6 text-sm text-slate-500">No accruals yet.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Accrual date</th>
            <th class="px-4 py-3">Estimated amount</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="accrual in accruals" :key="accrual.id" class="border-b border-slate-100 last:border-0">
            <td class="px-4 py-3 text-slate-600">{{ formatDate(accrual.accrualDate) }}</td>
            <td class="px-4 py-3 font-medium text-slate-900">
              {{ formatMoney(accrual.estimatedAmount, currencyCode(accrual.currencyId)) }}
            </td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(ACCRUAL_STATUS, accrual.status)" :tone="accrualStatusTone(accrual.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
