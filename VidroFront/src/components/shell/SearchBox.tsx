import { useNavigate } from '@tanstack/react-router'
import { Search } from 'lucide-react'
import { useEffect, useRef } from 'react'

function isTypingInField(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const isFormField = ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)
  return isFormField || target.isContentEditable
}

// "/" jumps to the search from anywhere on the page, unless the user is already typing.
function useSlashToFocus(inputRef: React.RefObject<HTMLInputElement | null>) {
  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      const isSlash = event.key === '/'
      if (!isSlash || isTypingInField(event.target)) return
      event.preventDefault()
      inputRef.current?.focus()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [inputRef])
}

export function SearchBox() {
  const navigate = useNavigate()
  const inputRef = useRef<HTMLInputElement>(null)
  useSlashToFocus(inputRef)

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const typedQuery = String(
      new FormData(event.currentTarget).get('q') ?? '',
    ).trim()
    if (!typedQuery) return
    navigate({ to: '/search', search: { q: typedQuery } })
  }

  return (
    <search className="w-full max-w-md min-w-0 flex-1">
      <form onSubmit={handleSubmit} className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <input
          ref={inputRef}
          name="q"
          type="search"
          placeholder="Search videos"
          aria-label="Search videos"
          className="h-9 w-full rounded-md border border-input bg-background pr-3 pl-9 text-sm outline-none transition focus:border-ring focus:ring-2 focus:ring-ring/30 lg:pr-10"
        />
        <kbd className="pointer-events-none absolute top-1/2 right-2 hidden -translate-y-1/2 rounded border border-border px-1.5 font-mono text-xs text-muted-foreground lg:block">
          /
        </kbd>
      </form>
    </search>
  )
}
