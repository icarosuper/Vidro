const SITE_NAME = 'Vidro'

type SeoInput = {
  /** Page title without the site name — the helper appends it. */
  title: string
  description: string
  /** Absolute URL. Presigned MinIO URLs work but expire; see docs/agents/architecture.md. */
  image?: string | undefined
  type?: 'website' | 'video.other'
  /** Private or duplicated pages (dashboard, search results) ask to stay out of the index. */
  noIndex?: boolean
}

/**
 * Builds the meta tags for a route `head`. og:url is deliberately absent: the site has no
 * configured public URL, and a crawler falls back to the URL it fetched.
 */
export function seo({ title, description, image, type, noIndex }: SeoInput) {
  const fullTitle = title === SITE_NAME ? SITE_NAME : `${title} · ${SITE_NAME}`

  const meta = [
    { title: fullTitle },
    { name: 'description', content: description },
    { property: 'og:title', content: fullTitle },
    { property: 'og:description', content: description },
    { property: 'og:site_name', content: SITE_NAME },
    { property: 'og:type', content: type ?? 'website' },
    {
      name: 'twitter:card',
      content: image ? 'summary_large_image' : 'summary',
    },
    { name: 'twitter:title', content: fullTitle },
    { name: 'twitter:description', content: description },
  ]

  if (image) {
    meta.push(
      { property: 'og:image', content: image },
      { name: 'twitter:image', content: image },
    )
  }

  if (noIndex) meta.push({ name: 'robots', content: 'noindex' })

  return meta
}
