<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { commoditiesApi } from '../api/commodities'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { COMMODITY_CATEGORY, label, type Commodity, type UnitOfMeasure } from '../api/types'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const commodity = ref<Commodity | null>(null)
const unitsOfMeasure = ref<UnitOfMeasure[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('commodity.master.manage'))

function uomLabel(id: string): string {
  const u = unitsOfMeasure.value.find((uom) => uom.id === id)
  return u ? `${u.code} — ${u.description}` : id
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [commodityData, uomList] = await Promise.all([commoditiesApi.get(props.id), referenceApi.unitsOfMeasure()])
    commodity.value = commodityData
    unitsOfMeasure.value = uomList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this commodity.'
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
const editForm = ref({ code: '', name: '', defaultUnitOfMeasureId: '', category: 0 })

function openEdit() {
  if (!commodity.value) return
  editForm.value = {
    code: commodity.value.code,
    name: commodity.value.name,
    defaultUnitOfMeasureId: commodity.value.defaultUnitOfMeasureId,
    category: commodity.value.category,
  }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await commoditiesApi.update(props.id, {
      code: editForm.value.code,
      name: editForm.value.name,
      defaultUnitOfMeasureId: editForm.value.defaultUnitOfMeasureId,
      category: editForm.value.category,
    })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!commodity.value) return
  runAction(() => (commodity.value!.active ? commoditiesApi.deactivate(props.id) : commoditiesApi.reactivate(props.id)))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="commodity">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ commodity.code }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ commodity.name }}</p>
        </div>
        <StatusBadge :text="commodity.active ? 'Active' : 'Inactive'" :tone="commodity.active ? 'success' : 'neutral'" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Category</dt>
          <dd class="text-slate-900">{{ label(COMMODITY_CATEGORY, commodity.category) }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Default unit of measure</dt>
          <dd class="text-slate-900">{{ uomLabel(commodity.defaultUnitOfMeasureId) }}</dd>
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
          :class="commodity.active ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ commodity.active ? 'Deactivate' : 'Reactivate' }}
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
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Category
          <select v-model.number="editForm.category" class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option v-for="(c, i) in COMMODITY_CATEGORY" :key="c" :value="i">{{ c }}</option>
          </select>
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Default unit of measure
          <select v-model="editForm.defaultUnitOfMeasureId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option v-for="u in unitsOfMeasure" :key="u.id" :value="u.id">{{ u.code }} — {{ u.description }}</option>
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
