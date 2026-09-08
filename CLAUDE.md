# CLAUDE.md — monorepo Vidro

Regras que valem para os **três serviços**. Cada serviço tem seu próprio `CLAUDE.md` com o que é
específico dele; o que está aqui não se repete lá.

## Os três serviços

| Serviço | O que é | Docs para agente |
|---|---|---|
| `VidroApi/` | API .NET (Clean + Vertical Slice), Postgres, MinIO, Redis | `VidroApi/docs/agents/` |
| `VidroFront/` | TanStack Start + React, consome a API | `VidroFront/docs/agents/` |
| `VidroProcessor/` | Worker Go: fila Redis → pipeline FFmpeg → MinIO → webhook | `VidroProcessor/docs/agents/` |

Os três são **um repositório só** — este. A raiz carrega o `docker-compose.yml` do stack inteiro,
o `TODO.md`, este arquivo e o `docs/MONOREPO.md` (por que é monorepo, e o que isso custou).

**Escopo de edição:** o padrão continua sendo mexer em **um** serviço por tarefa — leia os outros
dois à vontade para entender contratos, tipos e endpoints. A exceção é o contrato compartilhado:
aí os dois lados mudam juntos, no mesmo commit, e isso é o comportamento certo, não invasão de
escopo. Fora esse caso, faltou algo no outro serviço — informe o usuário antes de mexer.

**Contrato compartilhado** (nomes de fila Redis, caminhos no MinIO, formato do webhook): mude os
dois lados **no mesmo commit**. O payload do webhook `video-processed` é o único trecho já travado
por teste: os golden de [`contracts/`](contracts/README.md) são lidos pelo worker e pela API, então
mudar um campo é editar o golden e os dois lados juntos — o resto do contrato ainda é disciplina. É a razão principal de isto ser um monorepo — os dois P0 do
`TODO.md` foram divergências de contrato que ninguém viu por meses. Ver
`VidroProcessor/docs/agents/design-decisions.md`.

## Idioma do código

**Todo código em inglês** nos três serviços: nomes de classe, método, variável, tipo, teste,
mensagem de log, comentário e XML doc. Vale também para as docs do `docs/agents/` da API e do
Processor, que já são em inglês.

Duas exceções: **mensagens de commit** (português, ver abaixo) e as docs em português — este
arquivo, `docs/MONOREPO.md`, `TODO.md`, `README.md` e o `CLAUDE.md` + `docs/` do `VidroFront`.

## Legibilidade

Valem para os **três** serviços — C#, TypeScript e Go. O que é idiomático de uma linguagem fica no
`conventions.md` do serviço; o que está aqui não se repete lá.

- **Variável nomeada em vez de expressão inline.** O resultado de uma checagem, query ou chamada
  async recebe um nome antes de entrar numa condição. Nunca inline dentro do `if`.

  ```ts
  // ✅
  const emailAlreadyTaken = users.some((u) => u.email === email)
  if (emailAlreadyTaken) return { error: 'Email already in use' }

  // ❌
  if (users.some((u) => u.email === email)) return { error: 'Email already in use' }
  ```

- **Lógica complexa vira função com nome.** Se o bloco precisa de um comentário para se explicar,
  ele precisa de um nome.

- **Nome diz "o quê", não "como".** Sem abreviação, sem nome de uma letra (fora índice de loop),
  sem genérico — `result`, `data`, `temp`.

- **Ternário curto cabe em uma linha; longo ou aninhado vai para três** — condição, `?`, `:`.
  A regra até 2026-09-07 era "sempre três linhas, nunca uma"; ela caiu porque o formatter do
  Biome (`VidroFront`) colapsa ternário curto e não tem opção para preservar — manter a regra
  custava desligar o formatter do front inteiro. No front **quem decide é o formatter**: rode
  `bun run lint`. Em C# e Go, use o julgamento — o critério é o mesmo que o formatter aplica,
  quebrar quando não couber na linha.

  ```ts
  // ✅ curto
  const label = isAuthenticated ? 'Sign out' : 'Sign in'

  // ✅ longo ou aninhado — três linhas, e considere extrair para uma função nomeada
  const nextMode = mode === 'light'
    ? 'dark'
    : resolveModeFromSystemPreference(mode)
  ```

