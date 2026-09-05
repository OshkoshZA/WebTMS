<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { costCentresApi } from '../api/costCentres'
import { ApiError } from '../api/client'
import type { CostCentre } from '../api/types'

const router = useRouter()

const costCentres = ref<CostCentre[]>([])
const loadingReferenceData = ref(true)

const code = ref('')
const name = ref('')
const parentCostCentreId = ref('')

const error = ref('')
const submitting = ref(false)

onMounted(async () => {
  try {
    costCentres.value = await costCentresApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load existing cost centres.'
  } finally {
    loadingReferenceData.value = false
  }
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const costCentre = await costCentresApi.create({
      code: code.value,
      name: name.value,
      parentCostCentreId: parentCostCentreId.value || undefined,
    })
    await router.push(`/cost-centres/${costCentre.id}`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New cost centre</h1>

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
        Parent cost centre
        <select v-model="parentCostCentreId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
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
          {{ submitting ? 'Creating…' : 'Create cost centre' }}
        </button>
        <RouterLink to="/cost-centres" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
