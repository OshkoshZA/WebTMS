<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { retentionPoliciesApi } from '../api/privacy'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { DATA_CATEGORY } from '../api/types'

const auth = useAuthStore()

interface CategoryRow {
  enabled: boolean
  retentionPeriodYears: number
  legalBasis: string
  anonymizeAfterExpiry: boolean
}

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)
const saved = ref(false)

// One row per DataCategory, always all four shown — enabled/disabled is this
// screen's own concept of "in the next PUT's payload or not", since the API itself
// has no per-category delete, only "leave it out of the whole-set PUT" (§14.2).
const rows = ref<CategoryRow[]>(
  DATA_CATEGORY.map(() => ({ enabled: false, retentionPeriodYears: 7, legalBasis: '', anonymizeAfterExpiry: true })),
)

const canManage = computed(() => auth.hasFunction('privacy.retentionpolicy.manage'))

async function load() {
  loading.value = true
  error.value = ''
  try {
    const policies = await retentionPoliciesApi.list(auth.companyId)
    const byCategory = new Map(policies.map((p) => [p.dataCategory, p]))
    rows.value = DATA_CATEGORY.map((_, i) => {
      const existing = byCategory.get(i)
      return existing
        ? { enabled: true, retentionPeriodYears: existing.retentionPeriodYears, legalBasis: existing.legalBasis, anonymizeAfterExpiry: existing.anonymizeAfterExpiry }
        : { enabled: false, retentionPeriodYears: 7, legalBasis: '', anonymizeAfterExpiry: true }
    })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load retention policies.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function save() {
  actionError.value = ''
  saved.value = false
  actionBusy.value = true
  try {
    const payload = rows.value
      .map((row, i) => ({ row, dataCategory: i }))
      .filter(({ row }) => row.enabled)
      .map(({ row, dataCategory }) => ({
        dataCategory,
        retentionPeriodYears: row.retentionPeriodYears,
        legalBasis: row.legalBasis,
        anonymizeAfterExpiry: row.anonymizeAfterExpiry,
      }))
    await retentionPoliciesApi.set(auth.companyId, payload)
    saved.value = true
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not save retention policies.'
  } finally {
    actionBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Retention policies</h1>
    <p class="mt-1 text-sm text-slate-500">
      Per-category retention configuration (§14.2) for your own company. Not yet enforced anywhere — setting a policy here doesn't yet drive
      automatic anonymization; a data subject's Erasure still has to be requested explicitly (see Data subject requests).
    </p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>

    <template v-else>
      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />
      <p v-if="saved" class="mt-4 rounded-md bg-emerald-50 px-3 py-2 text-sm text-emerald-800">Saved.</p>

      <form class="mt-4 flex flex-col gap-4" @submit.prevent="save">
        <div v-for="(category, i) in DATA_CATEGORY" :key="category" class="rounded-lg border border-slate-200 bg-white p-4">
          <label class="flex items-center gap-2 text-sm font-medium text-slate-900">
            <input v-model="rows[i].enabled" type="checkbox" :disabled="!canManage" class="h-4 w-4 rounded border-slate-300" />
            {{ category }}
          </label>
          <div v-if="rows[i].enabled" class="mt-3 grid grid-cols-2 gap-4">
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Retention period (years)
              <input
                v-model.number="rows[i].retentionPeriodYears"
                type="number"
                min="1"
                required
                :disabled="!canManage"
                class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50"
              />
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Legal basis
              <input
                v-model="rows[i].legalBasis"
                type="text"
                required
                :disabled="!canManage"
                class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50"
              />
            </label>
            <label class="col-span-2 flex items-center gap-2 text-sm text-slate-700">
              <input v-model="rows[i].anonymizeAfterExpiry" type="checkbox" :disabled="!canManage" class="h-4 w-4 rounded border-slate-300" />
              Anonymize after expiry
            </label>
          </div>
        </div>

        <button
          v-if="canManage"
          type="submit"
          :disabled="actionBusy"
          class="mt-2 self-start rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ actionBusy ? 'Saving…' : 'Save' }}
        </button>
      </form>
    </template>
  </AppLayout>
</template>
