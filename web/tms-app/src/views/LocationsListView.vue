<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { locationsApi } from '../api/locations'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { Country, Location } from '../api/types'

const router = useRouter()

const locations = ref<Location[]>([])
const countries = ref<Country[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredLocations = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return locations.value
  return locations.value.filter((l) => l.name.toLowerCase().includes(term) || l.province.toLowerCase().includes(term))
})

function countryName(id: string): string {
  return countries.value.find((c) => c.id === id)?.name ?? id
}

onMounted(async () => {
  try {
    const [locationList, countryList] = await Promise.all([locationsApi.list(), referenceApi.countries()])
    locations.value = locationList
    countries.value = countryList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of locations.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Locations</h1>
      <RouterLink
        to="/locations/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New location
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by name or province…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredLocations.length === 0" class="mt-6 text-sm text-slate-500">No locations found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Province</th>
            <th class="px-4 py-3">Country</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="location in filteredLocations"
            :key="location.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/locations/${location.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ location.name }}</td>
            <td class="px-4 py-3 text-slate-600">{{ location.province }}</td>
            <td class="px-4 py-3 text-slate-600">{{ countryName(location.countryId) }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="location.active ? 'Active' : 'Inactive'" :tone="location.active ? 'success' : 'neutral'" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
