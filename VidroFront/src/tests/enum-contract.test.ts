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

// Os seis enums de `shared/types.ts` são espelhados à mão: a API os serializa como inteiro,
// e nada no sistema de tipos liga os dois lados. `contracts/enums.json` é o contrato — o
// mesmo arquivo que `VidroApi/tests/.../Contracts/EnumContractTests.cs` lê. Renomear ou
// renumerar um membro de um lado só deixa os dois CIs vermelhos, em vez de chegar ao browser
// como valor silenciosamente errado (`ReactionType` começa em 1, não em 0).
// Ver contracts/README.md.

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

describe('contrato de enums com a API', () => {
  it.each(Object.keys(mirrored))('%s bate com o golden', (name) => {
    expect(golden).toHaveProperty(name)
    expect(mirrored[name as keyof typeof mirrored]).toEqual(golden[name])
  })

  it('não descreve enum que o front não espelha mais', () => {
    expect(Object.keys(golden).sort()).toEqual(Object.keys(mirrored).sort())
  })
})
