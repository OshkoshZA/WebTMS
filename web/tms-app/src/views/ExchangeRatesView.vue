<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { exchangeRatesApi } from '../api/exchangeRates'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { Currency, ExchangeRate } from '../api/types'

const auth = useAuthStore()

const currencies = ref<Currency[]>([])
const loadingReferenceData = ref(true)
const error = ref('')

const canManage = computed(() => auth.hasFunction('finance.exchangerate.manage'))

function currencyCode(id: string): string {
  return currencies.value.find((c) => c.id === id)?.code ?? id
}

onMounted(async () => {
  try {
    currencies.value = await referenceApi.currencies()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of currencies.'
  } finally {
    loadingReferenceData.value = false
  }
})

// --- Capture / override a rate ---
const capture = ref({ fromCurrencyId: '', toCurrencyId: '', effectiveDate: '', rate: 0 })
const captureError = ref('')
const captureBusy = ref(false)
const captureResult = ref<ExchangeRate | null>(null)

async function submitCapture() {
  captureError.value = ''
  captureResult.value = null
  captureBusy.value = true
  try {
    captureResult.value = await exchangeRatesApi.capture(capture.value)
  } catch (e) {
    captureError.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    captureBusy.value = false
  }
}

// --- Look up the rate effective on a date ---
const lookup = ref({ fromCurrencyId: '', toCurrencyId: '', date: '' })
const lookupError = ref('')
const lookupBusy = ref(false)
const lookupResult = ref<ExchangeRate | null>(null)
const lookupNotFound = ref(false)

async function submitLookup() {
  lookupError.value = ''
  lookupResult.value = null
  lookupNotFound.value = false
  lookupBusy.value = true
  try {
    lookupResult.value = await exchangeRatesApi.get(lookup.value.fromCurrencyId, lookup.value.toCurrencyId, lookup.value.date)
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) {
      lookupNotFound.value = true
    } else {
      lookupError.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
    }
  } finally {
    lookupBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Exchange rates</h1>
    <p class="mt-1 text-sm text-slate-500">
      No browsable history exists — a rate is only ever captured or looked up by currency pair and date (§4.3).
    </p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <template v-else>
      <div class="mt-6 grid gap-6 lg:grid-cols-2">
        <div class="rounded-lg border border-slate-200 bg-white p-4">
          <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Look up a rate</h2>
          <p class="mt-1 text-xs text-slate-500">Resolves to the most recently captured rate on or before the given date.</p>
          <form class="mt-4 flex flex-col gap-3" @submit.prevent="submitLookup">
            <ErrorAlert v-if="lookupError" :message="lookupError" />
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              From currency
              <select v-model="lookup.fromCurrencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                <option value="" disabled>Select…</option>
                <option v-for="c in currencies" :key="c.id" :value="c.id">{{ c.code }} — {{ c.name }}</option>
              </select>
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              To currency
              <select v-model="lookup.toCurrencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                <option value="" disabled>Select…</option>
                <option v-for="c in currencies" :key="c.id" :value="c.id">{{ c.code }} — {{ c.name }}</option>
              </select>
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Date
              <input v-model="lookup.date" type="date" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
            </label>
            <button type="submit" :disabled="lookupBusy" class="self-start rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50">
              {{ lookupBusy ? 'Looking up…' : 'Look up' }}
            </button>
          </form>
          <p v-if="lookupNotFound" class="mt-3 text-sm text-amber-700">No captured rate for that currency pair on or before that date.</p>
          <dl v-if="lookupResult" class="mt-3 grid grid-cols-2 gap-3 rounded-md bg-slate-50 p-3 text-sm">
            <div>
              <dt class="text-slate-500">Rate</dt>
              <dd class="font-medium text-slate-900">
                1 {{ currencyCode(lookupResult.fromCurrencyId) }} = {{ lookupResult.rate }} {{ currencyCode(lookupResult.toCurrencyId) }}
              </dd>
            </div>
            <div>
              <dt class="text-slate-500">Effective date</dt>
              <dd class="text-slate-900">{{ lookupResult.effectiveDate }}</dd>
            </div>
          </dl>
        </div>

        <div v-if="canManage" class="rounded-lg border border-slate-200 bg-white p-4">
          <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Capture a rate</h2>
          <p class="mt-1 text-xs text-slate-500">A second capture for the same pair and date overrides it — never a second row.</p>
          <form class="mt-4 flex flex-col gap-3" @submit.prevent="submitCapture">
            <ErrorAlert v-if="captureError" :message="captureError" />
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              From currency
              <select v-model="capture.fromCurrencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                <option value="" disabled>Select…</option>
                <option v-for="c in currencies" :key="c.id" :value="c.id">{{ c.code }} — {{ c.name }}</option>
              </select>
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              To currency
              <select v-model="capture.toCurrencyId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
                <option value="" disabled>Select…</option>
                <option v-for="c in currencies" :key="c.id" :value="c.id">{{ c.code }} — {{ c.name }}</option>
              </select>
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Effective date
              <input v-model="capture.effectiveDate" type="date" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Rate
              <input v-model.number="capture.rate" type="number" step="any" min="0" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
            </label>
            <button type="submit" :disabled="captureBusy" class="self-start rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50">
              {{ captureBusy ? 'Saving…' : 'Capture rate' }}
            </button>
          </form>
          <dl v-if="captureResult" class="mt-3 grid grid-cols-2 gap-3 rounded-md bg-emerald-50 p-3 text-sm">
            <div>
              <dt class="text-slate-500">Saved</dt>
              <dd class="font-medium text-slate-900">
                1 {{ currencyCode(captureResult.fromCurrencyId) }} = {{ captureResult.rate }} {{ currencyCode(captureResult.toCurrencyId) }}
              </dd>
            </div>
            <div>
              <dt class="text-slate-500">Effective date</dt>
              <dd class="text-slate-900">{{ captureResult.effectiveDate }}</dd>
            </div>
          </dl>
        </div>
      </div>
    </template>
  </AppLayout>
</template>
