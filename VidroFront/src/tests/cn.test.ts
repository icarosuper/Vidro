import { describe, expect, it } from 'vitest'
import { cn } from '#/lib/utils'

// `cn` (the package) replaced clsx + tailwind-merge on 2026-11-07, at v0.5. These pin the merges
// the components rely on, so a release that breaks parity fails here, not in a screenshot.
describe('cn', () => {
  it('joins conditional classes like clsx', () => {
    const isActive = true
    const isDisabled = false
    expect(
      cn('px-2', isActive && 'bg-primary', isDisabled && 'opacity-50', {
        'text-white': isActive,
      }),
    ).toBe('px-2 bg-primary text-white')
  })

  it('lets the later conflicting utility win, like tailwind-merge', () => {
    expect(cn('px-2 py-1', 'px-4')).toBe('py-1 px-4')
    expect(cn('rounded-md', 'rounded-full')).toBe('rounded-full')
  })

  it('keeps a text size and a text color, which do not conflict', () => {
    expect(cn('text-sm', 'text-muted-foreground')).toBe(
      'text-sm text-muted-foreground',
    )
  })

  it('resolves conflicts per variant and keeps arbitrary values', () => {
    expect(cn('hover:bg-accent', 'hover:bg-primary', 'bg-muted')).toBe(
      'hover:bg-primary bg-muted',
    )
    expect(cn('min-w-52', 'w-(--radix-dropdown-menu-trigger-width)')).toBe(
      'min-w-52 w-(--radix-dropdown-menu-trigger-width)',
    )
  })
})
