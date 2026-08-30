import { useState } from 'react'
import { Button } from '#/components/ui/button'

const TRUNCATE_AT = 200

type ExpandableTextProps = {
  text: string
  className?: string
}

export function ExpandableText({ text, className }: ExpandableTextProps) {
  const [expanded, setExpanded] = useState(false)

  const cls = `break-words ${className ?? ''}`

  if (text.length <= TRUNCATE_AT) {
    return <p className={cls}>{text}</p>
  }

  return (
    <p className={cls}>
      {expanded ? text : `${text.slice(0, TRUNCATE_AT)}…`}
      <Button
        variant="link"
        size="sm"
        className="h-auto p-0 pl-1 text-xs font-normal"
        onClick={() => setExpanded((v) => !v)}
      >
        {expanded ? 'Show less' : 'Show more'}
      </Button>
    </p>
  )
}
