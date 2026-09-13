import { describe, expect, it } from 'vitest'
import { seo } from '#/shared/lib/seo'

function find(meta: ReturnType<typeof seo>, key: string) {
  return meta.find((tag) => 'name' in tag && tag.name === key) as
    | { name: string; content: string }
    | undefined
}

describe('seo', () => {
  it('appends the site name once, and not to the site name itself', () => {
    const [videoTitle] = seo({ title: 'My video', description: 'd' })
    const [homeTitle] = seo({ title: 'Vidro', description: 'd' })

    expect(videoTitle).toEqual({ title: 'My video · Vidro' })
    expect(homeTitle).toEqual({ title: 'Vidro' })
  })

  it('asks for the large card only when there is an image', () => {
    const withImage = seo({
      title: 't',
      description: 'd',
      image: 'https://example.test/thumb.jpg',
    })
    const withoutImage = seo({ title: 't', description: 'd' })

    expect(find(withImage, 'twitter:card')?.content).toBe('summary_large_image')
    expect(find(withoutImage, 'twitter:card')?.content).toBe('summary')
    expect(find(withoutImage, 'twitter:image')).toBeUndefined()
  })

  it('emits robots noindex only when asked', () => {
    expect(
      find(seo({ title: 't', description: 'd' }), 'robots'),
    ).toBeUndefined()
    expect(
      find(seo({ title: 't', description: 'd', noIndex: true }), 'robots')
        ?.content,
    ).toBe('noindex')
  })
})
