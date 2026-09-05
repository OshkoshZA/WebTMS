<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { costCentresApi } from '../api/costCentres'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { CostCentre } from '../api/types'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const costCentre = ref<CostCentre | null>(null)
const allCostCentres = ref<CostCentre[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('costcentre.master.manage'))

function parentLabel(parentId: string | null): string {
  if (!parentId) return '—'
  const parent = allCostCentres.value.find((c) => c.id === parentId)
  return parent ? `${parent.code} — ${parent.name}` : parentId
}

// Excludes itself — a cost centre can never be its own parent (CostCentresController's
// own guard); deeper multi-level cycles are still possible to pick here and are caught
// server-side, surfaced as the API's own error message.
const availableParents = computed(() => allCostCentres.value.filter((c) => c.id !== props.id))

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [costCentreData, allData] = await Promise.all([costCentresApi.get(props.id), costCentresApi.list()])
    costCentre.value = costCentreData
    allCostCentres.value = allData
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this cost centre.'
  } finally {
    loading.value = false
  }
}

onMounted(loadEverything)

async function runAction(action: () => Promise<void>) {
  actionError.value = ''
  actionBusy.value = true
  try {
    await action()
    await loadEverything()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

const editOpen = ref(false)
const editForm = ref({ code: '', name: '', parentCostCentreId: '' })

function openEdit() {
  if (!costCentre.value) return
  editForm.value = {
    code: costCentre.value.code,
    name: costCentre.value.name,
    parentCostCentreId: costCentre.value.parentCostCentreId ?? '',
  }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await costCentresApi.update(props.id, {
      code: editForm.value.code,
      name: editForm.value.name,
      parentCostCentreId: editForm.value.parentCostCentreId || undefined,
    })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!costCentre.value) return
  runAction(() => (costCentre.value!.active ? costCentresApi.deactivate(props.id) : costCentresApi.reactivate(props.id)))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="costCentre">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ costCentre.code }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ costCentre.name }}</p>
        </div>
        <StatusBadge :text="costCentre.active ? 'Active' : 'Inactive'" :tone="costCentre.active ? 'success' : 'neutral'" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Parent cost centre</dt>
          <dd class="text-slate-900">{{ parentLabel(costCentre.parentCostCentreId) }}</dd>
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
          :class="costCentre.active ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ costCentre.active ? 'Deactivate' : 'Reactivate' }}
        </button>
      </div>

      <form v-if="editOpen" class="mt-3 grid max-w-lg grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Code
          <input v-model="editForm.code" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Name
          <input v-model="editForm.name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Parent cost centre
          <select v-model="editForm.parentCostCentreId" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="">None</option>
            <option v-for="cc in availableParents" :key="cc.id" :value="cc.id">{{ cc.code }} — {{ cc.name }}</option>
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
