import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import {
  CommentSortOrder,
  PlaylistScope,
  PlaylistVisibility,
  ReactionType,
  VideoStatus,
  VideoVisibility,
} from '#/shared/types'

// The six enums in `shared/types.ts` are mirrored by hand: the API serializes them as integers,
// and the generated OpenAPI types only carry the ids (`0 | 1`), not the names. `contracts/enums.json`
// is the contract — the same file `VidroApi/tests/.../Contracts/EnumContractTests.cs` reads.
// Renaming or renumbering a member on one side only turns both CIs red, instead of reaching the
// browser as a silently wrong value (`ReactionType` starts at 1, not 0).
// See contracts/README.md.

const goldenPath = join(import.meta.dirname, '../../../contracts/enums.json')
const golden = JSON.parse(readFileSync(goldenPath, 'utf-8')) as Record<
  string,
  Record<string, number>
>

const mirrored = {
  VideoStatus,
  VideoVisibility,
  ReactionType,
  PlaylistVisibility,
  PlaylistScope,
  CommentSortOrder,
}

describe('enum contract with the API', () => {
  it.each(Object.keys(mirrored))('%s matches the golden', (name) => {
    expect(golden).toHaveProperty(name)
    expect(mirrored[name as keyof typeof mirrored]).toEqual(golden[name])
  })

  it('does not describe an enum the front no longer mirrors', () => {
    expect(Object.keys(golden).sort()).toEqual(Object.keys(mirrored).sort())
  })
})
