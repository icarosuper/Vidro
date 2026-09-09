# Contratos entre serviços

JSON golden dos contratos que atravessam serviços. Os dois lados de cada contrato testam **o
mesmo arquivo**: se um divergir, o CI do outro quebra.

| Golden | Atravessa |
|---|---|
| `video-processed-*.json` | `VidroProcessor` → `VidroApi` (payload do webhook) |
| `enums.json` | `VidroApi` → `VidroFront` (os seis enums espelhados à mão) |

Existe porque os dois P0 do [`TODO.md`](../TODO.md) foram divergência de contrato entre o
webhook Go e o handler C#, e ninguém viu por meses. Antes do monorepo isso exigiria publicar um
pacote; agora é um arquivo. Ver [`docs/MONOREPO.md`](../docs/MONOREPO.md).

## `video-processed-*.json` — webhook `POST /webhooks/video-processed`

| Arquivo | Caso | O que a API tem que fazer |
|---|---|---|
| `video-processed-success-full.json` | pipeline inteiro deu certo | vídeo vira `Ready`, com artefatos e metadata |
| `video-processed-success-minimal.json` | sucesso sem nenhum artefato opcional e sem metadata (todo passo não-crítico falhou, `analyze` incluído) | vídeo vira `Ready`, `thumbnailUrls` vazio, sem metadata |
| `video-processed-success-without-processed-path.json` | `success: true` sem o output do transcode — sucesso que não é sucesso | vídeo vira `Failed`, **sem** exceção no handler |
| `video-processed-failure.json` | falha permanente (retries esgotados, job no DLQ) | vídeo vira `Failed` |

O `videoId` é o placeholder `11111111-1111-1111-1111-111111111111`. Cada lado troca a string
pelo id real do seu teste — é o único campo que o teste pode alterar.

Quem testa:

- `VidroProcessor/webhook_contract_test.go` — `buildWebhookPayload` a partir de um `queue.JobState`.
- `VidroApi/tests/VidroApi.IntegrationTests/Videos/VideoProcessedTests.cs` — POST assinado no endpoint real.

## `enums.json` — os seis enums que o front espelha à mão

Atravessa `VidroApi` → `VidroFront`. A API serializa esses enums como **inteiro**, e o front os
redigita em `src/shared/types.ts`: nada no sistema de tipos liga os dois lados, então renomear ou
renumerar um membro chegaria ao browser como valor silenciosamente errado. `ReactionType` começa
em **1**, não em 0 — é exatamente o tipo de detalhe que um refactor leva junto sem ninguém notar.

Os seis moram em `VidroApi/src/VidroApi.Domain/Enums/`: `VideoStatus`, `VideoVisibility`,
`ReactionType`, `PlaylistVisibility`, `PlaylistScope` e `CommentSortOrder`.

Quem testa:

- `VidroApi/tests/VidroApi.UnitTests/Contracts/EnumContractTests.cs` — reflexão sobre os seis
  tipos. Unit test: sem Docker, sem container, roda sempre.
- `VidroFront/src/tests/enum-contract.test.ts` — os seis `as const` de `shared/types.ts`.

Os dois testes também falham se o golden descrever um enum que o lado deles não espelha mais —
entrada de golden sem ninguém atrás é contrato que ninguém confere.

**Por que não vem do OpenAPI:** viria, se o documento descrevesse os enums. Não descreve — ver
[`VidroApi/openapi/README.md`](../VidroApi/openapi/README.md). O `openapi/v1.json` trava a
superfície de URL; este golden trava os enums.

---

## Regras que valem para os dois

**Mudar um campo é mudar os dois lados no mesmo commit** (regra do `CLAUDE.md` da raiz).

Os três workflows disparam em mudança de `contracts/**` (filtro de path de cada um) — senão um
golden poderia mudar sem CI nenhum conferir.

Os goldens são pretty-printed para diff legível. No caso do webhook, o corpo real vai compacto,
o que é o mesmo JSON.
