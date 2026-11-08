# Features index

Maps each feature: files, endpoints, hooks, types. **Update it whenever you add/remove endpoints, hooks or components.**

Every feature's `types.ts` is a barrel: the names listed below are re-exports of the generated
aliases in `#/shared/api` (`src/shared/api/types.ts`), not hand-written shapes. See "Generated API
types" in [architecture.md](architecture.md).

## auth — `src/features/auth/`

Auth (signup/signin/signout), token renewal, global modal. See [auth.md](auth.md) for the pattern.

| File | Contents |
|---|---|
| `server.ts` | `serverSignUp`, `serverSignIn`, `serverSignOut`, `renewToken`, `getInitialToken` |
| `hooks.tsx` | `AuthProvider`, `AuthModalProvider`, `useIsAuthenticated`, `useSignIn`, `useSignUp`, `useSignOut`, `useAuthModal` |
| `types.ts` | Re-exports from `#/shared/api`: `SignUpRequest`, `SignInRequest`, `AuthTokens` |
| `components/AuthModal.tsx` | Global login/signup modal |
| `components/SignInForm.tsx` | |
| `components/SignUpForm.tsx` | |

**Backend endpoints (via server.ts):**
- `POST /v1/auth/signup`
- `POST /v1/auth/signin`
- `POST /v1/auth/signout`
- `POST /v1/auth/renew-token`

---

## users — `src/features/users/`

Authenticated user's profile + avatar upload.

| File | Contents |
|---|---|
| `api.ts` | `getMe`, `uploadAvatar` |
| `hooks.ts` | `useCurrentUser` (only fetches with a session — `skipToken` signed out, starts the moment the token arrives), `useUploadAvatar`, `userKeys.me()` |
| `types.ts` | Re-exports from `#/shared/api`: `UserProfile`, `UploadAvatarResponse` |
| `components/ProfileInfo.tsx` | |
| `components/AvatarUpload.tsx` | Upload via presigned URL |

**Backend endpoints:**
- `GET /v1/users/me`
- `POST /v1/users/me/avatar` (returns a presigned URL; the upload is a direct PUT to storage)

---

## channels — `src/features/channels/`

Channel CRUD, follow/unfollow, avatar.

| File | Contents |
|---|---|
| `api.ts` | `getUserChannels`, `getChannel`, `createChannel`, `updateChannel`, `followChannel`, `unfollowChannel`, `uploadChannelAvatar` |
| `hooks.ts` | `useUserChannels`, `useChannel`, `useCreateChannel`, `useUpdateChannel`, `useFollowChannel`, `useUnfollowChannel`, `useUploadChannelAvatar` |
| `types.ts` | Re-exports from `#/shared/api`: `Channel`, `ChannelSummary`, `ChannelsResponse`, `CreateChannelRequest/Response`, `UpdateChannelRequest`, `UploadAvatarResponse` |
| `components/CreateChannelForm.tsx` | |
| `components/EditChannelForm.tsx` | |
| `components/SubscribeButton.tsx` | Follow/unfollow toggle with an optimistic feel |

**Backend endpoints:**
- `GET /v1/users/{username}/channels`
- `GET /v1/users/{username}/channels/{handle}`
- `POST /v1/channels`
- `PUT /v1/channels/{handle}`
- `POST /v1/users/{username}/channels/{handle}/follow`
- `DELETE /v1/users/{username}/channels/{handle}/follow`
- `POST /v1/channels/{handle}/avatar`

---

## videos — `src/features/videos/`

Trending, feed, search, detail, reactions, upload (create + presigned + status polling), thumbnail, CRUD, listing by channel.

