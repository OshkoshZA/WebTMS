<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { usersApi } from '../api/users'
import { rolesApi } from '../api/roles'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { Company, Role } from '../api/types'

const router = useRouter()

const companies = ref<Company[]>([])
const roles = ref<Role[]>([])
const loadingReferenceData = ref(true)

const email = ref('')
const password = ref('')
const displayName = ref('')
const initialCompanyId = ref('')
const initialRoleId = ref('')

const error = ref('')
const submitting = ref(false)

onMounted(async () => {
  try {
    const [companyList, roleList] = await Promise.all([referenceApi.companies(), rolesApi.list()])
    companies.value = companyList
    roles.value = roleList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load companies and roles.'
  } finally {
    loadingReferenceData.value = false
  }
})

async function submit() {
  error.value = ''
  // Mirrors CreateUserRequest's own rule server-side: an initial Company/Role
  // assignment is both-or-neither, never just one.
  if (Boolean(initialCompanyId.value) !== Boolean(initialRoleId.value)) {
    error.value = 'Choose both an initial company and role, or leave both blank.'
    return
  }
  submitting.value = true
  try {
    const user = await usersApi.create({
      email: email.value,
      password: password.value,
      displayName: displayName.value,
      initialCompanyId: initialCompanyId.value || undefined,
      initialRoleId: initialRoleId.value || undefined,
    })
    await router.push(`/users/${user.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New user</h1>

    <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form v-else class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Email
        <input v-model="email" type="email" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Password
        <input v-model="password" type="password" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Display name
        <input v-model="displayName" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <p class="mt-2 text-xs uppercase tracking-wide text-slate-500">Initial company &amp; role (optional)</p>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Company
        <select v-model="initialCompanyId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="">None</option>
          <option v-for="c in companies" :key="c.id" :value="c.id">{{ c.legalName }}</option>
        </select>
      </label>
      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Role
        <select v-model="initialRoleId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="">None</option>
          <option v-for="r in roles" :key="r.id" :value="r.id">{{ r.name }}</option>
        </select>
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : 'Create user' }}
        </button>
        <RouterLink to="/users" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
