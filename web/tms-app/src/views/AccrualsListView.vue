<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { accrualsApi } from '../api/accruals'
import { subcontractorsApi } from '../api/subcontractors'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { SUBCONTRACTOR_ACCRUAL_STATUS, label, type Currency, type Subcontractor, type SubcontractorAccrual } from '../api/types'
import { accrualStatusTone, formatDate, formatMoney } from '../lib/presentation'

const route = useRoute()
const accruals = ref<SubcontractorAccrual[]>([])
const subcontractors = ref<Subcontractor[]>([])
const currencies = ref<Currency[]>([])
const subcontractorFilter = ref('')
// The AP clerk's own working set is "still outstanding" — default to Accrued rather
// than showing every Netted accrual too (§10.2) — but honor an explicit ?status=
// from a drill-through link (the dashboard's own Accrued tile, §16.2) over that default.
const statusFilter = ref<number | ''>(typeof route.query.status === 'string' ? Number(route.query.status) : 0)

const loading = ref(true)
const error = ref('')

function subcontractorName(id: string): string {
  return subcontractors.value.find((s) => s.id === id)?.name ?? id
}
function currencyCode(id: string): string {
  return currencies.value.find((c) => c.id === id)?.code ?? id
}

async function loadAccruals() {
  loading.value = true
  error.value = ''
  try {
    accruals.value = await accrualsApi.list(subcontractorFilter.value || undefined, statusFilter.value === '' ? undefined : statusFilter.value)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load accruals.'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  try {
    const [subList, currencyList] = await Promise.all([subcontractorsApi.list(), referenceApi.currencies()])
    subcontractors.value = subList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load reference data.'
  }
  await loadAccruals()
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Accruals</h1>
    <p class="mt-1 text-sm text-slate-500">The buy-side liability raised the moment a leg is Allocated to a subcontractor (§10.2) — what a carrier still has outstanding.</p>

    <div class="mt-6 flex flex-wrap gap-3">
      <select v-model="subcontractorFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="loadAccruals">
        <option value="">All subcontractors</option>
        <option v-for="s in subcontractors" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
      <select v-model="statusFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="loadAccruals">
        <option value="">All statuses</option>
        <option v-for="(s, i) in SUBCONTRACTOR_ACCRUAL_STATUS" :key="s" :value="i">{{ s }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="accruals.length === 0" class="mt-6 text-sm text-slate-500">No accruals found for this filter.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Subcontractor</th>
            <th class="px-4 py-3">Accrual date</th>
            <th class="px-4 py-3">Estimated amount</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="accrual in accruals" :key="accrual.id" class="border-b border-slate-100 last:border-0">
            <td class="px-4 py-3 font-medium text-slate-900">{{ subcontractorName(accrual.subcontractorId) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDate(accrual.accrualDate) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatMoney(accrual.estimatedAmount, currencyCode(accrual.currencyId)) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(SUBCONTRACTOR_ACCRUAL_STATUS, accrual.status)" :tone="accrualStatusTone(accrual.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
