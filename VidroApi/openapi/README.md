# `openapi/v1.json` — a superfície de URL da API, versionada

Este arquivo é **gerado**, não escrito à mão, e é versionado de propósito: é o contrato de URL
que o `VidroFront` consome. O workflow `.github/workflows/api.yml` regenera e falha se o diff
não for vazio, então uma rota não muda sem aparecer no review.

Existe porque o P0.1 do [`TODO.md`](../../TODO.md) foi exatamente isso: **4 rotas erradas** no
`docs/agents/features-index.md`, que ninguém tinha como conferir.

## Regenerar

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet build src/VidroApi.Api -p:GenerateOpenApiDocument=true
```

Duas coisas fora do comum nesse comando, ambas por necessidade:

- **`-p:GenerateOpenApiDocument=true`** — a geração fica atrás de uma propriedade
  (`VidroApi.Api.csproj`). O `dotnet build` de todo dia não paga o preço abaixo.
- **`ASPNETCORE_ENVIRONMENT=Development`** — o gerador (`dotnet-getdocument`) **boota o host**
  para ler as rotas, e `AddInfrastructure` monta o client do MinIO na hora, com o endpoint da
  config. O endpoint só existe em `appsettings.Development.json`; sem ele o boot estoura.
  Nada é conectado — só o endpoint precisa ser uma string válida.

Pelo mesmo motivo, `Program.cs` pula a migration quando o processo é o gerador
(`GetDocument.Insider`): gerar o contrato não pode depender de um Postgres de pé.

## O que este arquivo **não** cobre

Vale ler antes de contar com ele para mais do que ele entrega — hoje as respostas da API não
estão descritas:

| | estado |
|---|---|
| Rotas, métodos, parâmetros de path e query | **43 de 43**, corretos |
| Schema de **resposta** | **0 de 43** — os handlers devolvem `IResult`, sem `TypedResults`/`.Produces<T>()` |
| Schema de request | 16 operações, **um schema só**, chamado `Request` — o record aninhado de cada slice colide |
| Enums | `VideoVisibility` e `CommentSortOrder` saem como `{"type":"integer"}`, **sem os valores**; os outros quatro nem aparecem |

Ou seja: **gerar os tipos do front a partir daqui hoje não travaria nada além das URLs.** O
contrato dos seis enums é travado por outro caminho, o golden de
[`contracts/enums.json`](../../contracts/README.md).

Para este arquivo virar fonte de tipos de verdade, a API precisa antes declarar as respostas
(`TypedResults`/`.Produces<T>()` nas 43 features), dar schema ID por feature para resolver a
colisão de `Request`, e um transformer para os enums saírem com valores. Está registrado no
`TODO.md`.
