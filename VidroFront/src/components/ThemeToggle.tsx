import { Monitor, Moon, Sun } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Button } from '#/components/ui/button'

type ThemeMode = 'light' | 'dark' | 'auto'

const NEXT_MODE: Record<ThemeMode, ThemeMode> = {
  light: 'dark',
  dark: 'auto',
  auto: 'light',
}

const MODE_ICON: Record<ThemeMode, typeof Sun> = {
  light: Sun,
  dark: Moon,
  auto: Monitor,
}

const MODE_LABEL: Record<ThemeMode, string> = {
  light: 'Theme: light. Click for dark.',
  dark: 'Theme: dark. Click to follow the system.',
  auto: 'Theme: system. Click for light.',
}

function getInitialMode(): ThemeMode {
  if (typeof window === 'undefined') {
    return 'auto'
  }

  const stored = window.localStorage.getItem('theme')
  if (stored === 'light' || stored === 'dark' || stored === 'auto') {
    return stored
  }

  return 'auto'
}

function resolveMode(mode: ThemeMode): 'light' | 'dark' {
  if (mode !== 'auto') {
    return mode
  }

  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
  return prefersDark
    ? 'dark'
    : 'light'
}

function applyThemeMode(mode: ThemeMode) {
  const resolved = resolveMode(mode)

  document.documentElement.classList.remove('light', 'dark')
  document.documentElement.classList.add(resolved)

  if (mode === 'auto') {
    document.documentElement.removeAttribute('data-theme')
  } else {
    document.documentElement.setAttribute('data-theme', mode)
  }

  document.documentElement.style.colorScheme = resolved
}

// ponytail: o tema só é aplicado depois da hidratação, então quem escolheu dark vê um
// flash claro no primeiro paint. Resolver exige um script inline no <head> do shellComponent.
export default function ThemeToggle() {
  const [mode, setMode] = useState<ThemeMode>('auto')

  useEffect(() => {
    const initialMode = getInitialMode()
    setMode(initialMode)
    applyThemeMode(initialMode)
  }, [])

  useEffect(() => {
    if (mode !== 'auto') {
      return
    }

    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const onSystemThemeChange = () => applyThemeMode('auto')

    media.addEventListener('change', onSystemThemeChange)
    return () => {
      media.removeEventListener('change', onSystemThemeChange)
    }
  }, [mode])

  function toggleMode() {
    const nextMode = NEXT_MODE[mode]
    setMode(nextMode)
    applyThemeMode(nextMode)
    window.localStorage.setItem('theme', nextMode)
  }

  const label = MODE_LABEL[mode]
  const Icon = MODE_ICON[mode]

  return (
    <Button
      variant="ghost"
      size="sm"
      onClick={toggleMode}
      aria-label={label}
      title={label}
    >
      <Icon className="h-4 w-4" />
    </Button>
  )
}
