import { api } from './client'
import type {
  CreateWebhookSubscriptionRequest, CreateWebhookSubscriptionResponse, WebhookDelivery, WebhookSubscription,
} from './types'

export const webhookSubscriptionsApi = {
  list: () => api.get<WebhookSubscription[]>('/webhooks/subscriptions'),
  get: (id: string) => api.get<WebhookSubscription>(`/webhooks/subscriptions/${id}`),
  create: (request: CreateWebhookSubscriptionRequest) =>
    api.post<CreateWebhookSubscriptionResponse>('/webhooks/subscriptions', request),
  disable: (id: string) => api.post<void>(`/webhooks/subscriptions/${id}/disable`),
}

export const webhookDeliveriesApi = {
  // subscriptionId/status are both optional query filters the backend already
  // supports — every caller in this app scopes to one subscription at a time, from
  // that subscription's own detail view.
  list: (subscriptionId: string) => api.get<WebhookDelivery[]>(`/webhook-deliveries?subscriptionId=${subscriptionId}`),
  retry: (id: string) => api.post<void>(`/webhook-deliveries/${id}/retry`),
}
