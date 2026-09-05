<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { loadsApi } from '../api/loads'
import { referenceApi } from '../api/reference'
import { ApiError } from '../api/client'
import type { Client, LoadType } from '../api/types'

const router = useRouter()

const clients = ref<Client[]>([])
const loadTypes = ref<LoadType[]>([])
const loadingReferenceData = ref(true)

const clientId = ref('')
const referenceNo = ref('')
const loadTypeId = ref('')
const pickupWindowStart = ref('')
const pickupWindowEnd = ref('')
const deliveryWindowStart = ref('')
const deliveryWindowEnd = ref('')
const creditOverrideReason = ref('')

const error = ref('')
// Only a credit-limit breach (422) offers an override — every other error (400/404/409)
// just needs the message shown and the form fixed, not a reason field.
const creditBreach = ref(false)
const submitting = ref(false)

onMounted(async () => {
  try {
    const [clientList, loadTypeList] = await Promise.all([referenceApi.clients(), referenceApi.loadTypes()])
    clients.value = clientList
    loadTypes.value = loadTypeList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load clients/load types.'
  } finally {
    loadingReferenceData.value = false
  }
})

function toIso(localDateTime: string): string | undefined {
  return localDateTime ? new Date(localDateTime).toISOString() : undefined
}

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    const load = await loadsApi.create({
      clientId: clientId.value,
      referenceNo: referenceNo.value,
      loadTypeId: loadTypeId.value,
      pickupWindowStart: toIso(pickupWindowStart.value),
      pickupWindowEnd: toIso(pickupWindowEnd.value),
      deliveryWindowStart: toIso(deliveryWindowStart.value),
      deliveryWindowEnd: toIso(deliveryWindowEnd.value),
      creditOverrideReason: creditOverrideReason.value || undefined,
    })
    await router.push(`/loads/${load.id}`)
  } catch (e) {
    if (e instanceof ApiError) {
      error.value = e.message
      creditBreach.value = e.status === 422
    } else {
      error.value = 'Something went wrong — please try again.'
      creditBreach.value = false
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppLayout>
    <h1 class="text-xl font-semibold text-slate-900">New load</h1>

    <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

    <form v-else class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
      <ErrorAlert v-if="error" :message="error" />

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Client
        <select v-model="clientId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select a client…</option>
          <option v-for="client in clients" :key="client.id" :value="client.id">{{ client.name }}</option>
        </select>
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Reference no.
        <input
          v-model="referenceNo"
          type="text"
          required
          class="rounded-md border border-slate-300 px-3 py-2 text-sm"
        />
      </label>

      <label class="flex flex-col gap-1 text-sm text-slate-700">
        Load type
        <select v-model="loadTypeId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
          <option value="" disabled>Select a load type…</option>
          <option v-for="loadType in loadTypes" :key="loadType.id" :value="loadType.id">
            {{ loadType.code }} — {{ loadType.description }}
          </option>
        </select>
      </label>

      <div class="grid grid-cols-2 gap-4">
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Pickup window start
          <input v-model="pickupWindowStart" type="datetime-local" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Pickup window end
          <input v-model="pickupWindowEnd" type="datetime-local" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Delivery window start
          <input v-model="deliveryWindowStart" type="datetime-local" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Delivery window end
          <input v-model="deliveryWindowEnd" type="datetime-local" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <label v-if="creditBreach" class="flex flex-col gap-1 text-sm text-slate-700">
        Reason for credit-limit override
        <input
          v-model="creditOverrideReason"
          type="text"
          required
          class="rounded-md border border-slate-300 px-3 py-2 text-sm"
        />
      </label>

      <div class="mt-2 flex gap-3">
        <button
          type="submit"
          :disabled="submitting"
          class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
        >
          {{ submitting ? 'Creating…' : creditBreach ? 'Retry with override' : 'Create load' }}
        </button>
        <RouterLink to="/loads" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
          Cancel
        </RouterLink>
      </div>
    </form>
  </AppLayout>
</template>
