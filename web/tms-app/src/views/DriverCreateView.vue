<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { driversApi } from '../api/drivers'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { CostCentre } from '../api/types'

const router = useRouter()

const costCentres = ref<CostCentre[]>([])
const loadingReferenceData = ref(true)

const employeeNo = ref('')
const name = ref('')
const licenceCode = ref('')
const licenceExpiry = ref('')
const pdpExpiry = ref('')
const homeCostCentreId = ref('')

const error = ref('')
const submitting = ref(false)

onMounted(async () => {
  try {
    costCentres.value = await referenceApi.costCentres()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load cost centres.'
  } finally {
    loadingReferenceData.value = false
  }
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const driver = await driversApi.create({
      employeeNo: employeeNo.value,
      name: name.value,
      licenceCode: licenceCode.value,
      licenceExpiry: licenceExpiry.value || undefined,
      pdpExpiry: pdpExpiry.value || undefined,
      homeCostCentreId: homeCostCentreId.value || undefined,
    })
    await router.push(`/drivers/${driver.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New driver</h1>

    <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form v-else class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <div class="grid grid-cols-2 gap-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Employee no.
          <input v-model="employeeNo" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Name
          <input v-model="name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Licence code
        <input v-model="licenceCode" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <div class="grid grid-cols-2 gap-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Licence expiry
          <input v-model="licenceExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          PDP expiry
          <input v-model="pdpExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Home cost centre
        <select v-model="homeCostCentreId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="">None</option>
          <option v-for="cc in costCentres" :key="cc.id" :value="cc.id">{{ cc.code }} — {{ cc.name }}</option>
        </select>
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : 'Create driver' }}
        </button>
        <RouterLink to="/drivers" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
