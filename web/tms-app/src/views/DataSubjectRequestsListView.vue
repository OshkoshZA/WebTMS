<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { dataSubjectRequestsApi } from '../api/privacy'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { DSR_REQUEST_TYPE, DSR_STATUS, DSR_SUBJECT_TYPE, label, type DataSubjectRequest } from '../api/types'
import { dsrStatusTone, formatDate } from '../lib/presentation'

const router = useRouter()
const auth = useAuthStore()

const requests = ref<DataSubjectRequest[]>([])
const loading = ref(true)
const error = ref('')
const statusFilter = ref<number | ''>('')

const canManage = computed(() => auth.hasFunction('privacy.dsr.manage'))

async function load() {
  loading.value = true
  error.value = ''
  try {
    requests.value = await dataSubjectRequestsApi.list(statusFilter.value === '' ? undefined : statusFilter.value)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of data subject requests.'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Data subject requests</h1>
      <RouterLink
        v-if="canManage"
        to="/data-subject-requests/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        Log a request
      </RouterLink>
    </div>
    <p class="mt-1 text-sm text-slate-500">Access/Rectification/Erasure/Portability requests (§14.3) — due 30 days from receipt.</p>

    <div class="mt-6">
      <select v-model="statusFilter" class="rounded-md border border-slate-300 px-3 py-2 text-sm" @change="load">
        <option value="">All statuses</option>
        <option v-for="(s, i) in DSR_STATUS" :key="s" :value="i">{{ s }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="requests.length === 0" class="mt-6 text-sm text-slate-500">No data subject requests found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Subject type</th>
            <th class="px-4 py-3">Request type</th>
            <th class="px-4 py-3">Received</th>
            <th class="px-4 py-3">Due</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="req in requests"
            :key="req.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/data-subject-requests/${req.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ label(DSR_SUBJECT_TYPE, req.subjectType) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ label(DSR_REQUEST_TYPE, req.requestType) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDate(req.receivedAt) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDate(req.dueDate) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(DSR_STATUS, req.status)" :tone="dsrStatusTone(req.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
