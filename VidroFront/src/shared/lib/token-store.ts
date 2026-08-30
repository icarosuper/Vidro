let accessToken: string | null = null

type Listener = () => void

const listeners = new Set<Listener>()

function notifyListeners() {
  for (const listener of listeners) {
    listener()
  }
}

export const tokenStore = {
  get: () => accessToken,
  set: (token: string | null) => {
    accessToken = token
    notifyListeners()
  },
  clear: () => {
    accessToken = null
    notifyListeners()
  },
  subscribe: (listener: Listener) => {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
}
