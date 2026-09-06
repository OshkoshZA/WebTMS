<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { webhookSubscriptionsApi } from '../api/webhooks'
import { ApiError } from '../api/client'
import { WEBHOOK_EVENT_TYPES, WEBHOOK_EVENT_TYPES_LIVE, type CreateWebhookSubscriptionResponse } from '../api/types'

const eventType = ref('')
const callbackUrl = ref('')

const error = ref('')
const submitting = ref(false)
const created = ref<CreateWebhookSubscriptionResponse | null>(null)
const copied = ref(false)

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    created.value = await webhookSubscriptionsApi.create({ eventType: eventType.value, callbackUrl: callbackUrl.value })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}

async function copySecret() {
  if (!created.value) return
  try {
    await navigator.clipboard.writeText(created.value.secret)
    copied.value = true
  } catch {
    // Clipboard API unavailable — the secret is still fully visible and selectable below.
  }
}
</script>

<template>
  <AppLayout>
    <template v-if="!created">
      <h1 class="text-xl font-semibold text-slate-900">New webhook subscription</h1>
      <p class="mt-1 text-sm text-slate-500">Its signing secret is shown once, immediately after creation — there is no way to retrieve it again afterward.</p>

      <form class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
        <ErrorAlert v-if="error" :message="error" />

        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Event type
          <select v-model="eventType" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="t in WEBHOOK_EVENT_TYPES" :key="t" :value="t">
              {{ t }}{{ WEBHOOK_EVENT_TYPES_LIVE.has(t) ? '' : ' (not yet firing)' }}
            </option>
          </select>
        </label>
        <p v-if="eventType && !WEBHOOK_EVENT_TYPES_LIVE.has(eventType)" class="text-xs text-amber-700">
          No part of the platform raises this event yet — the subscription will be created and accepted, but nothing will ever be delivered to it until that's wired up.
        </p>

        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Callback URL
          <input v-model="callbackUrl" type="url" required placeholder="https://partner.example.com/webhook" class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>

        <div class="mt-2 flex gap-3">
          <button
            type="submit"
            :disabled="submitting"
            class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          >
            {{ submitting ? 'Creating…' : 'Create subscription' }}
          </button>
          <RouterLink to="/webhooks" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
            Cancel
          </RouterLink>
        </div>
      </form>
    </template>

    <template v-else>
      <h1 class="text-xl font-semibold text-slate-900">Subscription created</h1>
      <div class="mt-4 max-w-xl rounded-lg border border-amber-300 bg-amber-50 p-4">
        <p class="text-sm font-medium text-amber-900">Copy this signing secret now — it will never be shown again.</p>
        <dl class="mt-3 grid gap-3 text-sm">
          <div>
            <dt class="text-slate-500">Event type</dt>
            <dd class="font-mono text-slate-900">{{ created.eventType }}</dd>
          </div>
          <div>
            <dt class="text-slate-500">Callback URL</dt>
            <dd class="text-slate-900">{{ created.callbackUrl }}</dd>
          </div>
          <div>
            <dt class="text-slate-500">Signing secret</dt>
            <dd class="break-all rounded border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900">{{ created.secret }}</dd>
          </div>
        </dl>
        <button
          type="button"
          class="mt-3 rounded-md border border-amber-400 bg-white px-3 py-1.5 text-sm font-medium text-amber-900 hover:bg-amber-100"
          @click="copySecret"
        >
          {{ copied ? 'Copied' : 'Copy secret' }}
        </button>
      </div>
      <RouterLink
        :to="`/webhooks/${created.id}`"
        class="mt-4 inline-block rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        Continue to subscription
      </RouterLink>
    </template>
  </AppLayout>
</template>
