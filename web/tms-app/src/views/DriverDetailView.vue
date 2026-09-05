<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { driversApi } from '../api/drivers'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { DRIVER_STATUS, label, type CostCentre, type Driver } from '../api/types'
import { driverStatusTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const driver = ref<Driver | null>(null)
const costCentres = ref<CostCentre[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('driver.master.manage'))
const isDeactivated = computed(() => driver.value?.status === 2)

function costCentreLabel(id: string | null): string {
  if (!id) return '—'
  const cc = costCentres.value.find((c) => c.id === id)
  return cc ? `${cc.code} — ${cc.name}` : id
}

async function loadDriver() {
  loading.value = true
  error.value = ''
  try {
    const [driverData, costCentreList] = await Promise.all([driversApi.get(props.id), referenceApi.costCentres()])
    driver.value = driverData
    costCentres.value = costCentreList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this driver.'
  } finally {
    loading.value = false
  }
}

onMounted(loadDriver)

async function runAction(action: () => Promise<void>) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await action()
    await loadDriver()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// Status is only ever edited here as Active <-> OnLeave — Deactivated is a terminal
// state that only Deactivate/Reactivate below may enter or leave (DriversController's
// own guard rejects an Update that tries to touch it either way), so a Deactivated
// driver's own current status is carried through unchanged rather than exposed as a choice.
const editOpen = ref(false)
const editForm = ref({ name: '', licenceCode: '', licenceExpiry: '', pdpExpiry: '', homeCostCentreId: '', status: 0 })

function openEdit() {
  if (!driver.value) return
  editForm.value = {
    name: driver.value.name,
    licenceCode: driver.value.licenceCode,
    licenceExpiry: driver.value.licenceExpiry ?? '',
    pdpExpiry: driver.value.pdpExpiry ?? '',
    homeCostCentreId: driver.value.homeCostCentreId ?? '',
    status: driver.value.status,
  }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await driversApi.update(props.id, {
      name: editForm.value.name,
      licenceCode: editForm.value.licenceCode,
      licenceExpiry: editForm.value.licenceExpiry || undefined,
      pdpExpiry: editForm.value.pdpExpiry || undefined,
      homeCostCentreId: editForm.value.homeCostCentreId || undefined,
      status: editForm.value.status,
    })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!driver.value) return
  runAction(() => (isDeactivated.value ? driversApi.reactivate(props.id) : driversApi.deactivate(props.id)))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="driver">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ driver.name }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ driver.employeeNo }}</p>
        </div>
        <StatusBadge :text="label(DRIVER_STATUS, driver.status)" :tone="driverStatusTone(driver.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Licence code</dt>
          <dd class="text-slate-900">{{ driver.licenceCode }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Licence expiry</dt>
          <dd class="text-slate-900">{{ driver.licenceExpiry ?? '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">PDP expiry</dt>
          <dd class="text-slate-900">{{ driver.pdpExpiry ?? '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Home cost centre</dt>
          <dd class="text-slate-900">{{ costCentreLabel(driver.homeCostCentreId) }}</dd>
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
          :class="isDeactivated ? 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100' : 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100'"
          @click="toggleActive"
        >
          {{ isDeactivated ? 'Reactivate' : 'Deactivate' }}
        </button>
      </div>

      <form v-if="editOpen" class="mt-3 grid max-w-lg grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Name
          <input v-model="editForm.name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Licence code
          <input v-model="editForm.licenceCode" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label v-if="!isDeactivated" class="flex flex-col gap-1 text-sm text-slate-700">
          Status
          <select v-model.number="editForm.status" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option :value="0">Active</option>
            <option :value="1">On leave</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Licence expiry
          <input v-model="editForm.licenceExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          PDP expiry
          <input v-model="editForm.pdpExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Home cost centre
          <select v-model="editForm.homeCostCentreId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="">None</option>
            <option v-for="cc in costCentres" :key="cc.id" :value="cc.id">{{ cc.code }} — {{ cc.name }}</option>
          </select>
        </label>
        <div class="col-span-2 flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Save</button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editOpen = false">Cancel</button>
        </div>
      </form>
    </template>
  </AppLayout>
</template>
