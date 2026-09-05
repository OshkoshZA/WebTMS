<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { locationsApi } from '../api/locations'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { Country } from '../api/types'

const router = useRouter()

const countries = ref<Country[]>([])
const loadingReferenceData = ref(true)

const name = ref('')
const province = ref('')
const countryId = ref('')

const error = ref('')
const submitting = ref(false)

onMounted(async () => {
  try {
    countries.value = await referenceApi.countries()
    if (countries.value.length === 1) countryId.value = countries.value[0].id
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of countries.'
  } finally {
    loadingReferenceData.value = false
  }
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const location = await locationsApi.create({ name: name.value, province: province.value, countryId: countryId.value })
    await router.push(`/locations/${location.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New location</h1>

    <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form v-else class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Name
        <input v-model="name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Province
        <input v-model="province" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Country
        <select v-model="countryId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select a country…</option>
          <option v-for="country in countries" :key="country.id" :value="country.id">{{ country.name }}</option>
        </select>
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : 'Create location' }}
        </button>
        <RouterLink to="/locations" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