| File | Contents |
|---|---|
| `api.ts` | `getTrending`, `getFeed`, `searchVideos`, `getVideo`, `registerView`, `reactToVideo`, `removeReaction`, `getChannelVideos`, `createVideo`, `uploadVideoFile` (XHR with progress), `updateVideo`, `uploadThumbnail` |
| `hooks.ts` | `useTrending`, `useFeed`, `useSearchVideos`, `useVideo`, `useRegisterView`, `useReactToVideo`, `useRemoveReaction`, `useVideoStatus`, `useCreateVideo`, `useUpdateVideo`, `useChannelVideos`, `useUploadThumbnail` |
| `server.ts` | `fetchVideoSsr` — loader for `/watch/$videoId` |
| `types.ts` | Re-exports from `#/shared/api`: `Video`, `VideoSummary`, `ChannelVideoSummary`, `TrendingResponse`, `FeedPage`, `SearchPage`, `ChannelVideosPage`, `CreateVideoRequest/Response`, `UpdateVideoRequest/Response`, `ThumbnailUploadResponse` |
| `components/VideoCard.tsx` | Reusable card (home/feed, search and channel). The title link stretches over the card (`after:inset-0`) instead of wrapping it in an `<a>`, so the channel link and the owner buttons (`z-10`) are never nested links. Shows title, channel, mono `views · age` (`formatCompactAge`), up to 3 tags on hover; on hover the extra preview frames cross-fade (`ThumbnailStack`: main frame always underneath, extras mounted on the first hover only, `motion-reduce` skips the fade); likes/dislikes live on the watch page only. Props: `hideChannelInfo` hides the channel avatar/name; `isOwner` shows the edit/thumbnail buttons, status overlay and visibility badge; `isFeatured` spans 2×2 from `sm`. Exports `VideoCardVideo`, the shape it renders, `formatCount` and `formatCompactAge` |
| `components/VideoGrid.tsx` | Takes `VideoCardVideo[]` (the shape `VideoCard` renders), not a generated alias — search results lack `dislikeCount`. `featureFirst` makes the first card `isFeatured` — only on the first grid of a page |
| `components/VideoPlayer.tsx` | HLS player |
| `components/UploadVideoForm.tsx` | Creates the video + upload + polling |
| `components/EditVideoForm.tsx` | |
| `components/visibility-options.ts` | `VIDEO_VISIBILITY_OPTIONS` — the visibility `<Select>` options shared by the upload and edit forms, built from `VideoVisibility` |

**Backend endpoints:**
- `GET /v1/videos/trending?limit`
- `GET /v1/feed?limit&cursor`
- `GET /v1/videos/search?q&limit&cursor` (`limit` required)
- `GET /v1/videos/{videoId}`
- `POST /v1/videos/{videoId}/view`
- `POST /v1/videos/{videoId}/react`
- `DELETE /v1/videos/{videoId}/react`
- `GET /v1/users/{username}/channels/{handle}/videos?limit&cursor`
- `POST /v1/users/{username}/channels/{handle}/videos` (returns a presigned `uploadUrl`)
- `PUT /v1/videos/{videoId}`
- `POST /v1/videos/{videoId}/thumbnail` (presigned)

**Upload flow:** `createVideo` → direct PUT to `uploadUrl` with progress via `XMLHttpRequest` → `useVideoStatus` polls until `VideoStatus.Ready` or `Failed`.

---

## comments — `src/features/comments/`

| File | Contents |
|---|---|
| `api.ts` | `listComments`, `listReplies`, `addComment`, `editComment`, `deleteComment`, `reactToComment`, `removeCommentReaction` |
| `hooks.ts` | `useComments`, `useReplies`, `useAddComment`, `useEditComment`, `useDeleteComment`, `useReactToComment`, `useRemoveCommentReaction`, `commentKeys` |
| `types.ts` | Re-exports from `#/shared/api`: `CommentSummary`, `CommentsPage`, `ReplySummary`, `RepliesPage` |
| `components/CommentList.tsx` | Paginated list with Recent/Popular sort, add form |
| `components/ReplyList.tsx` | Replies lazy-loaded per comment, inline reply form |

**Backend endpoints:**
- `GET /v1/videos/{videoId}/comments?sort&limit&cursor`
- `GET /v1/comments/{commentId}/replies?limit&cursor`
- `POST /v1/videos/{videoId}/comments`
- `PUT /v1/comments/{commentId}`
- `DELETE /v1/comments/{commentId}`
- `POST /v1/comments/{commentId}/reactions`
- `DELETE /v1/comments/{commentId}/reactions`

---

## playlists — `src/features/playlists/`

| File | Contents |
|---|---|
| `api.ts` | `createPlaylist`, `getPlaylist`, `updatePlaylist`, `deletePlaylist`, `listChannelPlaylists`, `listUserPlaylists`, `addVideoToPlaylist`, `removeVideoFromPlaylist` |
| `hooks.ts` | `usePlaylist`, `useChannelPlaylists`, `useUserPlaylists`, `useCreatePlaylist`, `useUpdatePlaylist`, `useDeletePlaylist`, `useAddVideoToPlaylist`, `useRemoveVideoFromPlaylist`, `playlistKeys` |
| `types.ts` | Re-exports from `#/shared/api`: `PlaylistSummary`, `Playlist`, `PlaylistItem`, `PlaylistsPage`, `CreatePlaylistRequest`, `CreatePlaylistResponse`, `UpdatePlaylistRequest` |
| `components/PlaylistCard.tsx` | Summary card with videoCount and visibility badge |
| `components/CreatePlaylistForm.tsx` | Create form (name, description, visibility; scope/channelId via props) |
| `components/EditPlaylistForm.tsx` | Edit form (name, description, visibility) |
| `components/PlaylistItemList.tsx` | Video list with thumbnail, duration and remove button (owner-only) |

