import { VideoVisibility } from '#/shared/types'

// The <Select> options shared by the upload and edit forms. Values are strings because that is
// what <Select> works with; `toEnumMember` turns them back into the id on submit.
export const VIDEO_VISIBILITY_OPTIONS = [
  { label: 'Public', value: String(VideoVisibility.Public) },
  { label: 'Unlisted', value: String(VideoVisibility.Unlisted) },
  { label: 'Private', value: String(VideoVisibility.Private) },
]
