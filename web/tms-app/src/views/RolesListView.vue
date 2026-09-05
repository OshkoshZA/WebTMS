<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { rolesApi } from '../api/roles'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { Role } from '../api/types'

const router = useRouter()
const auth = useAuthStore()

const roles = ref<Role[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const canManage = computed(() => auth.hasFunction('identity.role.manage'))

const filteredRoles = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return roles.value
  return roles.value.filter((r) => r.name.toLowerCase().includes(term))
})

onMounted(async () => {
  try {
    roles.value = await rolesApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of roles.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Roles</h1>
      <RouterLink
        v-if="canManage"
        to="/roles/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New role
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by name…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredRoles.length === 0" class="mt-6 text-sm text-slate-500">No roles found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Functions granted</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="role in filteredRoles"
            :key="role.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/roles/${role.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ role.name }}</td>
            <td class="px-4 py-3 text-slate-600">{{ role.functions.length }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
