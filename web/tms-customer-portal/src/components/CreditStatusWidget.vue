<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import ErrorAlert from './ErrorAlert.vue'
import { clientsApi } from '../api/clients'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { Currency, CreditStatus } from '../api/types'
import { formatMoney } from '../lib/presentation'

const auth = useAuthStore()

const status = ref<CreditStatus | null>(null)
const currencies = ref<Currency[]>([])
const loading = ref(true)
const error = ref('')

const currencyCode = computed(() => currencies.value.find((c) => c.id === status.value?.currencyId)?.code ?? '')

onMounted(async () => {
  try {
    const [statusData, currencyList] = await Promise.all([
      clientsApi.creditStatus(auth.clientId),
      referenceApi.currencies(),
    ])
    status.value = statusData
    currencies.value = currencyList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load your credit status.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <ErrorAlert v-if="error" :message="error" />
  <p v-else-if="loading" class="text-sm text-slate-500">Loading credit status…</p>

  <dl v-else-if="status" class="grid grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4 text-sm sm:grid-cols-4">
    <div>
      <dt class="text-slate-500">Credit limit</dt>
      <dd class="text-slate-900">{{ formatMoney(status.creditLimit, currencyCode) }}</dd>
    </div>
    <div>
      <dt class="text-slate-500">AR outstanding</dt>
      <dd class="text-slate-900">{{ formatMoney(status.arOutstanding, currencyCode) }}</dd>
    </div>
    <div>
      <dt class="text-slate-500">WIP</dt>
      <dd class="text-slate-900">{{ formatMoney(status.wip, currencyCode) }}</dd>
    </div>
    <div>
      <dt class="text-slate-500">Available credit</dt>
      <dd class="font-medium" :class="status.availableCredit < 0 ? 'text-rose-700' : 'text-slate-900'">
        {{ formatMoney(status.availableCredit, currencyCode) }}
      </dd>
    </div>
  </dl>
</template>
