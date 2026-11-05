import { describe, expect, it } from 'vitest'
import { toEnumMember } from '#/shared/lib/enum-member'
import { VideoVisibility } from '#/shared/types'

describe('toEnumMember', () => {
  it('turns a <Select> string into the enum id', () => {
    expect(toEnumMember(VideoVisibility, '2')).toBe(VideoVisibility.Private)
  })

  it('accepts an id that is already a number', () => {
    expect(toEnumMember(VideoVisibility, 0)).toBe(VideoVisibility.Public)
  })

  it('throws instead of sending the API an id it does not know', () => {
    expect(() => toEnumMember(VideoVisibility, '7')).toThrow(
      '7 is not one of 0, 1, 2',
    )
  })
})
