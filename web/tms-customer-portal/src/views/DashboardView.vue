<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import CreditStatusWidget from '../components/CreditStatusWidget.vue'
import { loadsApi } from '../api/loads'
import { exceptionsApi } from '../api/exceptions'
import { ApiError } from '../api/client'
import { EXCEPTION_SEVERITY, LOAD_STATUS, type ExceptionRecord, type Load } from '../api/types'

const loads = ref<Load[]>([])
const openExceptions = ref<ExceptionRecord[]>([])
const loading = ref(true)
const error = ref('')

// An aged view of the caller's own outstanding balance (§16.3's own third bullet) is
// deliberately left out — DebtorsAgingSnapshot rows are only written at financial
// period close (§10.3), not live, the exact same reason the internal dashboard's own
// aged-debtors tile was skipped (§16.2). Everything else here is computed client-side
// from data this portal already fetches elsewhere — no new backend aggregate needed.
const loadCountsByStatus = computed(() => {
  const counts = new Array(LOAD_STATUS.length).fill(0)
  for (const load of loads.value) counts[load.status]++
  return counts
})

const exceptionCountsBySeverity = computed(() => {
  const counts = new Array(EXCEPTION_SEVERITY.length).fill(0)
  for (const exception of openExceptions.value) counts[exception.severity]++
  return counts
})

const totalOpenExceptions = computed(() => openExceptions.value.length)

onMounted(async () => {
  try {
    const [loadList, exceptionList] = await Promise.all([loadsApi.list(), exceptionsApi.list(0)])
    loads.value = loadList
    openExceptions.value = exceptionList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the dashboard.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Dashboard</h1>

    <div class="mt-6">
      <CreditStatusWidget />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>

    <template v-else>
      <section class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Open exceptions</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-4">
          <RouterLink
            to="/exceptions?status=0"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ totalOpenExceptions }}</p>
            <p class="mt-1 text-sm text-slate-500">All open</p>
          </RouterLink>
          <RouterLink
            v-for="(severity, i) in EXCEPTION_SEVERITY"
            :key="severity"
            :to="`/exceptions?status=0&severity=${i}`"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold" :class="i === 2 ? 'text-rose-700' : i === 1 ? 'text-amber-700' : 'text-slate-900'">
              {{ exceptionCountsBySeverity[i] }}
            </p>
            <p class="mt-1 text-sm text-slate-500">{{ severity }}</p>
          </RouterLink>
        </div>
      </section>

      <section class="mt-8">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Your loads by status</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-5">
          <RouterLink
            v-for="(status, i) in LOAD_STATUS"
            :key="status"
            :to="`/loads?status=${i}`"
            class="rounded-lg border border-slate-200 bg-white p-4 hover:border-slate-300 hover:shadow-sm"
          >
            <p class="text-2xl font-semibold text-slate-900">{{ loadCountsByStatus[i] }}</p>
            <p class="mt-1 text-sm text-slate-500">{{ status }}</p>
          </RouterLink>
        </div>
      </section>
    </template>
  </AppLayout>
</template>
