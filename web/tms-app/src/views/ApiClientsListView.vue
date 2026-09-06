<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { apiClientsApi } from '../api/apiClients'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { API_CLIENT_STATUS, label, type ApiClient } from '../api/types'
import { apiClientStatusTone, formatDateTime } from '../lib/presentation'

const router = useRouter()
const auth = useAuthStore()

const apiClients = ref<ApiClient[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const canManage = computed(() => auth.hasFunction('integration.apiclient.manage'))

const filteredApiClients = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return apiClients.value
  return apiClients.value.filter((c) => c.name.toLowerCase().includes(term) || c.clientId.toLowerCase().includes(term))
})

onMounted(async () => {
  try {
    apiClients.value = await apiClientsApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of API clients.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">API clients</h1>
      <RouterLink
        v-if="canManage"
        to="/api-clients/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New API client
      </RouterLink>
    </div>
    <p class="mt-1 text-sm text-slate-500">System-to-system integration partners, provisioned for the OAuth2 client-credentials grant (§11.1).</p>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by name or client ID…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredApiClients.length === 0" class="mt-6 text-sm text-slate-500">No API clients found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Client ID</th>
            <th class="px-4 py-3">Rate limit</th>
            <th class="px-4 py-3">Created</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="client in filteredApiClients"
            :key="client.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/api-clients/${client.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ client.name }}</td>
            <td class="px-4 py-3 font-mono text-xs text-slate-600">{{ client.clientId }}</td>
            <td class="px-4 py-3 text-slate-600">{{ client.rateLimitPerMinute }}/min</td>
            <td class="px-4 py-3 text-slate-600">{{ formatDateTime(client.createdAt) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(API_CLIENT_STATUS, client.status)" :tone="apiClientStatusTone(client.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
