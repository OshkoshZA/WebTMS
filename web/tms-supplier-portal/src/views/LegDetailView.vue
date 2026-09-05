<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { accrualsApi } from '../api/accruals'
import { legsApi } from '../api/legs'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  CLAIMED_AGAINST,
  CONFIRMATION_STATUS,
  DEBRIEF_STATUS,
  INCIDENT_SEVERITY,
  INCIDENT_TYPE,
  LEG_STATUS,
  label,
  type Currency,
  type Debrief,
  type ExpenseType,
  type SubcontractorAccrual,
  type SubcontractorLeg,
  type SubmitDebriefExpenseRequest,
  type SubmitDebriefIncidentRequest,
} from '../api/types'
import { confirmationStatusTone, debriefStatusTone, formatDateTime, formatMoney, legStatusTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const leg = ref<SubcontractorLeg | null>(null)
const debrief = ref<Debrief | null>(null)
const currencies = ref<Currency[]>([])
const expenseTypes = ref<ExpenseType[]>([])
const accruals = ref<SubcontractorAccrual[]>([])
const loading = ref(true)
const error = ref('')

const confirmationBusy = ref(false)
const confirmationError = ref('')
const declineReason = ref('')
const showDeclineForm = ref(false)

const debriefSubmitting = ref(false)
const debriefError = ref('')

function currencyCode(currencyId: string | null): string {
  if (!currencyId) return ''
  return currencies.value.find((c) => c.id === currencyId)?.code ?? currencyId
}

const canSubmitDebrief = computed(() => leg.value?.status === 3) // LEG_STATUS[3] === 'Delivered'

interface IncidentDraft {
  type: number
  severity: number
  narrative: string
}

interface ExpenseDraft {
  expenseTypeId: string
  description: string
  amount: string
  currencyId: string
  receiptImageUrl: string
  claimedAgainst: number
  accrualId: string
}

const form = ref({
  odometerStart: '',
  odometerEnd: '',
  fuelLitres: '',
  fuelCost: '',
  drivingHours: '',
  podReceived: false,
  podImageUrl: '',
})
const incidents = ref<IncidentDraft[]>([])
const expenses = ref<ExpenseDraft[]>([])

function addIncident() {
  incidents.value.push({ type: 0, severity: 0, narrative: '' })
}
function removeIncident(index: number) {
  incidents.value.splice(index, 1)
}

function addExpense() {
  expenses.value.push({
    expenseTypeId: '',
    description: '',
    amount: '',
    currencyId: '',
    receiptImageUrl: '',
    claimedAgainst: 0,
    accrualId: '',
  })
}
function removeExpense(index: number) {
  expenses.value.splice(index, 1)
}

function toOptionalNumber(value: string): number | undefined {
  const trimmed = value.trim()
  return trimmed === '' ? undefined : Number(trimmed)
}

async function loadAll() {
  loading.value = true
  error.value = ''
  try {
    const [legList, currencyList, expenseTypeList, accrualList] = await Promise.all([
      legsApi.listForSubcontractor(auth.subcontractorId),
      referenceApi.currencies(),
      referenceApi.expenseTypes(),
      accrualsApi.list(),
    ])
    currencies.value = currencyList
    expenseTypes.value = expenseTypeList
    accruals.value = accrualList.filter((a) => a.status === 0) // ACCRUAL_STATUS[0] === 'Accrued'

    const found = legList.find((l) => l.id === props.id)
    if (!found) {
      error.value = 'Leg not found.'
      return
    }
    leg.value = found

    try {
      debrief.value = await legsApi.getDebrief(props.id)
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) {
        debrief.value = null
      } else {
        throw e
      }
    }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this leg.'
  } finally {
    loading.value = false
  }
}

onMounted(loadAll)

async function acknowledge() {
  if (!leg.value) return
  confirmationBusy.value = true
  confirmationError.value = ''
  try {
    await legsApi.acknowledgeConfirmation(leg.value.id, { acknowledged: true })
    leg.value.confirmation = await legsApi.getConfirmation(leg.value.id)
  } catch (e) {
    confirmationError.value = e instanceof ApiError ? e.message : 'Could not accept this confirmation.'
  } finally {
    confirmationBusy.value = false
  }
}

