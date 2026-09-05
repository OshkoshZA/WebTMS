<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { commoditiesApi } from '../api/commodities'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { COMMODITY_CATEGORY, type UnitOfMeasure } from '../api/types'

const router = useRouter()

const unitsOfMeasure = ref<UnitOfMeasure[]>([])
const loadingReferenceData = ref(true)

const code = ref('')
const name = ref('')
const defaultUnitOfMeasureId = ref('')
const category = ref(4) // General — the common case (COMMODITY_CATEGORY[4])

const error = ref('')
const submitting = ref(false)

onMounted(async () => {
  try {
    unitsOfMeasure.value = await referenceApi.unitsOfMeasure()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of units of measure.'
  } finally {
    loadingReferenceData.value = false
  }
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const commodity = await commoditiesApi.create({
      code: code.value,
      name: name.value,
      defaultUnitOfMeasureId: defaultUnitOfMeasureId.value,
      category: category.value,
    })
    await router.push(`/commodities/${commodity.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New commodity</h1>

    <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form v-else class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Code
        <input v-model="code" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Name
        <input v-model="name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Category
        <select v-model.number="category" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option v-for="(c, i) in COMMODITY_CATEGORY" :key="c" :value="i">{{ c }}</option>
        </select>
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Default unit of measure
        <select v-model="defaultUnitOfMeasureId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select…</option>
          <option v-for="u in unitsOfMeasure" :key="u.id" :value="u.id">{{ u.code }} — {{ u.description }}</option>
        </select>
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : 'Create commodity' }}
        </button>
        <RouterLink to="/commodities" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
