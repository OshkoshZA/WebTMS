<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { exceptionsApi } from '../api/exceptions'
import { ApiError } from '../api/client'
import { EXCEPTION_SEVERITY, EXCEPTION_STATUS, label, type ExceptionRecord } from '../api/types'
import { exceptionSeverityTone, exceptionStatusTone, formatDateTime } from '../lib/presentation'

const route = useRoute()

const exceptions = ref<ExceptionRecord[]>([])
const loading = ref(true)
const error = ref('')

// Defaults to Open — "what needs attention" is this screen's whole point — but a
// dashboard drill-through can pre-select any status/severity via the query string.
const statusFilter = ref(typeof route.query.status === 'string' ? Number(route.query.status) : 0)
const severityFilter = ref(typeof route.query.severity === 'string' ? Number(route.query.severity) : -1)

const filteredExceptions = computed(() =>
  exceptions.value.filter((e) => severityFilter.value === -1 || e.severity === severityFilter.value),
)

async function load() {
  loading.value = true
  error.value = ''
  try {
    exceptions.value = await exceptionsApi.list(statusFilter.value === -1 ? undefined : statusFilter.value)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load exceptions.'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Exceptions</h1>
    <p class="mt-1 text-sm text-slate-500">Anything raised against your own legs or supplier invoices that needs a look.</p>

    <div class="mt-6 flex gap-3">
      <select v-model.number="statusFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="load">
        <option :value="-1">All statuses</option>
        <option v-for="(s, i) in EXCEPTION_STATUS" :key="s" :value="i">{{ s }}</option>
      </select>
      <select v-model.number="severityFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
        <option :value="-1">All severities</option>
        <option v-for="(s, i) in EXCEPTION_SEVERITY" :key="s" :value="i">{{ s }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredExceptions.length === 0" class="mt-6 text-sm text-slate-500">No exceptions found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Category</th>
            <th class="px-4 py-3">Severity</th>
            <th class="px-4 py-3">Description</th>
            <th class="px-4 py-3">Raised</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="exception in filteredExceptions" :key="exception.id" class="border-b border-slate-100 last:border-0">
            <td class="px-4 py-3 font-medium text-slate-900">{{ exception.category }}</td>
            <td class="px-4 py-3"><StatusBadge :text="label(EXCEPTION_SEVERITY, exception.severity)" :tone="exceptionSeverityTone(exception.severity)" /></td>
            <td class="px-4 py-3 text-slate-600">{{ exception.description }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDateTime(exception.raisedAt) }}</td>
            <td class="px-4 py-3"><StatusBadge :text="label(EXCEPTION_STATUS, exception.status)" :tone="exceptionStatusTone(exception.status)" /></td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
