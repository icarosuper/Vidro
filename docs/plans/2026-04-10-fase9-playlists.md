# Fase 9 — Playlists

## Objetivo

Impl criação/edição/exclusão/gerenciamento vídeos em playlists. Playlists: escopo `User` ou `Channel`, visibilidade pública/privada.

---

## Contratos da API

### Endpoints

| Método | Path | Auth | Descrição |
|---|---|---|---|
| `POST` | `/v1/playlists` | required | Criar playlist |
| `GET` | `/v1/playlists/{playlistId}` | optional | Buscar playlist + items |
| `PUT` | `/v1/playlists/{playlistId}` | required | Editar nome/desc/visibilidade |
| `DELETE` | `/v1/playlists/{playlistId}` | required | Deletar playlist |
| `GET` | `/v1/users/{username}/channels/{handle}/playlists` | optional | Listar playlists canal (cursor) |
| `GET` | `/v1/users/{username}/playlists` | optional | Listar playlists pessoais (cursor) |
| `POST` | `/v1/playlists/{playlistId}/items` | required | Adicionar vídeo |
| `DELETE` | `/v1/playlists/{playlistId}/items/{videoId}` | required | Remover vídeo |

### Shapes relevantes

**`POST /v1/playlists` — request:**
```ts
{ name: string; description?: string; visibility: PlaylistVisibility; scope: PlaylistScope; channelId?: string }
```
> `channelId` obrigatório quando `scope === PlaylistScope.Channel`.

**`POST /v1/playlists` — response:**
```ts
{ playlistId: string }
```

**`GET /v1/playlists/{playlistId}` — response:**
```ts
{
  playlistId: string; name: string; description?: string;
  visibility: EnumValue; scope: EnumValue; videoCount: number;
  channelId?: string; createdAt: string;
  items: { videoId: string; title: string; thumbnailUrl?: string; durationSeconds?: number }[]
}
```

**`GET /v1/channels/{channelId}/playlists` e `/v1/users/{userId}/playlists` — response (`PagedResult`):**
```ts
{ items: PlaylistSummary[]; nextCursor?: string }
// PlaylistSummary: { id, name, description?, visibility: EnumValue, videoCount, createdAt }
```

**`PUT /v1/playlists/{playlistId}` — request:**
```ts
{ name: string; description?: string; visibility: PlaylistVisibility }
```

**`POST /v1/playlists/{playlistId}/items` — request:**
```ts
{ videoId: string }
```

### Regras negócio (backend)
- Playlist `Channel`: só aceita vídeos do próprio canal.
- Playlist privada: retorna 404 p/ não-dono (GET individual + listagens).
- `listPlaylistsByUser`: só retorna `scope === User`.
- `listPlaylistsByChannel`: só retorna `scope === Channel`.

---

## Arquivos a criar/editar

### `src/features/playlists/`

| Arquivo | O que implementar |
|---|---|
| `types.ts` | `PlaylistSummary`, `Playlist`, `PlaylistItem`, `PlaylistsPage`, `CreatePlaylistRequest`, `CreatePlaylistResponse`, `UpdatePlaylistRequest` |
| `api.ts` | `createPlaylist`, `getPlaylist`, `updatePlaylist`, `deletePlaylist`, `listChannelPlaylists`, `listUserPlaylists`, `addVideoToPlaylist`, `removeVideoFromPlaylist` |
| `hooks.ts` | `usePlaylist`, `useChannelPlaylists`, `useUserPlaylists`, `useCreatePlaylist`, `useUpdatePlaylist`, `useDeletePlaylist`, `useAddVideoToPlaylist`, `useRemoveVideoFromPlaylist`, `playlistKeys` |
| `components/CreatePlaylistForm.tsx` | Form criação (nome, desc, visibilidade, escopo + channelId opcional) |
| `components/EditPlaylistForm.tsx` | Form edição (nome, desc, visibilidade) |
| `components/PlaylistCard.tsx` | Card: nome, videoCount, badge visibilidade |
| `components/PlaylistItemList.tsx` | Lista vídeos + botão remover (owner-only) |

### Rotas novas

| Rota | Arquivo | Estratégia | Auth | Descrição |
|---|---|---|---|---|
| `/playlists/$playlistId` | `routes/playlists.$playlistId.tsx` | SSR (público, owner vê itens privados) | optional | Detalhe playlist |

### Rotas editadas

| Rota | Arquivo | Mudança |
|---|---|---|
| `/$username/$channel` | `routes/$username.$channel.tsx` | Add aba/seção "Playlists" do canal |
| `/dashboard` | `routes/dashboard.tsx` | Add seção gerenciar playlists pessoais |

---

## Passos de implementação

- ✅ **1. Types** — `src/features/playlists/types.ts`
- ✅ **2. API** — `src/features/playlists/api.ts`
- ✅ **3. Hooks** — `src/features/playlists/hooks.ts` com `playlistKeys`
- ✅ **4. Components**
  - ✅ `PlaylistCard.tsx`
  - ✅ `CreatePlaylistForm.tsx`
  - ✅ `EditPlaylistForm.tsx`
  - ✅ `PlaylistItemList.tsx`
- ✅ **5. Rota `/playlists/$playlistId`** — detalhe SSR
- ✅ **6. Canal page** — add seção playlists em `/$username/$channel`
- ✅ **7. Dashboard** — add gestão playlists pessoais
- ✅ **8. Atualizar `docs/agents/features-index.md`**
- ✅ **9. Rodar testes**

---

## Notas

- `PlaylistVisibility` e `PlaylistScope` já em `src/shared/types.ts`.
- Rota detalhe: load playlist via loader SSR p/ SEO; hydration normal p/ ações owner.
- `addVideoToPlaylist` invalida `playlistKeys.detail(playlistId)` após sucesso.
- Playlist `Channel`: aparece só na page do canal. Playlist `User`: aparece no dashboard + page pública do usuário (confirmar se backend expõe `userId` em `UserProfile`).