async function decline() {
  if (!leg.value) return
  confirmationBusy.value = true
  confirmationError.value = ''
  try {
    await legsApi.acknowledgeConfirmation(leg.value.id, { acknowledged: false, reason: declineReason.value || undefined })
    leg.value.confirmation = await legsApi.getConfirmation(leg.value.id)
    showDeclineForm.value = false
  } catch (e) {
    confirmationError.value = e instanceof ApiError ? e.message : 'Could not decline this confirmation.'
  } finally {
    confirmationBusy.value = false
  }
}

async function submitDebrief() {
  if (!leg.value) return
  debriefError.value = ''
  debriefSubmitting.value = true
  try {
    const incidentRequests: SubmitDebriefIncidentRequest[] = incidents.value.map((i) => ({
      type: i.type,
      severity: i.severity,
      narrative: i.narrative,
    }))
    const expenseRequests: SubmitDebriefExpenseRequest[] = expenses.value.map((e) => ({
      expenseTypeId: e.expenseTypeId,
      description: e.description,
      amount: Number(e.amount),
      currencyId: e.currencyId,
      receiptImageUrl: e.receiptImageUrl || undefined,
      claimedAgainst: e.claimedAgainst,
      accrualId: e.claimedAgainst === 1 ? e.accrualId || undefined : undefined,
    }))

    debrief.value = await legsApi.submitDebrief(leg.value.id, {
      odometerStart: toOptionalNumber(form.value.odometerStart),
      odometerEnd: toOptionalNumber(form.value.odometerEnd),
      fuelLitres: toOptionalNumber(form.value.fuelLitres),
      fuelCost: toOptionalNumber(form.value.fuelCost),
      drivingHours: toOptionalNumber(form.value.drivingHours),
      podReceived: form.value.podReceived,
      podImageUrl: form.value.podImageUrl || undefined,
      incidents: incidentRequests.length ? incidentRequests : undefined,
      expenses: expenseRequests.length ? expenseRequests : undefined,
    })
  } catch (e) {
    debriefError.value = e instanceof ApiError ? e.message : 'Could not submit this debrief.'
  } finally {
    debriefSubmitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="leg">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="text-xl font-semibold text-slate-900">Leg #{{ leg.sequenceNo }}</h1>
          <p class="mt-1 text-sm text-slate-600">
            Buy amount: {{ formatMoney(leg.buyAmount, currencyCode(leg.buyCurrencyId)) }}
          </p>
        </div>
        <StatusBadge :text="label(LEG_STATUS, leg.status)" :tone="legStatusTone(leg.status)" />
      </div>

      <!-- Confirmation -->
      <section class="mt-8">
        <h2 class="text-lg font-semibold text-slate-900">Confirmation</h2>
        <div v-if="leg.confirmation" class="mt-4 rounded-lg border border-slate-200 bg-white p-4">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm text-slate-900">{{ leg.confirmation.documentNumber }}</p>
              <p class="text-sm text-slate-500">Issued {{ formatDateTime(leg.confirmation.issuedDate) }}</p>
            </div>
            <StatusBadge
              :text="label(CONFIRMATION_STATUS, leg.confirmation.status)"
              :tone="confirmationStatusTone(leg.confirmation.status)"
            />
          </div>
          <p v-if="leg.confirmation.declineReason" class="mt-2 text-sm text-rose-700">
            Decline reason: {{ leg.confirmation.declineReason }}
          </p>

          <ErrorAlert v-if="confirmationError" :message="confirmationError" class="mt-3" />

          <div v-if="leg.confirmation.status === 0" class="mt-4">
            <div v-if="!showDeclineForm" class="flex gap-2">
              <button
                type="button"
                :disabled="confirmationBusy"
                class="rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
                @click="acknowledge"
              >
                Accept
              </button>
              <button
                type="button"
                :disabled="confirmationBusy"
                class="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                @click="showDeclineForm = true"
              >
                Decline
              </button>
            </div>
            <div v-else class="flex flex-col gap-2">
              <textarea
                v-model="declineReason"
                rows="2"
                placeholder="Reason for declining (optional)"
                class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
              />
              <div class="flex gap-2">
                <button
                  type="button"
                  :disabled="confirmationBusy"
                  class="rounded-md bg-rose-700 px-3 py-2 text-sm font-medium text-white hover:bg-rose-800 disabled:opacity-50"
                  @click="decline"
                >
                  Confirm decline
                </button>
                <button
                  type="button"
                  class="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
                  @click="showDeclineForm = false"
                >
                  Cancel
                </button>
              </div>
            </div>
          </div>
        </div>
        <p v-else class="mt-4 text-sm text-slate-500">No load confirmation has been issued for this leg yet.</p>
      </section>

      <!-- Debrief -->
      <section class="mt-8">
        <h2 class="text-lg font-semibold text-slate-900">Debrief</h2>

        <div v-if="debrief" class="mt-4 rounded-lg border border-slate-200 bg-white p-4">
          <div class="flex items-center justify-between">
            <p class="text-sm text-slate-500">Submitted {{ formatDateTime(debrief.submittedAt) }}</p>
            <StatusBadge :text="label(DEBRIEF_STATUS, debrief.status)" :tone="debriefStatusTone(debrief.status)" />
          </div>
          <p v-if="debrief.exceptionReasons" class="mt-2 text-sm text-amber-700">{{ debrief.exceptionReasons }}</p>

          <dl class="mt-4 grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
            <div><dt class="text-slate-500">Odometer</dt><dd class="text-slate-900">{{ debrief.odometerStart ?? '—' }} → {{ debrief.odometerEnd ?? '—' }}</dd></div>
            <div><dt class="text-slate-500">Fuel</dt><dd class="text-slate-900">{{ debrief.fuelLitres ?? '—' }} L / {{ debrief.fuelCost ?? '—' }}</dd></div>
            <div><dt class="text-slate-500">Driving hours</dt><dd class="text-slate-900">{{ debrief.drivingHours ?? '—' }}</dd></div>
            <div><dt class="text-slate-500">POD received</dt><dd class="text-slate-900">{{ debrief.podReceived ? 'Yes' : 'No' }}</dd></div>
            <div v-if="debrief.podImageUrl"><dt class="text-slate-500">POD image</dt><dd class="truncate text-slate-900">{{ debrief.podImageUrl }}</dd></div>
          </dl>

          <template v-if="debrief.incidents.length">
            <h3 class="mt-6 text-sm font-semibold text-slate-900">Incidents</h3>
            <ul class="mt-2 flex flex-col gap-2">
              <li v-for="incident in debrief.incidents" :key="incident.id" class="rounded-md border border-slate-200 p-3 text-sm">
                <span class="font-medium text-slate-900">{{ label(INCIDENT_TYPE, incident.type) }}</span>
                <span class="ml-2 text-slate-500">{{ label(INCIDENT_SEVERITY, incident.severity) }}</span>
                <p class="mt-1 text-slate-600">{{ incident.narrative }}</p>
              </li>
            </ul>
          </template>

          <template v-if="debrief.expenses.length">
            <h3 class="mt-6 text-sm font-semibold text-slate-900">Expenses</h3>
            <ul class="mt-2 flex flex-col gap-2">
              <li v-for="expense in debrief.expenses" :key="expense.id" class="rounded-md border border-slate-200 p-3 text-sm">
                <div class="flex items-center justify-between">
                  <span class="font-medium text-slate-900">{{ expense.description }}</span>
                  <span class="text-slate-900">{{ formatMoney(expense.amount, currencyCode(expense.currencyId)) }}</span>
                </div>
                <p class="mt-1 text-slate-500">Claimed against: {{ label(CLAIMED_AGAINST, expense.claimedAgainst) }}</p>
              </li>
            </ul>
          </template>
        </div>

        <p v-else-if="!canSubmitDebrief" class="mt-4 text-sm text-slate-500">
          A debrief can be submitted once this leg is Delivered.
        </p>

        <form v-else class="mt-4 flex flex-col gap-6 rounded-lg border border-slate-200 bg-white p-4" @submit.prevent="submitDebrief">
          <ErrorAlert v-if="debriefError" :message="debriefError" />

          <div class="grid grid-cols-2 gap-4 sm:grid-cols-3">
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Odometer start
              <input v-model="form.odometerStart" type="number" step="any" class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none" />
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Odometer end
              <input v-model="form.odometerEnd" type="number" step="any" class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none" />
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Driving hours
              <input v-model="form.drivingHours" type="number" step="any" class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none" />
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Fuel (litres)
              <input v-model="form.fuelLitres" type="number" step="any" class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none" />
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              Fuel cost
              <input v-model="form.fuelCost" type="number" step="any" class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none" />
            </label>
          </div>

          <div class="flex flex-col gap-3">
            <label class="flex items-center gap-2 text-sm text-slate-700">
              <input v-model="form.podReceived" type="checkbox" class="rounded border-slate-300" />
              POD received
            </label>
            <label class="flex flex-col gap-1 text-sm text-slate-700">
              POD image URL
              <input v-model="form.podImageUrl" type="url" class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none" />
            </label>
          </div>

          <div>
            <div class="flex items-center justify-between">
              <h3 class="text-sm font-semibold text-slate-900">Incidents</h3>
              <button type="button" class="text-sm text-slate-600 hover:text-slate-900" @click="addIncident">+ Add incident</button>
            </div>
            <div v-for="(incident, i) in incidents" :key="i" class="mt-3 grid grid-cols-1 gap-2 rounded-md border border-slate-200 p-3 sm:grid-cols-4">
              <select v-model.number="incident.type" class="rounded-md border border-slate-300 px-2 py-1.5 text-sm">
                <option v-for="(t, idx) in INCIDENT_TYPE" :key="idx" :value="idx">{{ t }}</option>
              </select>
              <select v-model.number="incident.severity" class="rounded-md border border-slate-300 px-2 py-1.5 text-sm">
                <option v-for="(s, idx) in INCIDENT_SEVERITY" :key="idx" :value="idx">{{ s }}</option>
              </select>
              <input v-model="incident.narrative" placeholder="Narrative" class="col-span-2 rounded-md border border-slate-300 px-2 py-1.5 text-sm sm:col-span-1" />
              <button type="button" class="text-sm text-rose-700 hover:text-rose-900" @click="removeIncident(i)">Remove</button>
            </div>
          </div>

          <div>
            <div class="flex items-center justify-between">
              <h3 class="text-sm font-semibold text-slate-900">Expenses</h3>
              <button type="button" class="text-sm text-slate-600 hover:text-slate-900" @click="addExpense">+ Add expense</button>
            </div>
            <div v-for="(expense, i) in expenses" :key="i" class="mt-3 flex flex-col gap-2 rounded-md border border-slate-200 p-3">
              <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
                <select v-model="expense.expenseTypeId" class="rounded-md border border-slate-300 px-2 py-1.5 text-sm">
                  <option value="" disabled>Expense type…</option>
                  <option v-for="type in expenseTypes" :key="type.id" :value="type.id">{{ type.code }} — {{ type.name }}</option>
                </select>
                <input v-model="expense.description" placeholder="Description" class="rounded-md border border-slate-300 px-2 py-1.5 text-sm" />
                <input v-model="expense.amount" type="number" step="any" placeholder="Amount" class="rounded-md border border-slate-300 px-2 py-1.5 text-sm" />
                <select v-model="expense.currencyId" class="rounded-md border border-slate-300 px-2 py-1.5 text-sm">
                  <option value="" disabled>Currency…</option>
                  <option v-for="c in currencies" :key="c.id" :value="c.id">{{ c.code }}</option>
                </select>
              </div>
              <input v-model="expense.receiptImageUrl" placeholder="Receipt image URL (optional)" class="rounded-md border border-slate-300 px-2 py-1.5 text-sm" />
              <div class="flex flex-wrap items-center gap-4">
                <label class="flex items-center gap-1.5 text-sm text-slate-700">
                  <input v-model.number="expense.claimedAgainst" type="radio" :value="0" :name="`claimed-against-${i}`" />
                  Company
                </label>
                <label class="flex items-center gap-1.5 text-sm text-slate-700">
                  <input v-model.number="expense.claimedAgainst" type="radio" :value="1" :name="`claimed-against-${i}`" />
                  Subcontractor accrual
                </label>
                <select
                  v-if="expense.claimedAgainst === 1"
                  v-model="expense.accrualId"
                  class="rounded-md border border-slate-300 px-2 py-1.5 text-sm"
                >
                  <option value="" disabled>Accrual…</option>
                  <option v-for="a in accruals" :key="a.id" :value="a.id">
                    {{ formatMoney(a.estimatedAmount, currencyCode(a.currencyId)) }} — {{ a.accrualDate }}
                  </option>
                </select>
                <button type="button" class="ml-auto text-sm text-rose-700 hover:text-rose-900" @click="removeExpense(i)">Remove</button>
              </div>
            </div>
          </div>

          <button
            type="submit"
            :disabled="debriefSubmitting"
            class="self-start rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          >
            {{ debriefSubmitting ? 'Submitting…' : 'Submit debrief' }}
          </button>
        </form>
      </section>
    </template>
  </AppLayout>
</template>
