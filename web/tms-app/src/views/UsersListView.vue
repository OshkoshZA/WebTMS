<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { usersApi } from '../api/users'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { ACTIVE_DEACTIVATED, label, type User } from '../api/types'
import { activeDeactivatedTone } from '../lib/presentation'

const router = useRouter()
const auth = useAuthStore()

const users = ref<User[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const canManage = computed(() => auth.hasFunction('identity.user.manage'))

const filteredUsers = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return users.value
  return users.value.filter((u) => u.email.toLowerCase().includes(term) || u.displayName.toLowerCase().includes(term))
})

onMounted(async () => {
  try {
    users.value = await usersApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of users.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Users</h1>
      <RouterLink
        v-if="canManage"
        to="/users/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New user
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by email or name…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredUsers.length === 0" class="mt-6 text-sm text-slate-500">No users found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Email</th>
            <th class="px-4 py-3">Display name</th>
            <th class="px-4 py-3">Company/Role assignments</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="user in filteredUsers"
            :key="user.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/users/${user.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ user.email }}</td>
            <td class="px-4 py-3 text-slate-600">{{ user.displayName }}</td>
            <td class="px-4 py-3 text-slate-600">{{ user.companyRoles.length }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(ACTIVE_DEACTIVATED, user.status)" :tone="activeDeactivatedTone(user.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
