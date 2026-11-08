import { Link } from '@tanstack/react-router'
import { Clapperboard } from 'lucide-react'
import { cn } from '#/lib/utils'

type BrandLinkProps = {
  className?: string
  wordmarkClassName?: string
}

export function BrandLink({ className, wordmarkClassName }: BrandLinkProps) {
  return (
    <Link
      to="/"
      aria-label="Vidro home"
      className={cn(
        'flex items-center gap-2 text-xl font-extrabold tracking-tight text-foreground no-underline',
        className,
      )}
    >
      <Clapperboard className="size-5 text-primary" />
      <span className={wordmarkClassName}>vidro</span>
    </Link>
  )
}
