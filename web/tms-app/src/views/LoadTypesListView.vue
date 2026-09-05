<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { LoadType } from '../api/types'

const loadTypes = ref<LoadType[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredLoadTypes = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return loadTypes.value
  return loadTypes.value.filter((t) => t.code.toLowerCase().includes(term) || t.description.toLowerCase().includes(term))
})

onMounted(async () => {
  try {
    loadTypes.value = await referenceApi.loadTypes()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of load types.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Load types</h1>
    <p class="mt-1 text-sm text-slate-500">
      Read-only — seeded reference data, no create/edit endpoint exists for this yet.
    </p>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by code or description…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredLoadTypes.length === 0" class="mt-6 text-sm text-slate-500">No load types found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Code</th>
            <th class="px-4 py-3">Description</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="loadType in filteredLoadTypes" :key="loadType.id" class="border-b border-slate-100 last:border-0">
            <td class="px-4 py-3 font-medium text-slate-900">{{ loadType.code }}</td>
            <td class="px-4 py-3 text-slate-600">{{ loadType.description }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
