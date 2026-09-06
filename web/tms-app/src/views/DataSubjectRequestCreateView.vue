<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { dataSubjectRequestsApi } from '../api/privacy'
import { driversApi } from '../api/drivers'
import { usersApi } from '../api/users'
import { clientsApi } from '../api/clients'
import { subcontractorsApi } from '../api/subcontractors'
import { ApiError } from '../api/client'
import { DSR_REQUEST_TYPE, type Client, type Driver, type PortalContact, type Subcontractor, type User } from '../api/types'

const router = useRouter()

// 0 Driver | 1 User | 2 ClientContact | 3 SubcontractorContact
const subjectType = ref<number | ''>('')
const requestType = ref<number | ''>('')
const subjectId = ref('')

// Driver/User: a flat picker. ClientContact/SubcontractorContact: pick the owning
// party first, then one of its own portal contacts — there's no single endpoint that
// lists "every Client/Subcontractor contact across the tenant" to pick from directly.
const drivers = ref<Driver[]>([])
const users = ref<User[]>([])
const clients = ref<Client[]>([])
const subcontractors = ref<Subcontractor[]>([])
const clientId = ref('')
const subcontractorId = ref('')
const clientContacts = ref<PortalContact[]>([])
const subcontractorContacts = ref<PortalContact[]>([])

const loadingReferenceData = ref(false)
const error = ref('')
const submitting = ref(false)

watch(subjectType, async (value) => {
  subjectId.value = ''
  clientId.value = ''
  subcontractorId.value = ''
  clientContacts.value = []
  subcontractorContacts.value = []
  error.value = ''
  if (value === '') return

  loadingReferenceData.value = true
  try {
    if (value === 0 && drivers.value.length === 0) drivers.value = await driversApi.list()
    if (value === 1 && users.value.length === 0) users.value = await usersApi.list()
    if (value === 2 && clients.value.length === 0) clients.value = await clientsApi.list()
    if (value === 3 && subcontractors.value.length === 0) subcontractors.value = await subcontractorsApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the picker for this subject type.'
  } finally {
    loadingReferenceData.value = false
  }
})

watch(clientId, async (id) => {
  subjectId.value = ''
  clientContacts.value = []
  if (!id) return
  try {
    clientContacts.value = await clientsApi.contacts(id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Could not load this client's contacts."
  }
})

watch(subcontractorId, async (id) => {
  subjectId.value = ''
  subcontractorContacts.value = []
  if (!id) return
  try {
    subcontractorContacts.value = await subcontractorsApi.contacts(id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Could not load this subcontractor's contacts."
  }
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const dsr = await dataSubjectRequestsApi.create({
      subjectType: subjectType.value as number,
      subjectId: subjectId.value,
      requestType: requestType.value as number,
    })
    await router.push(`/data-subject-requests/${dsr.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Log a data subject request</h1>
    <p class="mt-1 text-sm text-slate-500">Due 30 days from now regardless of request type.</p>

    <form class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Subject type
        <select v-model="subjectType" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select…</option>
          <option :value="0">Driver</option>
          <option :value="1">User (internal staff)</option>
          <option :value="2">Client contact</option>
          <option :value="3">Subcontractor contact</option>
        </select>
      </label>

      <p v-if="loadingReferenceData" class="text-sm text-slate-500">Loading…</p>

      <label v-if="subjectType === 0" class="flex flex-col gap-1 text-sm text-slate-700">
        Driver
        <select v-model="subjectId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select…</option>
          <option v-for="d in drivers" :key="d.id" :value="d.id">{{ d.employeeNo }} — {{ d.name }}</option>
        </select>
      </label>

      <label v-if="subjectType === 1" class="flex flex-col gap-1 text-sm text-slate-700">
        User
        <select v-model="subjectId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select…</option>
          <option v-for="u in users" :key="u.id" :value="u.id">{{ u.email }} — {{ u.displayName }}</option>
        </select>
      </label>

      <template v-if="subjectType === 2">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Client
          <select v-model="clientId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
          </select>
        </label>
        <label v-if="clientId" class="flex flex-col gap-1 text-sm text-slate-700">
          Contact
          <select v-model="subjectId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="c in clientContacts" :key="c.id" :value="c.id">{{ c.email }} — {{ c.displayName }}</option>
          </select>
          <p v-if="clientContacts.length === 0" class="text-xs text-slate-500">This client has no portal contacts.</p>
        </label>
      </template>

      <template v-if="subjectType === 3">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Subcontractor
          <select v-model="subcontractorId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="s in subcontractors" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
        </label>
        <label v-if="subcontractorId" class="flex flex-col gap-1 text-sm text-slate-700">
          Contact
          <select v-model="subjectId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="c in subcontractorContacts" :key="c.id" :value="c.id">{{ c.email }} — {{ c.displayName }}</option>
          </select>
          <p v-if="subcontractorContacts.length === 0" class="text-xs text-slate-500">This subcontractor has no portal contacts.</p>
        </label>
      </template>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Request type
        <select v-model="requestType" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select…</option>
          <option v-for="(t, i) in DSR_REQUEST_TYPE" :key="t" :value="i">{{ t }}</option>
        </select>
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting || !subjectId"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Logging…' : 'Log request' }}
        </button>
        <RouterLink to="/data-subject-requests" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
