import { api } from './client'
import type { CaptureExchangeRateRequest, ExchangeRate } from './types'

// No list endpoint exists — a rate is only ever looked up by (pair, date), never
// browsed as a full history (ExchangeRatesController's own design: the "rate
// effective on" a date is the most recently captured one on or before it).
export const exchangeRatesApi = {
  get: (fromCurrencyId: string, toCurrencyId: string, date: string) =>
    api.get<ExchangeRate>(`/exchange-rates?from=${fromCurrencyId}&to=${toCurrencyId}&date=${date}`),
  capture: (request: CaptureExchangeRateRequest) => api.post<ExchangeRate>('/exchange-rates', request),
}
