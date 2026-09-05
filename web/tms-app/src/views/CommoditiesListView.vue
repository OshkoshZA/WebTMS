<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { commoditiesApi } from '../api/commodities'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { COMMODITY_CATEGORY, label, type Commodity, type UnitOfMeasure } from '../api/types'

const router = useRouter()

const commodities = ref<Commodity[]>([])
const unitsOfMeasure = ref<UnitOfMeasure[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredCommodities = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return commodities.value
  return commodities.value.filter((c) => c.code.toLowerCase().includes(term) || c.name.toLowerCase().includes(term))
})

function uomCode(id: string): string {
  return unitsOfMeasure.value.find((u) => u.id === id)?.code ?? id
}

onMounted(async () => {
  try {
    const [commodityList, uomList] = await Promise.all([commoditiesApi.list(), referenceApi.unitsOfMeasure()])
    commodities.value = commodityList
    unitsOfMeasure.value = uomList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of commodities.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Commodities</h1>
      <RouterLink
        to="/commodities/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New commodity
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
    <p v-else-if="filteredCommodities.length === 0" class="mt-6 text-sm text-slate-500">No commodities found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Code</th>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Category</th>
            <th class="px-4 py-3">Default unit</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="commodity in filteredCommodities"
            :key="commodity.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/commodities/${commodity.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ commodity.code }}</td>
            <td class="px-4 py-3 text-slate-600">{{ commodity.name }}</td>
            <td class="px-4 py-3 text-slate-600">{{ label(COMMODITY_CATEGORY, commodity.category) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ uomCode(commodity.defaultUnitOfMeasureId) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="commodity.active ? 'Active' : 'Inactive'" :tone="commodity.active ? 'success' : 'neutral'" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
