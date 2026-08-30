# Features Index

All implemented features and their file paths. Read this before searching for a feature.

## Auth — `src/VidroApi.Api/Features/Auth/`

| Feature | File | Endpoint |
|---|---|---|
| SignUp | `Auth/SignUp.cs` | `POST /v1/auth/signup` |
| SignIn | `Auth/SignIn.cs` | `POST /v1/auth/signin` |
| SignOut | `Auth/SignOut.cs` | `POST /v1/auth/signout` |
| RenewToken | `Auth/RenewToken.cs` | `POST /v1/auth/renew-token` |

## Users — `src/VidroApi.Api/Features/Users/`

| Feature | File | Endpoint |
|---|---|---|
| GetCurrentUser | `Users/GetCurrentUser.cs` | `GET /v1/users/me` |
| UploadAvatar | `Users/UploadAvatar.cs` | `POST /v1/users/me/avatar` |

## Channels — `src/VidroApi.Api/Features/Channels/`

| Feature | File | Endpoint |
|---|---|---|
| CreateChannel | `Channels/CreateChannel.cs` | `POST /v1/channels` |
| GetChannel | `Channels/GetChannel.cs` | `GET /v1/users/{username}/channels/{handle}` |
| UpdateChannel | `Channels/UpdateChannel.cs` | `PUT /v1/channels/{handle}` |
| DeleteChannel | `Channels/DeleteChannel.cs` | `DELETE /v1/channels/{handle}` |
| ListUserChannels | `Channels/ListUserChannels.cs` | `GET /v1/users/{username}/channels` |
| FollowChannel | `Channels/FollowChannel.cs` | `POST /v1/users/{username}/channels/{handle}/follow` |
| UnfollowChannel | `Channels/UnfollowChannel.cs` | `DELETE /v1/users/{username}/channels/{handle}/follow` |
| UploadChannelAvatar | `Channels/UploadChannelAvatar.cs` | `POST /v1/channels/{handle}/avatar` |

## Videos — `src/VidroApi.Api/Features/Videos/`

| Feature | File | Endpoint |
|---|---|---|
| CreateVideo | `Videos/CreateVideo.cs` | `POST /v1/users/{username}/channels/{handle}/videos` |
| GetVideo | `Videos/GetVideo.cs` | `GET /v1/videos/{videoId}` |
| UpdateVideo | `Videos/UpdateVideo.cs` | `PUT /v1/videos/{videoId}` |
| DeleteVideo | `Videos/DeleteVideo.cs` | `DELETE /v1/videos/{videoId}` |
| ListChannelVideos | `Videos/ListChannelVideos.cs` | `GET /v1/users/{username}/channels/{handle}/videos` |
| ListFeedVideos | `Videos/ListFeedVideos.cs` | `GET /v1/feed` |
| ListTrendingVideos | `Videos/ListTrendingVideos.cs` | `GET /v1/videos/trending` |
| SearchVideos | `Videos/SearchVideos.cs` | `GET /v1/videos/search` |
| ReactToVideo | `Videos/ReactToVideo.cs` | `POST /v1/videos/{videoId}/react` |
| RemoveReaction | `Videos/RemoveReaction.cs` | `DELETE /v1/videos/{videoId}/react` |
| RegisterVideoView | `Videos/RegisterVideoView.cs` | `POST /v1/videos/{videoId}/view` |
| UploadVideoThumbnail | `Videos/UploadVideoThumbnail.cs` | `POST /v1/videos/{videoId}/thumbnail` |
| MinioUploadCompleted | `Videos/MinioUploadCompleted.cs` | `POST /webhooks/minio-upload-completed` |
| VideoProcessed | `Videos/VideoProcessed.cs` | `POST /webhooks/video-processed` |

## Operational endpoints

Not features — mapped directly in `Program.cs`.

| Endpoint | What |
|---|---|
| `GET /health` | Liveness only, no dependency probes |
| `GET /openapi/v1.json` | OpenAPI document, Development only |

## Comments — `src/VidroApi.Api/Features/Comments/`

| Feature | File | Endpoint |
|---|---|---|
| AddComment | `Comments/AddComment.cs` | `POST /v1/videos/{videoId}/comments` |
| ListComments | `Comments/ListComments.cs` | `GET /v1/videos/{videoId}/comments` |
| ListReplies | `Comments/ListReplies.cs` | `GET /v1/comments/{commentId}/replies` |
| EditComment | `Comments/EditComment.cs` | `PUT /v1/comments/{commentId}` |
| DeleteComment | `Comments/DeleteComment.cs` | `DELETE /v1/comments/{commentId}` |
| ReactToComment | `Comments/ReactToComment.cs` | `POST /v1/comments/{commentId}/reactions` |
| RemoveCommentReaction | `Comments/RemoveCommentReaction.cs` | `DELETE /v1/comments/{commentId}/reactions` |

## Playlists — `src/VidroApi.Api/Features/Playlists/`

| Feature | File | Endpoint |
|---|---|---|
| CreatePlaylist | `Playlists/CreatePlaylist.cs` | `POST /v1/playlists` |
| GetPlaylist | `Playlists/GetPlaylist.cs` | `GET /v1/playlists/{playlistId}` |
| UpdatePlaylist | `Playlists/UpdatePlaylist.cs` | `PUT /v1/playlists/{playlistId}` |
| DeletePlaylist | `Playlists/DeletePlaylist.cs` | `DELETE /v1/playlists/{playlistId}` |
| ListPlaylistsByChannel | `Playlists/ListPlaylistsByChannel.cs` | `GET /v1/users/{username}/channels/{handle}/playlists` |
| ListPlaylistsByUser | `Playlists/ListPlaylistsByUser.cs` | `GET /v1/users/{username}/playlists` |
| AddVideoToPlaylist | `Playlists/AddVideoToPlaylist.cs` | `POST /v1/playlists/{playlistId}/items` |
| RemoveVideoFromPlaylist | `Playlists/RemoveVideoFromPlaylist.cs` | `DELETE /v1/playlists/{playlistId}/items/{videoId}` |