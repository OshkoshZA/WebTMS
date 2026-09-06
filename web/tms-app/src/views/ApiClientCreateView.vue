<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import AppLayout from '../components/AppLayout.vue'
import ErrorAlert from '../components/ErrorAlert.vue'
import { apiClientsApi } from '../api/apiClients'
import { rolesApi } from '../api/roles'
import { ApiError } from '../api/client'
import type { CreateApiClientResponse, Role } from '../api/types'

const roles = ref<Role[]>([])
const loadingReferenceData = ref(true)

const name = ref('')
const roleId = ref('')
const rateLimitPerMinute = ref(60)

const error = ref('')
const submitting = ref(false)
const created = ref<CreateApiClientResponse | null>(null)
const copied = ref(false)

onMounted(async () => {
  try {
    roles.value = await rolesApi.list()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the list of roles.'
  } finally {
    loadingReferenceData.value = false
  }
})

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    created.value = await apiClientsApi.create({
      name: name.value,
      roleId: roleId.value,
      rateLimitPerMinute: rateLimitPerMinute.value || undefined,
    })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Something went wrong — please try again.'
  } finally {
    submitting.value = false
  }
}

async function copySecret() {
  if (!created.value) return
  try {
    await navigator.clipboard.writeText(created.value.clientSecret)
    copied.value = true
  } catch {
    // Clipboard API unavailable (insecure context, permission denied) — the secret
    // is still fully visible and selectable in the box below either way.
  }
}
</script>

<template>
  <AppLayout>
    <template v-if="!created">
      <h1 class="text-xl font-semibold text-slate-900">New API client</h1>
      <p class="mt-1 text-sm text-slate-500">Its secret is shown once, immediately after creation — there is no way to retrieve it again afterward.</p>

      <p v-if="loadingReferenceData" class="mt-6 text-sm text-slate-500">Loading…</p>

      <form v-else class="mt-6 flex max-w-lg flex-col gap-4" @submit.prevent="submit">
        <ErrorAlert v-if="error" :message="error" />

        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Name
          <input v-model="name" type="text" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>

        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Role
          <select v-model="roleId" required class="rounded-md border border-slate-300 px-3 py-2 text-sm">
            <option value="" disabled>Select…</option>
            <option v-for="r in roles" :key="r.id" :value="r.id">{{ r.name }}</option>
          </select>
        </label>

        <label class="flex flex-col gap-1 text-sm text-slate-700">
          Rate limit (requests/minute)
          <input v-model.number="rateLimitPerMinute" type="number" min="1" required class="rounded-md border border-slate-300 px-3 py-2 text-sm" />
        </label>

        <div class="mt-2 flex gap-3">
          <button
            type="submit"
            :disabled="submitting"
            class="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          >
            {{ submitting ? 'Creating…' : 'Create API client' }}
          </button>
          <RouterLink to="/api-clients" class="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-700 hover:bg-slate-100">
            Cancel
          </RouterLink>
        </div>
      </form>
    </template>

    <template v-else>
      <h1 class="text-xl font-semibold text-slate-900">{{ created.name }} created</h1>
      <div class="mt-4 max-w-xl rounded-lg border border-amber-300 bg-amber-50 p-4">
        <p class="text-sm font-medium text-amber-900">Copy this secret now — it will never be shown again.</p>
        <dl class="mt-3 grid gap-3 text-sm">
          <div>
            <dt class="text-slate-500">Client ID</dt>
            <dd class="font-mono text-slate-900">{{ created.clientId }}</dd>
          </div>
          <div>
            <dt class="text-slate-500">Client secret</dt>
            <dd class="break-all rounded border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900">{{ created.clientSecret }}</dd>
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
        :to="`/api-clients/${created.id}`"
        class="mt-4 inline-block rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        Continue to API client
      </RouterLink>
    </template>
  </AppLayout>
</template>
