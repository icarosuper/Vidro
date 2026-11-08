// @vitest-environment jsdom

import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { THEME_SCRIPT } from '#/components/ThemeToggle'
import {
  LAYOUT_PREFERENCES_SCRIPT,
  useLayoutPreference,
} from '#/shared/lib/layout-preferences'

// This jsdom setup has no localStorage; a Map-backed stand-in is enough here.
function stubLocalStorage() {
  const entries = new Map<string, string>()
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => entries.get(key) ?? null,
    setItem: (key: string, value: string) => entries.set(key, value),
  })
  return entries
}

const html = document.documentElement

beforeEach(() => {
  html.removeAttribute('data-sidebar-collapsed')
  html.removeAttribute('data-theater')
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('useLayoutPreference', () => {
  it('toggles the <html> attribute and remembers the choice', () => {
    const storage = stubLocalStorage()
    const { result } = renderHook(() => useLayoutPreference('sidebarCollapsed'))

    act(() => result.current[1]())

    expect(result.current[0]).toBe(true)
    expect(html.hasAttribute('data-sidebar-collapsed')).toBe(true)
    expect(storage.get('layout:sidebar-collapsed')).toBe('on')

    act(() => result.current[1]())

    expect(html.hasAttribute('data-sidebar-collapsed')).toBe(false)
    expect(storage.get('layout:sidebar-collapsed')).toBe('off')
  })

  it('picks up what the head script already applied', () => {
    html.setAttribute('data-theater', '')

    const { result } = renderHook(() => useLayoutPreference('theater'))

    expect(result.current[0]).toBe(true)
  })

  it('still toggles when storage is unavailable', () => {
    vi.stubGlobal('localStorage', undefined)
    const { result } = renderHook(() => useLayoutPreference('theater'))

    act(() => result.current[1]())

    expect(html.hasAttribute('data-theater')).toBe(true)
  })
})

describe('LAYOUT_PREFERENCES_SCRIPT', () => {
  it('applies the stored preferences to <html>', () => {
    const storage = stubLocalStorage()
    storage.set('layout:sidebar-collapsed', 'on')
    storage.set('layout:theater', 'off')

    new Function(LAYOUT_PREFERENCES_SCRIPT)()

    expect(html.hasAttribute('data-sidebar-collapsed')).toBe(true)
    expect(html.hasAttribute('data-theater')).toBe(false)
  })

  it('does not throw when storage is unavailable', () => {
    vi.stubGlobal('localStorage', undefined)

    expect(() => new Function(LAYOUT_PREFERENCES_SCRIPT)()).not.toThrow()
  })
})

describe('THEME_SCRIPT', () => {
  function stubSystemPrefersDark(prefersDark: boolean) {
    vi.stubGlobal('matchMedia', () => ({ matches: prefersDark }))
  }

  afterEach(() => {
    html.classList.remove('light', 'dark')
    html.removeAttribute('data-theme')
    html.style.colorScheme = ''
  })

  it('puts a saved dark theme on <html> before the app loads', () => {
    stubLocalStorage().set('theme', 'dark')
    stubSystemPrefersDark(false)

    new Function(THEME_SCRIPT)()

    expect(html.classList.contains('dark')).toBe(true)
    expect(html.getAttribute('data-theme')).toBe('dark')
    expect(html.style.colorScheme).toBe('dark')
  })

  it('follows the system when nothing is saved', () => {
    stubLocalStorage()
    stubSystemPrefersDark(true)

    new Function(THEME_SCRIPT)()

    expect(html.classList.contains('dark')).toBe(true)
    expect(html.hasAttribute('data-theme')).toBe(false)
  })

  it('does not throw when storage is unavailable', () => {
    vi.stubGlobal('localStorage', undefined)

    expect(() => new Function(THEME_SCRIPT)()).not.toThrow()
  })
})