**Backend endpoints:**
- `POST /v1/playlists`
- `GET /v1/playlists/{playlistId}`
- `PUT /v1/playlists/{playlistId}`
- `DELETE /v1/playlists/{playlistId}`
- `GET /v1/users/{username}/channels/{handle}/playlists?limit&cursor`
- `GET /v1/users/{username}/playlists?limit&cursor`
- `POST /v1/playlists/{playlistId}/items`
- `DELETE /v1/playlists/{playlistId}/items/{videoId}`

---

## Routes (`src/routes/`)

| Route | File | Strategy | Auth | Features consumed |
|---|---|---|---|---|
| `/` | `index.tsx` | SSR | public | videos (trending/feed; an empty feed is hidden, and the first section shown features its first card) |
| `/watch/$videoId` | `watch.$videoId.tsx` | SSR | public | videos, channels, comments |
| `/search` | `search.tsx` | SSR | public | videos (search; query in `?q=`, field in the `TopBar`'s `SearchBox`) |
| `/$username` | `$username.tsx` + `$username.index.tsx` | SSR | public | users, channels |
| `/$username/$channel` | `$username.$channel.tsx` | SSR | public | channels, videos, playlists |
| `/playlists/$playlistId` | `playlists.$playlistId.tsx` | SSR | optional | playlists |
| `/upload` | `upload.tsx` | client-only | **required** | videos |
| `/dashboard` | `dashboard.tsx` | client-only | **required** | playlists |
| `/settings` | `settings.tsx` | client-only | **required** | users |

## Shared

| Module | File | Contents |
|---|---|---|
| api client | `src/shared/lib/api-client.ts` | `apiClient`, `ApiClientError` (with `correlationId`), `setRenewTokenCallback` |
| correlation id | `src/shared/lib/correlation-id.ts` | `CORRELATION_ID_HEADER`, `newCorrelationId()` — one id per request, sent in `X-Correlation-ID` |
| token store | `src/shared/lib/token-store.ts` | `tokenStore` (get/set/clear/subscribe) |
| API types (aliases) | `src/shared/api/types.ts` (barrel `src/shared/api/index.ts`, imported as `#/shared/api`) | Named aliases over the generated `components['schemas'][...]` — every feature's request/response types, plus `EnumValue` and `ApiError` (the domain error body) |
| API schema (generated) | `src/shared/api/schema.d.ts` | **Generated by `bun run openapi` from `../VidroApi/openapi/v1.json` — do not edit.** Committed, ignored by Biome; CI regenerates it and fails on a diff |
| base types | `src/shared/types.ts` | `ValidationError`, `API_ERROR_CODES`, `VideoStatus`, `VideoVisibility`, `ReactionType` + `ReactionTypeValue` (the value's type; the const is only a value), `PlaylistVisibility`, `PlaylistScope`, `CommentSortOrder`. **The six enums are pinned by `contracts/enums.json`** — changing a value here without changing the golden breaks CI on both sides |
| enum member | `src/shared/lib/enum-member.ts` | `toEnumMember(enumObject, raw)` — turns a `<Select>` string into the typed enum id; throws on an unknown value |
| errors | `src/shared/lib/error-messages.ts` | Map `code → user-facing message` |
| toast | `src/shared/lib/toast-error.ts` | `toastApiError(error)` |
| global components | `src/components/` | `shell/AppShell` — the frame around every route: `AppSidebar` (nav rail from `lg`: Home; Creator: Upload, Dashboard; You: Your channels, Settings; signed out a sign-in prompt), `TopBar` (`BrandLink` below `lg`, `SearchBox` with `/` to focus, `ThemeToggle`, `AccountMenu` below `lg`), `BottomNav` (tab bar below `lg`, signed in only, Upload in the middle), `AccountMenu` (dropdown: Your channels, Dashboard, Settings, Sign out; `layout="avatar"` round trigger in the top bar, `layout="block"` full-width trigger with the name at the bottom of the sidebar; signed out a Sign in button). `ThemeToggle` (light → dark → auto, persisted in `localStorage`; render it **once** — each instance keeps its own mode), `RouteFallback` (`RouteErrorFallback` + `RouteNotFoundFallback`, registered as the router's defaults), `TimeAgo`, `ui/*` (shadcn) |
| integration | `src/integrations/tanstack-query/` | `QueryClient` + devtools |
