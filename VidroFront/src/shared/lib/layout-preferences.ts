import { useSyncExternalStore } from 'react'

// Layout toggles the viewer keeps across reloads. Each one lives in two places: localStorage
// (what survives the reload) and an attribute on <html> (what the CSS reads, through the
// `sidebar-collapsed:` and `theater:` variants in styles.css). The attribute — not React state —
// drives the layout, so `LAYOUT_PREFERENCES_SCRIPT` can apply it before the first paint: reading
// localStorage in an effect would paint the default layout first and then jump.
const PREFERENCES = {
  sidebarCollapsed: {
    storageKey: 'layout:sidebar-collapsed',
    attribute: 'data-sidebar-collapsed',
  },
  theater: { storageKey: 'layout:theater', attribute: 'data-theater' },
} as const

export type LayoutPreference = keyof typeof PREFERENCES

// Every toggle of a preference re-renders all its users — e.g. the sidebar's own hamburger and the
// top bar's, which stand in for each other.
const listeners = new Set<() => void>()

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function writePreference(preference: LayoutPreference, isEnabled: boolean) {
  const { storageKey, attribute } = PREFERENCES[preference]
  document.documentElement.toggleAttribute(attribute, isEnabled)
  for (const listener of listeners) listener()
  try {
    window.localStorage.setItem(storageKey, isEnabled ? 'on' : 'off')
  } catch {
    // Private mode or blocked storage: the toggle still works, it just is not remembered.
  }
}

export function useLayoutPreference(preference: LayoutPreference) {
  const { attribute } = PREFERENCES[preference]
  // The <html> attribute is the source of truth. Off on the server; hydration starts from that and
  // then re-renders with what the head script already applied — no mismatch.
  const isEnabled = useSyncExternalStore(
    subscribe,
    () => document.documentElement.hasAttribute(attribute),
    () => false,
  )

  function toggle() {
    const nextIsEnabled = !document.documentElement.hasAttribute(attribute)
    writePreference(preference, nextIsEnabled)
  }

  return [isEnabled, toggle] as const
}

// Inlined in <head> by the root route. Plain ES5 on purpose: it runs before any bundle loads.
export const LAYOUT_PREFERENCES_SCRIPT = `(function () {
  var preferences = ${JSON.stringify(Object.values(PREFERENCES))};
  try {
    preferences.forEach(function (preference) {
      if (localStorage.getItem(preference.storageKey) === 'on') {
        document.documentElement.setAttribute(preference.attribute, '');
      }
    });
  } catch (error) {}
})();`
