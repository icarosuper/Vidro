# Contratos entre serviços

JSON golden do contrato que atravessa `VidroProcessor` → `VidroApi`. Os dois lados testam **o
mesmo arquivo**: o worker prova que *serializa exatamente isto*, a API prova que *aceita
exatamente isto*. Se um lado divergir, o CI do outro quebra.

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

**Mudar um campo é mudar os dois lados no mesmo commit** (regra do `CLAUDE.md` da raiz). O golden
é pretty-printed para diff legível; o corpo real vai compacto, o que é o mesmo JSON.
