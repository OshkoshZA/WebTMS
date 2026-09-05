<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { costCentresApi } from '../api/costCentres'
import { ApiError } from '../api/client'
import type { CostCentre } from '../api/types'

const router = useRouter()

const costCentres = ref<CostCentre[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredCostCentres = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return costCentres.value
  return costCentres.value.filter(
    (c) => c.code.toLowerCase().includes(term) || c.name.toLowerCase().includes(term),
  )
})

function parentLabel(parentId: string | null): string {
  if (!parentId) return '—'
  const parent = costCentres.value.find((c) => c.id === parentId)
  return parent ? `${parent.code} — ${parent.name}` : parentId
}

onMounted(async () => {
  try {
    costCentres.value = await costCentresApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of cost centres.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Cost centres</h1>
      <RouterLink
        to="/cost-centres/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New cost centre
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
    <p v-else-if="filteredCostCentres.length === 0" class="mt-6 text-sm text-slate-500">No cost centres found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Code</th>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Parent</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="costCentre in filteredCostCentres"
            :key="costCentre.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/cost-centres/${costCentre.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ costCentre.code }}</td>
            <td class="px-4 py-3 text-slate-600">{{ costCentre.name }}</td>
            <td class="px-4 py-3 text-slate-600">{{ parentLabel(costCentre.parentCostCentreId) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="costCentre.active ? 'Active' : 'Inactive'" :tone="costCentre.active ? 'success' : 'neutral'" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
