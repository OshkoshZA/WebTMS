<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { webhookSubscriptionsApi } from '../api/webhooks'
import { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { WEBHOOK_SUBSCRIPTION_STATUS, label, type WebhookSubscription } from '../api/types'
import { webhookSubscriptionStatusTone } from '../lib/presentation'

const router = useRouter()
const auth = useAuthStore()

const subscriptions = ref<WebhookSubscription[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const canManage = computed(() => auth.hasFunction('integration.webhook.manage'))

const filteredSubscriptions = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return subscriptions.value
  return subscriptions.value.filter((s) => s.eventType.toLowerCase().includes(term) || s.callbackUrl.toLowerCase().includes(term))
})

onMounted(async () => {
  try {
    subscriptions.value = await webhookSubscriptionsApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of webhook subscriptions.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold text-slate-900">Webhook subscriptions</h1>
      <RouterLink
        v-if="canManage"
        to="/webhooks/new"
        class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        New subscription
      </RouterLink>
    </div>
    <p class="mt-1 text-sm text-slate-500">Partner callbacks for platform events (§11.2/§11.3) — one event type per subscription.</p>

    <div class="mt-6">
      <input
        v-model="search"
        type="search"
        placeholder="Search by event type or callback URL…"
        class="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />
    </div>

    <ErrorAlert v-if="error" :message="error" class="mt-4" />
    <p v-else-if="loading" class="mt-6 text-sm text-slate-500">Loading…</p>
    <p v-else-if="filteredSubscriptions.length === 0" class="mt-6 text-sm text-slate-500">No webhook subscriptions found.</p>

    <div v-else class="mt-4 overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">Event type</th>
            <th class="px-4 py-3">Callback URL</th>
            <th class="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="sub in filteredSubscriptions"
            :key="sub.id"
            class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
            @click="router.push(`/webhooks/${sub.id}`)"
          >
            <td class="px-4 py-3 font-mono text-xs font-medium text-slate-900">{{ sub.eventType }}</td>
            <td class="px-4 py-3 text-slate-600">{{ sub.callbackUrl }}</td>
            <td class="px-4 py-3">
              <StatusBadge :text="label(WEBHOOK_SUBSCRIPTION_STATUS, sub.status)" :tone="webhookSubscriptionStatusTone(sub.status)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppLayout>
</template>
