<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { rolesApi, functionsApi } from '../api/roles'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { AppFunction, Role } from '../api/types'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const role = ref<Role | null>(null)
const allFunctions = ref<AppFunction[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
// Functions currently mid-grant/revoke — disables just that row's checkbox rather
// than the whole grid, since each toggle is its own independent API call.
const pendingFunctionIds = ref(new Set<string>())

const canManage = computed(() => auth.hasFunction('identity.role.manage'))

const grantedIds = computed(() => new Set((role.value?.functions ?? []).map((f) => f.id)))

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [roleData, functionList] = await Promise.all([rolesApi.get(props.id), functionsApi.list()])
    role.value = roleData
    allFunctions.value = functionList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this role.'
  } finally {
    loading.value = false
  }
}

onMounted(loadEverything)

async function toggleFunction(fn: AppFunction, currentlyGranted: boolean) {
  actionError.value = ''
  pendingFunctionIds.value.add(fn.id)
  try {
    if (currentlyGranted) {
      await rolesApi.revokeFunction(props.id, fn.id)
    } else {
      await rolesApi.grantFunction(props.id, fn.id)
    }
    role.value = await rolesApi.get(props.id)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That change failed — please try again.'
  } finally {
    pendingFunctionIds.value.delete(fn.id)
  }
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="role">
      <h1 class="text-xl font-semibold text-slate-900">{{ role.name }}</h1>
      <p class="mt-1 text-sm text-slate-500">
        {{ role.functions.length }} of {{ allFunctions.length }} functions granted. Each checkbox change applies immediately.
      </p>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Granted</th>
              <th class="px-4 py-3">Code</th>
              <th class="px-4 py-3">Description</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="fn in allFunctions" :key="fn.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3">
                <input
                  type="checkbox"
                  :checked="grantedIds.has(fn.id)"
                  :disabled="!canManage || pendingFunctionIds.has(fn.id)"
                  class="h-4 w-4 rounded border-slate-300"
                  @change="toggleFunction(fn, grantedIds.has(fn.id))"
                />
              </td>
              <td class="px-4 py-3 font-mono text-xs text-slate-900">{{ fn.code }}</td>
              <td class="px-4 py-3 text-slate-600">{{ fn.description }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
