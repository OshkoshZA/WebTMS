<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { subcontractorsApi } from '../api/subcontractors'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { Currency } from '../api/types'

const router = useRouter()

const currencies = ref<Currency[]>([])
const loadingReferenceData = ref(true)

const name = ref('')
const registrationNo = ref('')
const currencyId = ref('')
const insuranceExpiry = ref('')
const bankingDetails = ref('')
const paymentTermsDays = ref(30)

const error = ref('')
const submitting = ref(false)

onMounted(async () => {
  try {
    currencies.value = await referenceApi.currencies()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load currencies.'
  } finally {
    loadingReferenceData.value = false
  }
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const subcontractor = await subcontractorsApi.create({
      name: name.value,
      registrationNo: registrationNo.value,
      currencyId: currencyId.value,
      insuranceExpiry: insuranceExpiry.value || undefined,
      bankingDetails: bankingDetails.value || undefined,
      paymentTermsDays: paymentTermsDays.value,
    })
    await router.push(`/subcontractors/${subcontractor.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New subcontractor</h1>

    <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form v-else class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Name
        <input v-model="name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Registration no.
        <input v-model="registrationNo" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Currency
        <select v-model="currencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select a currency…</option>
          <option v-for="currency in currencies" :key="currency.id" :value="currency.id">
            {{ currency.code }} — {{ currency.name }}
          </option>
        </select>
      </label>

      <div class="grid grid-cols-2 gap-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Insurance expiry
          <input v-model="insuranceExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Payment terms (days)
          <input v-model.number="paymentTermsDays" type="number" min="0" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Banking details
        <input v-model="bankingDetails" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : 'Create subcontractor' }}
        </button>
        <RouterLink to="/subcontractors" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
