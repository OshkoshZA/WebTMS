<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { usersApi } from '../api/users'
import { rolesApi } from '../api/roles'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { ACTIVE_DEACTIVATED, label, type Company, type Role, type User } from '../api/types'
import { activeDeactivatedTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const user = ref<User | null>(null)
const companies = ref<Company[]>([])
const roles = ref<Role[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('identity.user.manage'))
// Client-side mirror of UsersController's own self-deactivation guard — the API
// still enforces it (409) either way, this just avoids offering a button that
// would only ever fail.
const isSelf = computed(() => user.value?.email === auth.email)

function companyName(companyId: string): string {
  return companies.value.find((c) => c.id === companyId)?.legalName ?? companyId
}

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [userData, companyList, roleList] = await Promise.all([
      usersApi.get(props.id),
      referenceApi.companies(),
      rolesApi.list(),
    ])
    user.value = userData
    companies.value = companyList
    roles.value = roleList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this user.'
  } finally {
    loading.value = false
  }
}

onMounted(loadEverything)

async function runAction(action: () => Promise<void>) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await action()
    await loadEverything()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// --- Edit display name ---
const editOpen = ref(false)
const editDisplayName = ref('')

function openEdit() {
  if (!user.value) return
  editDisplayName.value = user.value.displayName
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await usersApi.update(props.id, { displayName: editDisplayName.value })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!user.value) return
  runAction(() => (user.value!.status === 0 ? usersApi.deactivate(props.id) : usersApi.reactivate(props.id)))
}

// --- Add a company/role assignment ---
const addAssignmentOpen = ref(false)
const newAssignment = ref({ companyId: '', roleId: '' })

function openAddAssignment() {
  newAssignment.value = { companyId: '', roleId: '' }
  addAssignmentOpen.value = true
}

function submitAddAssignment() {
  runAction(async () => {
    await usersApi.addCompanyRole(props.id, newAssignment.value)
    addAssignmentOpen.value = false
  })
}

function removeAssignment(companyRoleId: string) {
  runAction(() => usersApi.removeCompanyRole(props.id, companyRoleId))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="user">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ user.displayName }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ user.email }}</p>
        </div>
        <StatusBadge :text="label(ACTIVE_DEACTIVATED, user.status)" :tone="activeDeactivatedTone(user.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <div v-if="canManage" class="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="openEdit"
        >
          Edit display name
        </button>
        <button
          v-if="!isSelf"
          type="button"
          :disabled="actionBusy"
          class="rounded-md border px-3 py-1.5 text-sm font-medium disabled:opacity-50"
          :class="user.status === 0 ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ user.status === 0 ? 'Deactivate' : 'Reactivate' }}
        </button>
      </div>

      <form v-if="editOpen" class="mt-3 flex max-w-lg flex-col gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Display name
          <input v-model="editDisplayName" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <div class="flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Save</button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editOpen = false">Cancel</button>
        </div>
      </form>

      <div class="mt-8 flex items-center justify-between">
        <h2 class="text-lg font-semibold text-slate-900">Company &amp; role assignments</h2>
        <button
          v-if="canManage"
          type="button"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-100"
          @click="openAddAssignment"
        >
          Add assignment
        </button>
      </div>

      <form
        v-if="addAssignmentOpen"
        class="mt-3 flex max-w-xl flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white p-4"
        @submit.prevent="submitAddAssignment"
      >
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Company
          <select v-model="newAssignment.companyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="c in companies" :key="c.id" :value="c.id">{{ c.legalName }}</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Role
          <select v-model="newAssignment.roleId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="r in roles" :key="r.id" :value="r.id">{{ r.name }}</option>
          </select>
        </label>
        <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Assign</button>
        <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="addAssignmentOpen = false">Cancel</button>
      </form>

      <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Company</th>
              <th class="px-4 py-3">Role</th>
              <th v-if="canManage" class="px-4 py-3">Action</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="cr in user.companyRoles" :key="cr.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-900">{{ companyName(cr.companyId) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ cr.roleName }}</td>
              <td v-if="canManage" class="px-4 py-3">
                <button
                  type="button"
                  :disabled="actionBusy"
                  class="rounded-md border border-rose-300 bg-rose-50 px-2.5 py-1 text-xs font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
                  @click="removeAssignment(cr.id)"
                >
                  Remove
                </button>
              </td>
            </tr>
            <tr v-if="user.companyRoles.length === 0">
              <td :colspan="canManage ? 3 : 2" class="px-4 py-6 text-center text-slate-500">No company/role assignments.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
