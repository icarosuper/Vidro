import { describe, expect, it } from 'vitest'
import { pickUpNextVideos } from '#/features/videos/components/UpNextList'
import type { VideoCardVideo } from '#/features/videos/components/VideoCard'

function makeVideo(
  videoId: string,
  overrides: Partial<VideoCardVideo> = {},
): VideoCardVideo {
  return {
    videoId,
    thumbnailUrls: [],
    title: videoId,
    ownerUsername: 'mango',
    channelHandle: 'tears-of-steel',
    channelAvatarUrl: null,
    tags: [],
    viewCount: 0,
    likeCount: 0,
    createdAt: '2026-11-07T00:00:00Z',
    ...overrides,
  }
}

const idsOf = (videos: VideoCardVideo[]) => videos.map((video) => video.videoId)

describe('pickUpNextVideos', () => {
  it('puts the channel first and fills with trending, without the current video', () => {
    const channelVideos = [
      makeVideo('current'),
      makeVideo('c1'),
      makeVideo('c2'),
    ]
    const trendingVideos = [makeVideo('t1'), makeVideo('current')]

    const picked = pickUpNextVideos('current', channelVideos, trendingVideos)

    expect(idsOf(picked)).toEqual(['c1', 'c2', 't1'])
  })

  it('lists a video in both sources only once', () => {
    const picked = pickUpNextVideos(
      'current',
      [makeVideo('shared')],
      [makeVideo('shared'), makeVideo('t1')],
    )

    expect(idsOf(picked)).toEqual(['shared', 't1'])
  })

  it("skips the owner's private and still-processing videos", () => {
    const channelVideos = [
      makeVideo('private', { visibility: { id: 2, value: 'Private' } }),
      makeVideo('processing', { status: { id: 1, value: 'Processing' } }),
      makeVideo('public', {
        visibility: { id: 0, value: 'Public' },
        status: { id: 2, value: 'Ready' },
      }),
    ]

    const picked = pickUpNextVideos('current', channelVideos, [])

    expect(idsOf(picked)).toEqual(['public'])
  })

  it('stops at the limit', () => {
    const trendingVideos = ['a', 'b', 'c', 'd'].map((id) => makeVideo(id))

    const picked = pickUpNextVideos('current', [], trendingVideos, 2)

    expect(idsOf(picked)).toEqual(['a', 'b'])
  })
})
