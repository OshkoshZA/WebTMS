<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { exceptionsApi } from '../api/exceptions'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { EXCEPTION_SEVERITY, EXCEPTION_STATUS, label, type ExceptionRecord } from '../api/types'
import { exceptionSeverityTone, exceptionStatusTone, formatDateTime } from '../lib/presentation'

const route = useRoute()
const auth = useAuthStore()

const exceptions = ref<ExceptionRecord[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

// Defaults to Open — "what needs attention" is this screen's whole point — but a
// dashboard drill-through can pre-select any status/severity via the query string.
const statusFilter = ref(typeof route.query.status === 'string' ? Number(route.query.status) : 0)
const severityFilter = ref(typeof route.query.severity === 'string' ? Number(route.query.severity) : -1)

const canManage = computed(() => auth.hasFunction('exception.manage'))

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

const resolvingId = ref<string | null>(null)
const resolutionNotes = ref('')

async function acknowledge(id: string) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await exceptionsApi.acknowledge(id)
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

function openResolve(id: string) {
  resolvingId.value = id
  resolutionNotes.value = ''
}

async function submitResolve(id: string) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await exceptionsApi.resolve(id, resolutionNotes.value || undefined)
    resolvingId.value = null
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Exceptions</h1>

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
    <ErrorAlert v-else-if="actionError" :message="actionError" class="mt-4" />
    <p v-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredExceptions.length === 0" class="mt-6 text-sm text-slate-500">No exceptions found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Category</th>
            <th class="px-4 py-3">Severity</th>
            <th class="px-4 py-3">Description</th>
            <th class="px-4 py-3">Entity</th>
            <th class="px-4 py-3">Raised</th>
            <th class="px-4 py-3">Status</th>
            <th v-if="canManage" class="px-4 py-3">Action</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="exception in filteredExceptions" :key="exception.id">
            <tr class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 font-medium text-slate-900">{{ exception.category }}</td>
              <td class="px-4 py-3"><StatusBadge :text="label(EXCEPTION_SEVERITY, exception.severity)" :tone="exceptionSeverityTone(exception.severity)" /></td>
              <td class="px-4 py-3 text-slate-600">{{ exception.description }}</td>
              <td class="px-4 py-3 text-slate-600">{{ exception.entityType }}</td>
              <td class="px-4 py-3 text-slate-600">{{ formatDateTime(exception.raisedAt) }}</td>
              <td class="px-4 py-3"><StatusBadge :text="label(EXCEPTION_STATUS, exception.status)" :tone="exceptionStatusTone(exception.status)" /></td>
              <td v-if="canManage" class="px-4 py-3">
                <div class="flex gap-2">
                  <button
                    v-if="exception.status === 0"
                    type="button"
                    :disabled="actionBusy"
                    class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                    @click="acknowledge(exception.id)"
                  >
                    Acknowledge
                  </button>
                  <button
                    v-if="exception.status !== 2"
                    type="button"
                    :disabled="actionBusy"
                    class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                    @click="openResolve(exception.id)"
                  >
                    Resolve
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="resolvingId === exception.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
              <td :colspan="canManage ? 7 : 6" class="px-4 py-3">
                <form class="flex items-end gap-3" @submit.prevent="submitResolve(exception.id)">
                  <label class="flex flex-1 flex-col gap-1 text-sm text-slate-700">
                    Resolution notes
                    <input v-model="resolutionNotes" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
                  </label>
                  <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
                    Confirm
                  </button>
                  <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="resolvingId = null">
                    Cancel
                  </button>
                </form>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
