# Teste de carga da API

Um cenário só, contra o compose local, num host só — o escopo que o `TODO.md` define. Serve para
responder "a API aguenta e onde dói", **não** para medir o worker: o pipeline de vídeo tem o
P-PERF6 e as métricas por passo em `VidroProcessor/metrics/metrics.go`.

## Como rodar

Suba a stack e dispare o k6 dentro da rede do compose — nada é instalado no host, e o repo não
ganha dependência nenhuma:

```bash
docker compose up -d --build
docker run --rm -i --network vidro_default -e BASE_URL=http://api:5000 grafana/k6 run - < loadtest/api-load.js
```

Fora do Docker, com a API em `localhost:5000`, o default de `BASE_URL` já serve.

O que interpreta o resultado é o `/metrics` da API (ver `VidroApi/docs/agents/design-decisions.md`
**#12**): Prometheus em `:9090`, Grafana em `:3001`. As séries que importam:

| Pergunta | Query |
|---|---|
| p95 por rota | `histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket[5m])))` |
| erro por rota | `sum by (http_route, http_response_status_code) (rate(http_server_request_duration_seconds_count[5m]))` |
| pool do Npgsql | `db_client_connection_count` (label `state`: `idle`/`used`) |
| rate limiter | `sum by (aspnetcore_rate_limiting_result) (aspnetcore_rate_limiting_requests_total)` |

## Os três cenários

| Cenário | O que exercita | Por quê |
|---|---|---|
| `browse` | `GET /v1/videos/{id}` (a forma exata do polling da tela de upload), `trending`, `feed` | É o tráfego que um espectador produz |
| `createVideo` | `POST .../videos` — linha de metadata + URL presignada | É a fatia da API num upload |
| `signInStorm` | `POST /v1/auth/signin` com senha errada, a 50/s | O `AddRateLimiter` nunca tinha visto pressão real, só teste de integração |

**Não sobe bytes de vídeo de propósito.** Um PUT presignado de verdade mede banda, MinIO e worker —
outro assunto, outro instrumento.

## Medição de 2026-09-13 (primeira)

Stack do compose num host só (containers de dev, banco praticamente vazio), k6 na rede do compose.

| Cenário | Requisições | p95 | Falhas |
|---|---|---|---|
| `browse` | 3.330 | **2,11 ms** | 0 |
| `createVideo` | 141 | **7,77 ms** | 0 |
| `signInStorm` | 1.500 | — | 0 erro 5xx; **1.493** responderam 429 |

p95 por rota, do próprio Prometheus: `signup` 100 ms (é o BCrypt, e é para ser lento), `CreateVideo`
24 ms, todo o resto ≤ 5 ms. Pool do Npgsql estabilizou em 6 conexões.

**O limiter faz o que promete:** `aspnetcore_rate_limiting_requests_total` mostra ~10 `acquired` por
minuto — exatamente `RateLimit:AuthPermitLimit` — e o resto rejeitado por `endpoint_limiter`,
sempre com 429, nunca 5xx.

### Duas ressalvas, para o próximo que ler esses números

1. **Banco vazio.** Um canal, ~140 vídeos, sem comentário. `trending` e `feed` a 2 ms dizem pouco
   sobre o mesmo endpoint com 100 mil linhas — os índices compostos do `design-decisions #4` só
   provam o valor deles com volume.
2. **A primeira versão deste script media a coisa errada.** `signInStorm` era `constant-vus` num
   laço sem pausa: como uma requisição rejeitada custa quase nada, três VUs produziram **753 mil**
   rejeições a 10k rps e afogaram os outros dois cenários no resumo. Virou `constant-arrival-rate`.
   Limiter é o único alvo em que o caminho rápido é a rejeição — medir isso com VUs em laço mede a
   velocidade da rejeição, não a saúde da API.
