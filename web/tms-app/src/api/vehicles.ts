import { api } from './client'
import type { CreateVehicleRequest, UpdateVehicleRequest, Vehicle } from './types'

export const vehiclesApi = {
  list: () => api.get<Vehicle[]>('/vehicles'),
  get: (id: string) => api.get<Vehicle>(`/vehicles/${id}`),
  create: (request: CreateVehicleRequest) => api.post<Vehicle>('/vehicles', request),
  update: (id: string, request: UpdateVehicleRequest) => api.put<void>(`/vehicles/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/vehicles/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/vehicles/${id}/reactivate`),
}
