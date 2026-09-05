import { api } from './client'
import type { Currency, ExpenseType } from './types'

// Currencies is open to any authenticated caller (CurrenciesController has no portal
// block) since it's harmless shared reference data. ExpenseTypes was opened to a
// Subcontractor Portal contact specifically (ExpenseTypesController.List/Get) so the
// debrief-expense form has something to offer beyond a raw GUID — see the doc comment
// on that controller for why a Client Portal contact still gets nothing from it.
export const referenceApi = {
  currencies: () => api.get<Currency[]>('/currencies'),
  expenseTypes: () => api.get<ExpenseType[]>('/expense-types'),
}
