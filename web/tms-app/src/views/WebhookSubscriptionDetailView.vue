<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { webhookDeliveriesApi, webhookSubscriptionsApi } from '../api/webhooks'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import {
  WEBHOOK_DELIVERY_STATUS, WEBHOOK_EVENT_TYPES_LIVE, WEBHOOK_SUBSCRIPTION_STATUS, label,
  type WebhookDelivery, type WebhookSubscription,
} from '../api/types'
import { formatDateTime, webhookDeliveryStatusTone, webhookSubscriptionStatusTone } from '../lib/presentation'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const subscription = ref<WebhookSubscription | null>(null)
const deliveries = ref<WebhookDelivery[]>([])
const loading = ref(true)
const error = ref('')
const actionError = ref('')
const actionBusy = ref(false)

const canManage = computed(() => auth.hasFunction('integration.webhook.manage'))
const isDisabled = computed(() => subscription.value?.status === 1)
const sortedDeliveries = computed(() => deliveries.value.slice().sort((a, b) => b.occurredAtUtc.localeCompare(a.occurredAtUtc)))

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [subData, deliveryList] = await Promise.all([
      webhookSubscriptionsApi.get(props.id),
      webhookDeliveriesApi.list(props.id),
    ])
    subscription.value = subData
    deliveries.value = deliveryList
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load this webhook subscription.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

// --- Disable (irreversible — a two-step confirm, no re-enable action exists) ---
const confirmingDisable = ref(false)

async function disable() {
  actionError.value = ''
  actionBusy.value = true
  try {
    await webhookSubscriptionsApi.disable(props.id)
    confirmingDisable.value = false
    await load()
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That action failed — please try again.'
  } finally {
    actionBusy.value = false
  }
}

// --- Retry a delivery ---
const retryingId = ref<string | null>(null)

async function retry(deliveryId: string) {
  actionError.value = ''
  retryingId.value = deliveryId
  try {
    await webhookDeliveriesApi.retry(deliveryId)
    deliveries.value = await webhookDeliveriesApi.list(props.id)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'That retry failed — please try again.'
  } finally {
    retryingId.value = null
  }
}
</script>

<template>
  <AppLayout>
    <ErrorAlert v-if="error" :message="error" />
    <p v-else-if="loading" class="text-sm text-slate-500">Loading…</p>

    <template v-else-if="subscription">
      <div class="flex items-start justify-between">
        <div>
          <h1 class="font-mono text-xl font-semibold text-slate-900">{{ subscription.eventType }}</h1>
          <p class="mt-1 text-sm text-slate-600">{{ subscription.callbackUrl }}</p>
        </div>
        <StatusBadge :text="label(WEBHOOK_SUBSCRIPTION_STATUS, subscription.status)" :tone="webhookSubscriptionStatusTone(subscription.status)" />
      </div>
      <p v-if="!WEBHOOK_EVENT_TYPES_LIVE.has(subscription.eventType)" class="mt-2 text-xs text-amber-700">
        No part of the platform raises this event yet — nothing will ever be delivered here until that's wired up.
      </p>

      <ErrorAlert v-if="actionError" :message="actionError" class="mt-4" />

      <div v-if="canManage && !isDisabled" class="mt-4 flex flex-wrap items-center gap-3">
        <button
          v-if="!confirmingDisable"
          type="button"
          :disabled="actionBusy"
          class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
          @click="confirmingDisable = true"
        >
          Disable
        </button>
        <template v-else>
          <span class="text-sm text-rose-800">Disable this subscription permanently? There is no way to re-enable it.</span>
          <button
            type="button"
            :disabled="actionBusy"
            class="rounded-md border border-rose-300 bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-800 hover:bg-rose-100 disabled:opacity-50"
            @click="disable"
          >
            {{ actionBusy ? 'Disabling…' : 'Yes, disable it' }}
          </button>
          <button type="button" class="rounded-md border border-slate-300 px-3 py-1.5 text-sm" @click="confirmingDisable = false">Cancel</button>
        </template>
      </div>

      <h2 class="mt-8 text-lg font-semibold text-slate-900">Deliveries</h2>
      <p v-if="sortedDeliveries.length === 0" class="mt-3 text-sm text-slate-500">No deliveries yet for this subscription.</p>
      <div v-else class="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th class="px-4 py-3">Occurred</th>
              <th class="px-4 py-3">Entity</th>
              <th class="px-4 py-3">Status</th>
              <th class="px-4 py-3">Response</th>
              <th class="px-4 py-3">Error</th>
              <th v-if="canManage" class="px-4 py-3">Action</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="delivery in sortedDeliveries" :key="delivery.id" class="border-b border-slate-100 last:border-0">
              <td class="px-4 py-3 text-slate-600">{{ formatDateTime(delivery.occurredAtUtc) }}</td>
              <td class="px-4 py-3 text-slate-600">{{ delivery.entityType }} {{ delivery.entityId.slice(0, 8) }}</td>
              <td class="px-4 py-3">
                <StatusBadge :text="label(WEBHOOK_DELIVERY_STATUS, delivery.status)" :tone="webhookDeliveryStatusTone(delivery.status)" />
              </td>
              <td class="px-4 py-3 text-slate-600">{{ delivery.responseStatusCode ?? '—' }}</td>
              <td class="px-4 py-3 max-w-xs truncate text-slate-600" :title="delivery.errorDetail ?? ''">{{ delivery.errorDetail ?? '—' }}</td>
              <td v-if="canManage" class="px-4 py-3">
                <button
                  type="button"
                  :disabled="retryingId === delivery.id"
                  class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                  @click="retry(delivery.id)"
                >
                  {{ retryingId === delivery.id ? 'Retrying…' : 'Retry' }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </AppLayout>
</template>
