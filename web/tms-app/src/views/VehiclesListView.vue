<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { vehiclesApi } from '../api/vehicles'
import { ApiError } from '../api/client'
import { ACTIVE_DEACTIVATED, VEHICLE_TYPE, label, type Vehicle } from '../api/types'
import { activeDeactivatedTone } from '../lib/presentation'

const router = useRouter()

const vehicles = ref<Vehicle[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const filteredVehicles = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return vehicles.value
  return vehicles.value.filter(
    (v) => v.fleetNo.toLowerCase().includes(term) || v.registration.toLowerCase().includes(term),
  )
})

onMounted(async () => {
  try {
    vehicles.value = await vehiclesApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of vehicles.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Vehicles</h1>
      <RouterLink
        to="/vehicles/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New vehicle
      </RouterLink>
    </div>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by fleet no. or registration…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredVehicles.length === 0" class="mt-6 text-sm text-slate-500">No vehicles found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Fleet no.</th>
            <th class="px-4 py-3">Registration</th>
            <th class="px-4 py-3">Type</th>
            <th class="px-4 py-3">Make / model</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="vehicle in filteredVehicles"
            :key="vehicle.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/vehicles/${vehicle.id}`)"
          >
            <td class="px-4 py-3 font-medium text-slate-900">{{ vehicle.fleetNo }}</td>
            <td class="px-4 py-3 text-slate-600">{{ vehicle.registration }}</td>
            <td class="px-4 py-3 text-slate-600">{{ label(VEHICLE_TYPE, vehicle.type) }}</td>
            <td class="px-4 py-3 text-slate-600">{{ [vehicle.make, vehicle.model].filter(Boolean).join(' ') || '—' }}</td>
            <td class="px-4 py-3"><StatusBadge :text="label(ACTIVE_DEACTIVATED, vehicle.status)" :tone="activeDeactivatedTone(vehicle.status)" /></td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