## Vídeo preso em `Processing`

O runbook é [`docs/troubleshooting-stuck-video.md`](docs/troubleshooting-stuck-video.md). Mora na
raiz porque atravessa API → Redis → worker → MinIO; nenhum serviço sozinho o resolve.

## Padrão de mensagem de commit

Conventional Commits, **em português**, só o assunto — sem corpo, sem escopo, sem rodapé (nada de
`Co-authored-by`).

Formato: `<tipo>: <verbo no infinitivo> <complemento>` — minúsculo depois do tipo, sem ponto final,
até ~72 chars.

Tipos usados nos repos (frequência real): `feat` > `chore` > `fix` > `refactor` > `docs` / `test`.

- `feat` — funcionalidade nova ou ampliada
- `fix` — correção de bug/comportamento
- `chore` — docs, README, migrations, scaffold, reorganização sem lógica
- `refactor` — renomear/reestruturar sem mudar comportamento
- `docs` / `test` — quando a mudança é só documentação ou só teste

Exemplos do histórico: `feat: adicionar upload de avatar do canal`, `fix: corrigir botão de
reações`, `chore: atualizar README`, `refactor: renomear projeto`.

O assunto diz o **comportamento**, não o arquivo: descreva a regra que passou a valer. Se o diff não
couber em um assunto, é sinal de que virou mais de um commit.

Título de PR (squash merge): `Feature/nome-da-branch (#N)`.

## Onde commitar

- **Padrão: direto na `master`.** Coisa pequena e bugfix não abre branch.
- **Exceção: feature grande** (vários commits). Aí **pergunte ao usuário** se é para criar
  `feature/<topic>` ou mandar direto para `master` — nunca decida sozinho.
- `master` sempre deployável; produção sai de tags `vX.Y.Z` (estratégia pretendida — ainda não há
  tag nenhuma). Uma tag versiona **os três serviços de uma vez**: é o preço, e a vantagem, do
  monorepo — nunca existe combinação de versões que não foi testada junta. Ver `docs/MONOREPO.md`.
- **Não existe pipeline de deploy.** `.github/workflows/{api,front,processor}.yml` só buildam e
  testam, um por serviço, com filtro de path. Deploy é manual.

## Nunca commitar sem pedido explícito

1. Implementar as mudanças
2. Rodar os testes e verificar que passam
3. Mostrar o que mudou e **sugerir um** título de commit (não uma lista de opções)
4. Esperar o usuário aprovar ou pedir o commit

## Código ruim no caminho

Achou código ruim **perto** do que você está mexendo — mal escrito, deprecado, não utilizado,
desatualizado, não otimizado, duplicado, comentário mentindo sobre o código — **pergunte antes de
agir**. Sempre as três opções, e o usuário escolhe:

1. **aproveitar e ajustar agora**
2. **só deixar um TODO** (no código, junto do trecho)
3. **ignorar**

Nunca decida sozinho por nenhuma das três — nem "arrumo de passagem porque é rápido", nem "deixo
quieto porque está fora do escopo". Diga o que é, onde está (`arquivo:linha`) e por que é ruim, em
uma ou duas linhas, e pergunte. Vale para o que você encontra de passagem; o que o pedido do usuário
já cobre não é "de passagem", é o trabalho.

## Depois de qualquer mudança

1. **Rodar os testes** do serviço que você tocou (`dotnet test` / `bun run test` / `go test ./...`).
2. **Atualizar as docs afetadas na mesma leva** — o gatilho de cada arquivo está no `CLAUDE.md` do
   serviço. Doc desatualizada custa mais que código faltando: o `TODO.md` desta raiz tem uma seção
   inteira ("Documentação que mente") que só existiu por isso.
3. **Sugerir o título do commit** e parar.
