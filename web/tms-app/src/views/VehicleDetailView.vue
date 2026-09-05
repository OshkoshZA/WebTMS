<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { vehiclesApi } from '../api/vehicles'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { ACTIVE_DEACTIVATED, VEHICLE_TYPE, label, type Vehicle } from '../api/types'
import { activeDeactivatedTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const vehicle = ref<Vehicle | null>(null)
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('vehicle.master.manage'))

async function loadVehicle() {
  loading.value = true
  error.value = ''
  try {
    vehicle.value = await vehiclesApi.get(props.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this vehicle.'
  } finally {
    loading.value = false
  }
}

onMounted(loadVehicle)

async function runAction(action: () => Promise<void>) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await action()
    await loadVehicle()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

const editOpen = ref(false)
const editForm = ref({ fleetNo: '', registration: '', type: 0, make: '', model: '', licenceExpiry: '', vehicleTestExpiry: '' })

function openEdit() {
  if (!vehicle.value) return
  editForm.value = {
    fleetNo: vehicle.value.fleetNo,
    registration: vehicle.value.registration,
    type: vehicle.value.type,
    make: vehicle.value.make ?? '',
    model: vehicle.value.model ?? '',
    licenceExpiry: vehicle.value.licenceExpiry ?? '',
    vehicleTestExpiry: vehicle.value.vehicleTestExpiry ?? '',
  }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await vehiclesApi.update(props.id, {
      fleetNo: editForm.value.fleetNo,
      registration: editForm.value.registration,
      type: editForm.value.type,
      make: editForm.value.make || undefined,
      model: editForm.value.model || undefined,
      licenceExpiry: editForm.value.licenceExpiry || undefined,
      vehicleTestExpiry: editForm.value.vehicleTestExpiry || undefined,
    })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!vehicle.value) return
  runAction(() => (vehicle.value!.status === 0 ? vehiclesApi.deactivate(props.id) : vehiclesApi.reactivate(props.id)))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="vehicle">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ vehicle.fleetNo }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ vehicle.registration }}</p>
        </div>
        <StatusBadge :text="label(ACTIVE_DEACTIVATED, vehicle.status)" :tone="activeDeactivatedTone(vehicle.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Type</dt>
          <dd class="text-slate-900">{{ label(VEHICLE_TYPE, vehicle.type) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Make / model</dt>
          <dd class="text-slate-900">{{ [vehicle.make, vehicle.model].filter(Boolean).join(' ') || '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Licence expiry</dt>
          <dd class="text-slate-900">{{ vehicle.licenceExpiry ?? '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Vehicle test expiry</dt>
          <dd class="text-slate-900">{{ vehicle.vehicleTestExpiry ?? '—' }}</dd>
        </div>
      </dl>

      <div v-if="canManage" class="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="openEdit"
        >
          Edit details
        </button>
        <button
          type="button"
          :disabled="actionBusy"
          class="rounded-md border px-3 py-1.5 text-sm font-medium disabled:opacity-50"
          :class="vehicle.status === 0 ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ vehicle.status === 0 ? 'Deactivate' : 'Reactivate' }}
        </button>
      </div>

      <form v-if="editOpen" class="mt-3 grid max-w-lg grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Fleet no.
          <input v-model="editForm.fleetNo" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Registration
          <input v-model="editForm.registration" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Type
          <select v-model.number="editForm.type" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option v-for="(t, i) in VEHICLE_TYPE" :key="t" :value="i">{{ t }}</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Make
          <input v-model="editForm.make" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Model
          <input v-model="editForm.model" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Licence expiry
          <input v-model="editForm.licenceExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Vehicle test expiry
          <input v-model="editForm.vehicleTestExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <div class="col-span-2 flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Save</button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editOpen = false">Cancel</button>
        </div>
      </form>
    </template>
  </AppLayout>
</template>
