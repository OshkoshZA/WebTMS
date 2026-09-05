<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { subcontractorsApi } from '../api/subcontractors'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { ACTIVE_DEACTIVATED, label, type Currency, type Subcontractor, type SubcontractorCurrency } from '../api/types'
import { activeDeactivatedTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const subcontractor = ref<Subcontractor | null>(null)
const currencies = ref<Currency[]>([])
const subcontractorCurrencies = ref<SubcontractorCurrency[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('subcontractor.master.manage'))

function currencyCode(currencyId: string): string {
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}
const primaryCurrencyCode = computed(() => (subcontractor.value ? currencyCode(subcontractor.value.currencyId) : ''))

// Every currency not already the primary and not already on the subcontractor's own
// allow-list — AddCurrency's own duplicate checks, mirrored here so the dropdown
// never even offers a choice the API would reject.
const availableCurrenciesToAdd = computed(() => {
  if (!subcontractor.value) return []
  const taken = new Set([subcontractor.value.currencyId, ...subcontractorCurrencies.value.map((sc) => sc.currencyId)])
  return currencies.value.filter((c) => !taken.has(c.id))
})

async function loadEverything() {
  loading.value = true
  error.value = ''
  try {
    const [subcontractorData, currencyList] = await Promise.all([subcontractorsApi.get(props.id), referenceApi.currencies()])
    subcontractor.value = subcontractorData
    currencies.value = currencyList
    subcontractorCurrencies.value = await subcontractorsApi.currencies(props.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this subcontractor.'
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

// --- Edit core fields ---
const editOpen = ref(false)
const editForm = ref({ name: '', registrationNo: '', insuranceExpiry: '', bankingDetails: '', paymentTermsDays: 30 })

function openEdit() {
  if (!subcontractor.value) return
  editForm.value = {
    name: subcontractor.value.name,
    registrationNo: subcontractor.value.registrationNo,
    insuranceExpiry: subcontractor.value.insuranceExpiry ?? '',
    bankingDetails: subcontractor.value.bankingDetails ?? '',
    paymentTermsDays: subcontractor.value.paymentTermsDays,
  }
  editOpen.value = true
}

function submitEdit() {
  runAction(async () => {
    await subcontractorsApi.update(props.id, {
      name: editForm.value.name,
      registrationNo: editForm.value.registrationNo,
      insuranceExpiry: editForm.value.insuranceExpiry || undefined,
      bankingDetails: editForm.value.bankingDetails || undefined,
      paymentTermsDays: editForm.value.paymentTermsDays,
    })
    editOpen.value = false
  })
}

function toggleActive() {
  if (!subcontractor.value) return
  runAction(() => (subcontractor.value!.status === 0 ? subcontractorsApi.deactivate(props.id) : subcontractorsApi.reactivate(props.id)))
}

// --- Add currency ---
const addCurrencyOpen = ref(false)
const newCurrencyId = ref('')

function openAddCurrency() {
  newCurrencyId.value = ''
  addCurrencyOpen.value = true
}

function submitAddCurrency() {
  runAction(async () => {
    await subcontractorsApi.addCurrency(props.id, newCurrencyId.value)
    addCurrencyOpen.value = false
  })
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="subcontractor">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">{{ subcontractor.name }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ subcontractor.registrationNo }}</p>
        </div>
        <StatusBadge :text="label(ACTIVE_DEACTIVATED, subcontractor.status)" :tone="activeDeactivatedTone(subcontractor.status)" />
      </div>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Primary currency</dt>
          <dd class="text-slate-900">{{ primaryCurrencyCode }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Payment terms</dt>
          <dd class="text-slate-900">{{ subcontractor.paymentTermsDays }} days</dd>
        </div>
        <div>
          <dt class="text-slate-500">Insurance expiry</dt>
          <dd class="text-slate-900">{{ subcontractor.insuranceExpiry ?? '—' }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Banking details</dt>
          <dd class="text-slate-900">{{ subcontractor.bankingDetails ?? '—' }}</dd>
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
          :class="subcontractor.status === 0 ? 'border-rose-300 bg-rose-50 text-rose-800 hover:bg-rose-100' : 'border-sky-300 bg-sky-50 text-sky-800 hover:bg-sky-100'"
          @click="toggleActive"
        >
          {{ subcontractor.status === 0 ? 'Deactivate' : 'Reactivate' }}
        </button>
      </div>

      <form v-if="editOpen" class="mt-3 grid max-w-lg grid-cols-2 gap-4 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitEdit">
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Name
          <input v-model="editForm.name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Registration no.
          <input v-model="editForm.registrationNo" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Insurance expiry
          <input v-model="editForm.insuranceExpiry" type="date" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Payment terms (days)
          <input v-model.number="editForm.paymentTermsDays" type="number" min="0" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Banking details
          <input v-model="editForm.bankingDetails" type="text" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <div class="col-span-2 flex gap-3">
          <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Save</button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="editOpen = false">Cancel</button>
        </div>
      </form>

      <div class="mt-8 flex items-center justify-between">
        <h2 class="text-lg font-semibold text-slate-900">Additional currencies</h2>
        <button
          v-if="canManage"
          type="button"
          class="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-100"
          @click="openAddCurrency"
        >
          Add currency
        </button>
      </div>

      <form
        v-if="addCurrencyOpen"
        class="mt-3 flex max-w-xl flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white p-4"
        @submit.prevent="submitAddCurrency"
      >
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Currency
          <select v-model="newCurrencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="c in availableCurrenciesToAdd" :key="c.id" :value="c.id">{{ c.code }} — {{ c.name }}</option>
          </select>
        </label>
        <button type="submit" :disabled="actionBusy" class="rounded-md bg-slate-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">Add</button>
        <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="addCurrencyOpen = false">Cancel</button>
      </form>

      <div class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Currency</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="sc in subcontractorCurrencies" :key="sc.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-900">{{ currencyCode(sc.currencyId) }}</td>
            </tr>
            <tr v-if="subcontractorCurrencies.length === 0">
              <td class="px-4 py-6 text-center text-slate-500">No additional currencies.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
