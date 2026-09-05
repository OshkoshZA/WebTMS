<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { expenseTypesApi } from '../api/expenseTypes'
import { ApiError } from '../api/client'
import type { ExpenseType } from '../api/types'

const router = useRouter()

const expenseTypes = ref<ExpenseType[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredExpenseTypes = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return expenseTypes.value
  return expenseTypes.value.filter((t) => t.code.toLowerCase().includes(term) || t.name.toLowerCase().includes(term))
})

onMounted(async () => {
  try {
    expenseTypes.value = await expenseTypesApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of expense types.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Expense types</h1>
      <RouterLink
        to="/expense-types/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New expense type
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by code or name…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredExpenseTypes.length === 0" class="mt-6 text-sm text-slate-500">No expense types found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Code</th>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="type in filteredExpenseTypes"
            :key="type.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/expense-types/${type.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ type.code }}</td>
            <td class="px-4 py-3 text-slate-600">{{ type.name }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="type.active ? 'Active' : 'Inactive'" :tone="type.active ? 'success' : 'neutral'" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
