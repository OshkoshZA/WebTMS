<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { expenseTypesApi } from '../api/expenseTypes'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { ExpenseType } from '../api/types'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const expenseType = ref<ExpenseType | null>(null)
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('expensetype.master.manage'))

async function load() {
  loading.value = true
  error.value = ''
  try {
    expenseType.value = await expenseTypesApi.get(props.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this expense type.'
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
const editForm = ref({ code: '', name: '' })

function openEdit() {
  if (!expenseType.value) return
  editForm.value = { code: expenseType.value.code, name: expenseType.value.name }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await expenseTypesApi.update(props.id, { code: editForm.value.code, name: editForm.value.name })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!expenseType.value) return
  runAction(() => (expenseType.value!.active ? expenseTypesApi.deactivate(props.id) : expenseTypesApi.reactivate(props.id)))
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="expenseType">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ expenseType.code }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ expenseType.name }}</p>
        </div>
        <StatusBadge :text="expenseType.active ? 'Active' : 'Inactive'" :tone="expenseType.active ? 'success' : 'neutral'" />
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
          :class="expenseType.active ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ expenseType.active ? 'Deactivate' : 'Reactivate' }}
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
        <div class="col-span-2 flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Save</button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editOpen = false">Cancel</button>
        </div>
      </form>
    </template>
  </AppLayout>
</template>
