import { Input } from '#/components/ui/input'
import { Label } from '#/components/ui/label'
import type { UserProfile } from '../types'

type ProfileInfoProps = {
  profile: UserProfile
}

export function ProfileInfo({ profile }: ProfileInfoProps) {
  return (
    <div className="space-y-4">
      <div className="space-y-1">
        <Label>Username</Label>
        <Input value={profile.username} disabled />
      </div>

      <div className="space-y-1">
        <Label>Email</Label>
        <Input value={profile.email} disabled />
      </div>
    </div>
  )
}
