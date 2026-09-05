<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { locationsApi } from '../api/locations'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { Country, Location } from '../api/types'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const location = ref<Location | null>(null)
const countries = ref<Country[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('location.master.manage'))

function countryName(id: string): string {
  return countries.value.find((c) => c.id === id)?.name ?? id
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [locationData, countryList] = await Promise.all([locationsApi.get(props.id), referenceApi.countries()])
    location.value = locationData
    countries.value = countryList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this location.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function runAction(action: () => Promise<void>) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await action()
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

const editOpen = ref(false)
const editForm = ref({ name: '', province: '', countryId: '' })

function openEdit() {
  if (!location.value) return
  editForm.value = { name: location.value.name, province: location.value.province, countryId: location.value.countryId }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await locationsApi.update(props.id, {
      name: editForm.value.name,
      province: editForm.value.province,
      countryId: editForm.value.countryId,
    })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!location.value) return
  runAction(() => (location.value!.active ? locationsApi.deactivate(props.id) : locationsApi.reactivate(props.id)))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="location">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ location.name }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ location.province }}, {{ countryName(location.countryId) }}</p>
        </div>
        <StatusBadge :text="location.active ? 'Active' : 'Inactive'" :tone="location.active ? 'success' : 'neutral'" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

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
          :class="location.active ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ location.active ? 'Deactivate' : 'Reactivate' }}
        </button>
      </div>

      <form v-if="editOpen" class="mt-3 grid max-w-lg grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Name
          <input v-model="editForm.name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Province
          <input v-model="editForm.province" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Country
          <select v-model="editForm.countryId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option v-for="country in countries" :key="country.id" :value="country.id">{{ country.name }}</option>
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
