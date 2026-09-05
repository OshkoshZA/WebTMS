<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { legsApi } from '../api/legs'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { CONFIRMATION_STATUS, LEG_STATUS, label, type Currency, type SubcontractorLeg } from '../api/types'
import { confirmationStatusTone, formatMoney, legStatusTone } from '../lib/presentation'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const legs = ref<SubcontractorLeg[]>([])
const currencies = ref<Currency[]>([])
const loading = ref(true)
const error = ref('')
// Pre-filled from ?status=/?confirmationStatus= so the dashboard's own tiles have
// somewhere real to drill into — still just plain selects here, no separate route
// per filter value.
const statusFilter = ref(typeof route.query.status === 'string' ? Number(route.query.status) : -1)
const confirmationStatusFilter = ref(
  typeof route.query.confirmationStatus === 'string' ? Number(route.query.confirmationStatus) : -1,
)

function currencyCode(currencyId: string | null): string {
  if (!currencyId) return ''
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}

const sortedLegs = computed(() =>
  legs.value
    .filter((leg) => {
      if (statusFilter.value !== -1 && leg.status !== statusFilter.value) return false
      if (confirmationStatusFilter.value !== -1 && leg.confirmation?.status !== confirmationStatusFilter.value) return false
      return true
    })
    .slice()
    .sort((a, b) => a.sequenceNo - b.sequenceNo),
)

onMounted(async () => {
  try {
    const [legList, currencyList] = await Promise.all([
      legsApi.listForSubcontractor(auth.subcontractorId),
      referenceApi.currencies(),
    ])
    legs.value = legList
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load your legs.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Your legs</h1>
    <p class="mt-1 text-sm text-slate-500">
      Origin and destination names aren't shown here — location details aren't part of this portal's scope yet.
    </p>

    <div class="mt-6 flex gap-3">
      <select v-model.number="statusFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
        <option :value="-1">All statuses</option>
        <option v-for="(s, i) in LEG_STATUS" :key="s" :value="i">{{ s }}</option>
      </select>
      <select v-model.number="confirmationStatusFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
        <option :value="-1">All confirmations</option>
        <option v-for="(s, i) in CONFIRMATION_STATUS" :key="s" :value="i">{{ s }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="sortedLegs.length === 0" class="mt-6 text-sm text-slate-500">No legs allocated to you yet.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Seq</th>
            <th class="px-4 py-3">Status</th>
            <th class="px-4 py-3">Buy amount</th>
            <th class="px-4 py-3">Confirmation</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="leg in sortedLegs"
            :key="leg.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/legs/${leg.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ leg.sequenceNo }}</td>
            <td class="px-4 py-3"><StatusBadge :text="label(LEG_STATUS, leg.status)" :tone="legStatusTone(leg.status)" /></td>
            <td class="px-4 py-3 text-slate-600">{{ formatMoney(leg.buyAmount, currencyCode(leg.buyCurrencyId)) }}</td>
            <td class="px-4 py-3">
              <StatusBadge
                v-if="leg.confirmation"
                :text="label(CONFIRMATION_STATUS, leg.confirmation.status)"
                :tone="confirmationStatusTone(leg.confirmation.status)"
              />
              <span v-else class="text-slate-400">—</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
