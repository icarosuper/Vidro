import { toast } from 'sonner'
import { getApiErrorMessage } from './error-messages'

export function toastApiError(error: unknown): void {
  const message = getApiErrorMessage(error)
  toast.error(message)
}
