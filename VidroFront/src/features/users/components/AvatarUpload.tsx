import { useRef } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import { getApiErrorMessage } from '#/shared/lib/error-messages'
import { useUploadAvatar } from '../hooks'
import type { UserProfile } from '../types'

type AvatarUploadProps = {
  profile: UserProfile
}

const ACCEPTED_IMAGE_TYPES = 'image/jpeg,image/png,image/webp'
const MAX_FILE_SIZE_MB = 5

export function AvatarUpload({ profile }: AvatarUploadProps) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const uploadAvatar = useUploadAvatar()

  function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return

    const fileSizeMB = file.size / (1024 * 1024)
    const fileTooLarge = fileSizeMB > MAX_FILE_SIZE_MB
    if (fileTooLarge) {
      alert(`File must be smaller than ${MAX_FILE_SIZE_MB} MB`)
      return
    }

    uploadAvatar.mutate(file)
    // reset so the same file can be re-selected after a change
    event.target.value = ''
  }

  function handleUploadClick() {
    fileInputRef.current?.click()
  }

  const avatarInitial = profile.username.charAt(0).toUpperCase()

  return (
    <div className="flex items-center gap-4">
      <Avatar className="h-20 w-20">
        <AvatarImage src={profile.avatarUrl ?? undefined} alt={profile.username} />
        <AvatarFallback className="text-2xl">{avatarInitial}</AvatarFallback>
      </Avatar>

      <div className="flex flex-col gap-2">
        <input
          ref={fileInputRef}
          type="file"
          accept={ACCEPTED_IMAGE_TYPES}
          onChange={handleFileChange}
          className="hidden"
        />

        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={handleUploadClick}
          disabled={uploadAvatar.isPending}
        >
          {uploadAvatar.isPending ? 'Uploading…' : 'Change avatar'}
        </Button>

        {uploadAvatar.error && (
          <p className="text-sm text-destructive">{getApiErrorMessage(uploadAvatar.error)}</p>
        )}
      </div>
    </div>
  )
}
