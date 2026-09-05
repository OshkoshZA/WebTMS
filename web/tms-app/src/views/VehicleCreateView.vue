<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { vehiclesApi } from '../api/vehicles'
import { ApiError } from '../api/client'
import { VEHICLE_TYPE } from '../api/types'

const router = useRouter()

const fleetNo = ref('')
const registration = ref('')
const type = ref(0)
const make = ref('')
const model = ref('')
const licenceExpiry = ref('')
const vehicleTestExpiry = ref('')

const error = ref('')
const submitting = ref(false)

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const vehicle = await vehiclesApi.create({
      fleetNo: fleetNo.value,
      registration: registration.value,
      type: type.value,
      make: make.value || undefined,
      model: model.value || undefined,
      licenceExpiry: licenceExpiry.value || undefined,
      vehicleTestExpiry: vehicleTestExpiry.value || undefined,
    })
    await router.push(`/vehicles/${vehicle.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New vehicle</h1>

    <form class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <div class="grid grid-cols-2 gap-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Fleet no.
          <input v-model="fleetNo" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Registration
          <input v-model="registration" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Type
        <select v-model.number="type" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option v-for="(t, i) in VEHICLE_TYPE" :key="t" :value="i">{{ t }}</option>
        </select>
      </label>

      <div class="grid grid-cols-2 gap-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Make
          <input v-model="make" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Model
          <input v-model="model" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <div class="grid grid-cols-2 gap-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Licence expiry
          <input v-model="licenceExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Vehicle test expiry
          <input v-model="vehicleTestExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : 'Create vehicle' }}
        </button>
        <RouterLink to="/vehicles" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
