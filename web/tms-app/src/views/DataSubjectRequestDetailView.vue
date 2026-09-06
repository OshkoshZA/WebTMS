<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { dataSubjectRequestsApi } from '../api/privacy'
import { driversApi } from '../api/drivers'
import { usersApi } from '../api/users'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { DSR_REQUEST_TYPE, DSR_STATUS, DSR_SUBJECT_TYPE, label, type DataSubjectRequest } from '../api/types'
import { dsrStatusTone, formatDateTime } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const request = ref<DataSubjectRequest | null>(null)
const subjectLabel = ref('')
const exportResult = ref<Record<string, unknown> | null>(null)

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('privacy.dsr.manage'))
const isReceived = computed(() => request.value?.status === 0)
const isFulfilled = computed(() => request.value?.status === 2)
const isExportable = computed(
  () => isFulfilled.value && (request.value?.requestType === 0 || request.value?.requestType === 3),
)
const isErasure = computed(() => request.value?.requestType === 2)

async function resolveSubject(dsr: DataSubjectRequest) {
  try {
    if (dsr.subjectType === 0) {
      const driver = await driversApi.get(dsr.subjectId)
      subjectLabel.value = `${driver.employeeNo} — ${driver.name}`
    } else {
      const user = await usersApi.get(dsr.subjectId)
      subjectLabel.value = `${user.email} — ${user.displayName}`
    }
  } catch {
    // The subject record may itself have been removed/anonymized since — fall back to
    // showing the raw id rather than blocking the rest of the page on it.
    subjectLabel.value = dsr.subjectId
  }
}

async function load() {
  loading.value = true
  error.value = ''
  exportResult.value = null
  try {
    request.value = await dataSubjectRequestsApi.get(props.id)
    await resolveSubject(request.value)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this data subject request.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

// --- Fulfill (Erasure gets an explicit confirm — it has a real, irreversible side effect) ---
const confirmingErasure = ref(false)

async function fulfill() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await dataSubjectRequestsApi.fulfill(props.id)
    confirmingErasure.value = false
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

function onFulfillClick() {
  if (isErasure.value && !confirmingErasure.value) {
    confirmingErasure.value = true
    return
  }
  fulfill()
}

// --- Reject ---
const rejectOpen = ref(false)
const rejectionReason = ref('')

async function submitReject() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await dataSubjectRequestsApi.reject(props.id, rejectionReason.value)
    rejectOpen.value = false
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// --- Export (live, recomputed — repeatable, not a one-shot side effect) ---
async function runExport() {
  actionError.value = ''
  actionBusy.value = true
  try {
    exportResult.value = await dataSubjectRequestsApi.export(props.id)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not export this request.'
  } finally {
    actionBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="request">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">
            {{ label(DSR_REQUEST_TYPE, request.requestType) }} — {{ label(DSR_SUBJECT_TYPE, request.subjectType) }}
          </h1>
          <p class="mt-1 text-sm text-slate-600">{{ subjectLabel }}</p>
        </div>
        <StatusBadge :text="label(DSR_STATUS, request.status)" :tone="dsrStatusTone(request.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
        <div>
          <dt class="text-slate-500">Received</dt>
          <dd class="text-slate-900">{{ formatDateTime(request.receivedAt) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Due</dt>
          <dd class="text-slate-900">{{ formatDateTime(request.dueDate) }}</dd>
        </div>
        <div v-if="request.fulfilledAt">
          <dt class="text-slate-500">Fulfilled</dt>
          <dd class="text-slate-900">{{ formatDateTime(request.fulfilledAt) }}</dd>
        </div>
        <div v-if="request.rejectionReason" class="col-span-2 sm:col-span-3">
          <dt class="text-slate-500">Rejection reason</dt>
          <dd class="text-slate-900">{{ request.rejectionReason }}</dd>
        </div>
      </dl>

      <div v-if="canManage && isReceived" class="mt-4 flex flex-wrap items-center gap-3">
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="onFulfillClick"
        >
          {{ actionBusy && !rejectOpen ? 'Fulfilling…' : 'Fulfill' }}
        </button>
        <button
          v-if="!rejectOpen"
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
          @click="rejectOpen = true"
        >
          Reject
        </button>
      </div>

      <div v-if="confirmingErasure" class="mt-3 max-w-xl rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
        <p>
          Fulfilling this Erasure request will anonymize the subject's identifying fields (name, or email/username/display name) right now —
          this cannot be undone. It only succeeds if the subject is already deactivated.
        </p>
        <div class="mt-3 flex gap-3">
          <button type="button" :disabled="actionBusy" class="rounded-md bg-amber-900 px-3 py-1.5 text-sm text-white disabled:opacity-50" @click="fulfill">
            {{ actionBusy ? 'Erasing…' : 'Yes, erase and fulfill' }}
          </button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="confirmingErasure = false">Cancel</button>
        </div>
      </div>

      <form v-if="rejectOpen" class="mt-3 flex max-w-lg flex-col gap-3 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitReject">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Rejection reason
          <input v-model="rejectionReason" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <div class="flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-rose-800 px-3 py-1.5 text-sm text-white disabled:opacity-50">
            {{ actionBusy ? 'Rejecting…' : 'Reject' }}
          </button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="rejectOpen = false">Cancel</button>
        </div>
      </form>

      <template v-if="isExportable">
        <h2 class="mt-8 text-lg font-semibold text-slate-900">Export</h2>
        <p class="mt-1 text-sm text-slate-500">A live, recomputed snapshot — never archived, so this can be run again any time.</p>
        <button
          type="button"
          :disabled="actionBusy"
          class="mt-3 rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="runExport"
        >
          {{ actionBusy ? 'Exporting…' : 'Run export' }}
        </button>
        <dl v-if="exportResult" class="mt-3 grid max-w-lg grid-cols-2 gap-3 rounded-md bg-slate-50 p-4 text-sm">
          <template v-for="(value, key) in exportResult" :key="key">
            <dt class="text-slate-500">{{ key }}</dt>
            <dd class="text-slate-900">{{ value ?? '—' }}</dd>
          </template>
        </dl>
      </template>
    </template>
  </AppLayout>
</template>
