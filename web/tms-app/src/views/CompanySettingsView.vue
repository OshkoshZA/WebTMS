<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { companiesApi } from '../api/companies'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import type { Company, Country, Currency } from '../api/types'

const auth = useAuthStore()

const company = ref<Company | null>(null)
const countries = ref<Country[]>([])
const currencies = ref<Currency[]>([])

const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)
const saved = ref(false)

const canManage = computed(() => auth.hasFunction('company.master.manage'))
const countryName = computed(() => countries.value.find((c) => c.id === company.value?.countryId)?.name ?? '')
const currencyCode = computed(() => currencies.value.find((c) => c.id === company.value?.currencyId)?.code ?? '')

const form = ref({
  legalName: '', tradingName: '', registrationNo: '', vatNumber: '', physicalAddress: '', postalAddress: '',
  bankingDetails: '', invoiceNumberPrefix: '', logoUrl: '', invoicingEnabled: true,
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [companyData, countryList, currencyList] = await Promise.all([
      companiesApi.get(auth.companyId),
      referenceApi.countries(),
      referenceApi.currencies(),
    ])
    company.value = companyData
    countries.value = countryList
    currencies.value = currencyList
    form.value = {
      legalName: companyData.legalName,
      tradingName: companyData.tradingName ?? '',
      registrationNo: companyData.registrationNo,
      vatNumber: companyData.vatNumber,
      physicalAddress: companyData.physicalAddress,
      postalAddress: companyData.postalAddress,
      bankingDetails: companyData.bankingDetails,
      invoiceNumberPrefix: companyData.invoiceNumberPrefix,
      logoUrl: companyData.logoUrl ?? '',
      invoicingEnabled: companyData.invoicingEnabled,
    }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load company settings.'
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
    await companiesApi.update(auth.companyId, {
      legalName: form.value.legalName,
      tradingName: form.value.tradingName || undefined,
      registrationNo: form.value.registrationNo,
      vatNumber: form.value.vatNumber,
      physicalAddress: form.value.physicalAddress,
      postalAddress: form.value.postalAddress,
      bankingDetails: form.value.bankingDetails,
      invoiceNumberPrefix: form.value.invoiceNumberPrefix,
      logoUrl: form.value.logoUrl || undefined,
      invoicingEnabled: form.value.invoicingEnabled,
    })
    saved.value = true
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not save company settings.'
  } finally {
    actionBusy.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">Company settings</h1>
    <p class="mt-1 text-sm text-slate-500">
      The letterhead master data every invoice, credit note, and load confirmation is built from (§5.1, §06).
    </p>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>

    <template v-else-if="company">
      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />
      <p v-if="saved" class="mt-4 rounded-md bg-emerald-50 px-3 py-2 text-sm text-emerald-800">Saved.</p>

      <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
        <div>
          <dt class="text-slate-500">Country</dt>
          <dd class="text-slate-900">{{ countryName }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Reporting currency</dt>
          <dd class="text-slate-900">{{ currencyCode }}</dd>
        </div>
      </dl>
      <p class="mt-1 text-xs text-slate-500">Country and currency are set once at onboarding and aren't editable here.</p>

      <form class="mt-4 grid max-w-2xl grid-cols-2 gap-4" @submit.prevent="save">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Legal name
          <input v-model="form.legalName" type="text" required :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Trading name
          <input v-model="form.tradingName" type="text" :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Registration no.
          <input v-model="form.registrationNo" type="text" required :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          VAT number
          <input v-model="form.vatNumber" type="text" required :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Physical address
          <input v-model="form.physicalAddress" type="text" required :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Postal address
          <input v-model="form.postalAddress" type="text" required :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="col-span-2 flex flex-col gap-1 text-sm text-slate-700">
          Banking details
          <input v-model="form.bankingDetails" type="text" required :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Invoice number prefix
          <input v-model="form.invoiceNumberPrefix" type="text" required :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Logo URL
          <input v-model="form.logoUrl" type="text" :disabled="!canManage" class="rounded-md border border-slate-300 px-3 py-2 text-sm disabled:bg-slate-50" />
        </label>
        <label class="col-span-2 flex items-center gap-2 text-sm text-slate-700">
          <input v-model="form.invoicingEnabled" type="checkbox" :disabled="!canManage" class="h-4 w-4 rounded border-slate-300" />
          Invoicing enabled (off = load-tracking-only mode, §10)
        </label>

        <button
          v-if="canManage"
          type="submit"
          :disabled="actionBusy"
          class="col-span-2 mt-2 self-start rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ actionBusy ? 'Saving…' : 'Save' }}
        </button>
      </form>
    </template>
  </AppLayout>
</template>
