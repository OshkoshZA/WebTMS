<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { auditEntriesApi, downloadAuditExport } from '../api/audit'
import { ApiError } from '../api/client'
import { AUDIT_ACTION, label, type AuditEntry } from '../api/types'
import { formatDateTime } from '../lib/presentation'

const entries = ref<AuditEntry[]>([])
const loading = ref(true)
const error = ref('')
const exporting = ref(false)
const exportError = ref('')
const expandedId = ref<string | null>(null)

const filters = ref({ entityType: '', entityId: '', action: '' as number | '', from: '', to: '' })

const currentFilters = computed(() => ({
  entityType: filters.value.entityType || undefined,
  entityId: filters.value.entityId || undefined,
  action: filters.value.action === '' ? undefined : filters.value.action,
  from: filters.value.from ? new Date(filters.value.from).toISOString() : undefined,
  to: filters.value.to ? new Date(filters.value.to).toISOString() : undefined,
}))

async function load() {
  loading.value = true
  error.value = ''
  try {
    entries.value = await auditEntriesApi.list(currentFilters.value)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the audit trail.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function runExport() {
  exportError.value = ''
  exporting.value = true
  try {
    await downloadAuditExport(currentFilters.value)
  } catch (e) {
    exportError.value = e instanceof ApiError ? e.message : 'Could not export the audit trail.'
  } finally {
    exporting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Audit trail</h1>
    <p class="mt-1 text-sm text-slate-500">Every mutating change across the platform (§12) — capped to the most recent 500 here; Export CSV has no cap.</p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <ErrorAlert v-else-if="exportError" :message="exportError" class="mt-4" />

    <form class="mt-6 grid grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4 sm:grid-cols-5" @submit.prevent="load">
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Entity type
        <input v-model="filters.entityType" type="text" placeholder="e.g. Invoice" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Entity ID
        <input v-model="filters.entityId" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Action
        <select v-model="filters.action" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="">All</option>
          <option v-for="(a, i) in AUDIT_ACTION" :key="a" :value="i">{{ a }}</option>
        </select>
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        From
        <input v-model="filters.from" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        To
        <input v-model="filters.to" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>
      <div class="col-span-2 flex items-end gap-3 sm:col-span-5">
        <button type="submit" :disabled="loading" class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50">
          {{ loading ? 'Filtering…' : 'Apply filters' }}
        </button>
        <button
          type="button"
          :disabled="exporting"
          class="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="runExport"
        >
          {{ exporting ? 'Exporting…' : 'Export CSV' }}
        </button>
      </div>
    </form>

    <p v-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="entries.length === 0" class="mt-6 text-sm text-slate-500">No audit entries match these filters.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Changed at</th>
            <th class="px-4 py-3">Entity</th>
            <th class="px-4 py-3">Action</th>
            <th class="px-4 py-3">Changed by</th>
            <th class="px-4 py-3">Reason</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="entry in entries" :key="entry.id">
            <tr
              class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
              @click="expandedId = expandedId === entry.id ? null : entry.id"
            >
              <td class="px-4 py-3 text-slate-600">{{ formatDateTime(entry.changedAtUtc) }}</td>
              <td class="px-4 py-3 font-medium text-slate-900">{{ entry.entityType }} {{ entry.entityId.slice(0, 8) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ label(AUDIT_ACTION, entry.action) }}</td>
              <td class="px-4 py-3 font-mono text-xs text-slate-600">
                {{ entry.changedByApiClientId ? `API client ${entry.changedByApiClientId.slice(0, 8)}` : entry.changedByUserId ? entry.changedByUserId.slice(0, 8) : '—' }}
              </td>
              <td class="px-4 py-3 text-slate-600">{{ entry.reason ?? '—' }}</td>
            </tr>
            <tr v-if="expandedId === entry.id" class="border-b border-slate-100 bg-slate-50 last:border-0">
              <td colspan="5" class="px-4 py-3">
                <div class="grid gap-4 sm:grid-cols-2">
                  <div>
                    <p class="text-xs font-semibold uppercase tracking-wide text-slate-500">Old value</p>
                    <pre class="mt-1 max-h-64 overflow-auto rounded border border-slate-200 bg-white p-2 text-xs">{{ entry.oldValueJson ?? '—' }}</pre>
                  </div>
                  <div>
                    <p class="text-xs font-semibold uppercase tracking-wide text-slate-500">New value</p>
                    <pre class="mt-1 max-h-64 overflow-auto rounded border border-slate-200 bg-white p-2 text-xs">{{ entry.newValueJson ?? '—' }}</pre>
                  </div>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
