<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { apiClientsApi } from '../api/apiClients'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { API_CLIENT_STATUS, label, type ApiClient } from '../api/types'
import { apiClientStatusTone, formatDateTime } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const client = ref<ApiClient | null>(null)
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('integration.apiclient.manage'))
const isRevoked = computed(() => client.value?.status === 1)

async function load() {
  loading.value = true
  error.value = ''
  try {
    client.value = await apiClientsApi.get(props.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this API client.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

// --- Edit rate limit ---
const editOpen = ref(false)
const editRateLimit = ref(0)

function openEdit() {
  if (!client.value) return
  editRateLimit.value = client.value.rateLimitPerMinute
  editOpen.value = true
}

async function submitEdit() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await apiClientsApi.updateRateLimit(props.id, editRateLimit.value)
    editOpen.value = false
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// --- Rotate secret (one-time reveal, same as Create) ---
const rotatedSecret = ref<string | null>(null)
const copied = ref(false)

async function rotateSecret() {
  actionError.value = ''
  actionBusy.value = true
  copied.value = false
  try {
    const result = await apiClientsApi.rotateSecret(props.id)
    rotatedSecret.value = result.clientSecret
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

async function copySecret() {
  if (!rotatedSecret.value) return
  try {
    await navigator.clipboard.writeText(rotatedSecret.value)
    copied.value = true
  } catch {
    // Clipboard API unavailable — the secret is still fully visible and selectable below.
  }
}

// --- Revoke (irreversible — a two-step confirm rather than a plain button) ---
const confirmingRevoke = ref(false)

async function revoke() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await apiClientsApi.revoke(props.id)
    confirmingRevoke.value = false
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
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="client">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ client.name }}</h1>
          <p class="mt-1 font-mono text-sm text-slate-600">{{ client.clientId }}</p>
        </div>
        <StatusBadge :text="label(API_CLIENT_STATUS, client.status)" :tone="apiClientStatusTone(client.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
        <div>
          <dt class="text-slate-500">Rate limit</dt>
          <dd class="text-slate-900">{{ client.rateLimitPerMinute }}/min</dd>
        </div>
        <div>
          <dt class="text-slate-500">Created</dt>
          <dd class="text-slate-900">{{ formatDateTime(client.createdAt) }}</dd>
        </div>
      </dl>

      <div v-if="canManage && !isRevoked" class="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="openEdit"
        >
          Edit rate limit
        </button>
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="rotateSecret"
        >
          Rotate secret
        </button>
        <button
          v-if="!confirmingRevoke"
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
          @click="confirmingRevoke = true"
        >
          Revoke
        </button>
        <template v-else>
          <span class="self-center text-sm text-rose-800">Revoke this client permanently? There is no way to undo this.</span>
          <button
            type="button"
            :disabled="actionBusy"
            class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
            @click="revoke"
          >
            {{ actionBusy ? 'Revoking…' : 'Yes, revoke it' }}
          </button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="confirmingRevoke = false">Cancel</button>
        </template>
      </div>

      <form v-if="editOpen" class="mt-3 flex max-w-md flex-col gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Rate limit (requests/minute)
          <input v-model.number="editRateLimit" type="number" min="1" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <div class="flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">{{ actionBusy ? 'Saving…' : 'Save' }}</button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editOpen = false">Cancel</button>
        </div>
      </form>

      <div v-if="rotatedSecret" class="mt-4 max-w-xl rounded-lg border border-amber-300 bg-amber-50 p-4">
        <p class="text-sm font-medium text-amber-900">New secret — copy it now, it will never be shown again. The previous secret stays valid until you revoke it separately, so existing integrations have an overlap window to switch over.</p>
        <p class="mt-3 break-all rounded border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900">{{ rotatedSecret }}</p>
        <button
          type="button"
          class="mt-3 rounded-md border border-amber-400 bg-white px-3 py-1.5 text-sm font-medium text-amber-900 hover:bg-amber-100"
          @click="copySecret"
        >
          {{ copied ? 'Copied' : 'Copy secret' }}
        </button>
      </div>
    </template>
  </AppLayout>
</template>
