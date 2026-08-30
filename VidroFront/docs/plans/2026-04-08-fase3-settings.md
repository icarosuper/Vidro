# Fase 3 — Settings Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Settings page — auth users edit profile (username, email, bio, birthdate, gender) + upload/remove avatar.

**Architecture:** Vertical slice in `src/features/users/` — types, api.ts (apiClient calls), hooks.ts (TanStack Query). `apiClient` gets `upload` method for multipart form-data. `/settings` route (already scaffolded) renders assembled page component.

**Tech Stack:** TanStack Query, react-hook-form + zod, shadcn/ui (Select, Textarea, Avatar, Card, Separator — install needed), Tailwind CSS v4.

---

## Task 1: Install shadcn components

**Files:**
- No file changes — CLI installs into `src/components/ui/`

**Step 1: Install missing shadcn components**

```bash
bunx --bun shadcn@latest add select textarea avatar card separator
```

Expected: creates `src/components/ui/select.tsx`, `textarea.tsx`, `avatar.tsx`, `card.tsx`, `separator.tsx`.

---

## Task 2: Users types — `src/features/users/types.ts`

**Files:**
- Create: `src/features/users/types.ts`

**Step 1: Create the file**

```ts
import type { EnumValue } from '#/shared/types'

export type UserProfile = {
  id: string
  username: string
  email: string
  bio: string | null
  birthdate: string | null   // ISO 8601 date string e.g. "1995-06-15"
  avatarUrl: string | null
  gender: EnumValue | null
  createdAt: string
}

export type UpdateProfileRequest = {
  username: string
  email: string
  bio: string | null
  birthdate: string | null
  genderId: number | null
}
```

No test needed — pure type declarations.

---

## Task 3: Extend `apiClient` with `upload` method

`apiClient.post` always sets `Content-Type: application/json` + JSON-stringifies body. Avatar upload needs `multipart/form-data` — browser must set `Content-Type` itself (with boundary). Need dedicated `upload` method.

**Files:**
- Modify: `src/shared/lib/api-client.ts`

**Step 1: Add `uploadRequest` helper and expose it as `apiClient.upload`**

Add private helper right after existing `request` function (before `apiClient` object):

```ts
async function uploadRequest<T>(
  method: string,
  path: string,
  formData: FormData,
  signal?: AbortSignal,
): Promise<T> {
  const token = tokenStore.get()

  const headers: Record<string, string> = {}
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }
  // Do NOT set Content-Type — browser sets it with the multipart boundary

  const response = await fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: formData,
    signal,
  })

  if (response.status === 204) {
    return undefined as T
  }

  const data = await response.json()

  if (!response.ok) {
    const error = data as ApiError
    throw new ApiClientError(error.code, error.message, response.status)
  }

  return (data as { data: T }).data
}
```

Then add to `apiClient`:

```ts
upload: <T>(path: string, formData: FormData, signal?: AbortSignal) =>
  uploadRequest<T>('POST', path, formData, signal),
```

**Step 2: Run existing tests to confirm nothing broke**

```bash
bun run test
```

Expected: all 14 tests still passing.

---

## Task 4: Users API — `src/features/users/api.ts`

**Files:**
- Create: `src/features/users/api.ts`

**Step 1: Create the file**

```ts
import { apiClient } from '#/shared/lib/api-client'
import type { EnumValue } from '#/shared/types'
import type { UpdateProfileRequest, UserProfile } from './types'

export function getMe(signal?: AbortSignal) {
  return apiClient.get<UserProfile>('/v1/users/me', signal)
}

export function updateMe(body: UpdateProfileRequest, signal?: AbortSignal) {
  return apiClient.put<UserProfile>('/v1/users/me', body, signal)
}

export function uploadAvatar(file: File, signal?: AbortSignal) {
  const formData = new FormData()
  formData.append('file', file)
  return apiClient.upload<{ avatarUrl: string }>('/v1/users/me/avatar', formData, signal)
}

export function deleteAvatar(signal?: AbortSignal) {
  return apiClient.delete<void>('/v1/users/me/avatar', signal)
}

export function getGenderOptions(signal?: AbortSignal) {
  return apiClient.get<EnumValue[]>('/v1/enums/gender', signal)
}
```

No isolated test — thin wrappers; integration covered by hook tests.

---

## Task 5: Users hooks — `src/features/users/hooks.ts`

**Files:**
- Create: `src/features/users/hooks.ts`

**Step 1: Create the file**

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { deleteAvatar, getGenderOptions, getMe, updateMe, uploadAvatar } from './api'
import type { UpdateProfileRequest } from './types'

export const userKeys = {
  me: () => ['users', 'me'] as const,
  genderOptions: () => ['enums', 'gender'] as const,
}

export function useCurrentUser() {
  return useQuery({
    queryKey: userKeys.me(),
    queryFn: ({ signal }) => getMe(signal),
  })
}

export function useGenderOptions() {
  return useQuery({
    queryKey: userKeys.genderOptions(),
    queryFn: ({ signal }) => getGenderOptions(signal),
    staleTime: Number.POSITIVE_INFINITY, // enum values never change
  })
}

export function useUpdateProfile() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: UpdateProfileRequest) => updateMe(request),
    onSuccess: (updatedProfile) => {
      queryClient.setQueryData(userKeys.me(), updatedProfile)
    },
  })
}

export function useUploadAvatar() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (file: File) => uploadAvatar(file),
    onSuccess: ({ avatarUrl }) => {
      queryClient.setQueryData(userKeys.me(), (prev: Parameters<typeof useCurrentUser>[0]) => {
        if (!prev) return prev
        return { ...prev, avatarUrl }
      })
    },
  })
}

export function useDeleteAvatar() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => deleteAvatar(),
    onSuccess: () => {
      queryClient.setQueryData(userKeys.me(), (prev: Parameters<typeof useCurrentUser>[0]) => {
        if (!prev) return prev
        return { ...prev, avatarUrl: null }
      })
    },
  })
}
```

> **Note on `onSuccess` typing:** `prev` param type in `setQueryData` callbacks should be `UserProfile | undefined`. Replace generic cast with explicit import:
```ts
import type { UserProfile } from './types'
// ...
onSuccess: ({ avatarUrl }) => {
  queryClient.setQueryData(userKeys.me(), (prev: UserProfile | undefined) => {
    if (!prev) return prev
    return { ...prev, avatarUrl }
  })
},
```
Apply same pattern in `useDeleteAvatar`.

---

## Task 6: `AvatarUpload` component — `src/features/users/components/AvatarUpload.tsx`

Renders current avatar (or placeholder), upload button, remove button.

**Files:**
- Create: `src/features/users/components/AvatarUpload.tsx`

**Step 1: Create the file**

```tsx
import { useRef } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import { useDeleteAvatar, useUploadAvatar } from '../hooks'
import type { UserProfile } from '../types'

type AvatarUploadProps = {
  profile: UserProfile
}

const ACCEPTED_IMAGE_TYPES = 'image/jpeg,image/png,image/webp'
const MAX_FILE_SIZE_MB = 5

export function AvatarUpload({ profile }: AvatarUploadProps) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const uploadAvatar = useUploadAvatar()
  const deleteAvatar = useDeleteAvatar()

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
    // reset input so the same file can be re-selected after removal
    event.target.value = ''
  }

  function handleUploadClick() {
    fileInputRef.current?.click()
  }

  function handleDeleteClick() {
    deleteAvatar.mutate()
  }

  const avatarInitial = profile.username.charAt(0).toUpperCase()
  const isBusy = uploadAvatar.isPending || deleteAvatar.isPending

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
          disabled={isBusy}
        >
          {uploadAvatar.isPending ? 'Uploading…' : 'Change avatar'}
        </Button>

        {profile.avatarUrl && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={handleDeleteClick}
            disabled={isBusy}
          >
            {deleteAvatar.isPending ? 'Removing…' : 'Remove avatar'}
          </Button>
        )}

        {(uploadAvatar.error || deleteAvatar.error) && (
          <p className="text-sm text-destructive">
            {(uploadAvatar.error ?? deleteAvatar.error)?.message}
          </p>
        )}
      </div>
    </div>
  )
}
```

---

## Task 7: `ProfileForm` component — `src/features/users/components/ProfileForm.tsx`

Form: edit username, email, bio, birthdate, gender. Pre-populated from current profile.

**Files:**
- Create: `src/features/users/components/ProfileForm.tsx`

**Step 1: Create the file**

```tsx
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '#/components/ui/button'
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '#/components/ui/form'
import { Input } from '#/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '#/components/ui/select'
import { Textarea } from '#/components/ui/textarea'
import { useGenderOptions, useUpdateProfile } from '../hooks'
import type { UserProfile } from '../types'

const profileSchema = z.object({
  username: z.string().min(3, 'Username must be at least 3 characters'),
  email: z.string().min(1, 'Email is required').email('Invalid email'),
  bio: z.string().max(300, 'Bio must be at most 300 characters').nullable(),
  birthdate: z.string().nullable(),
  genderId: z.coerce.number().nullable(),
})

type ProfileFormValues = z.infer<typeof profileSchema>

type ProfileFormProps = {
  profile: UserProfile
}

const NO_GENDER_VALUE = '__none__'

export function ProfileForm({ profile }: ProfileFormProps) {
  const updateProfile = useUpdateProfile()
  const { data: genderOptions = [] } = useGenderOptions()

  const form = useForm<ProfileFormValues>({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      username: profile.username,
      email: profile.email,
      bio: profile.bio ?? null,
      birthdate: profile.birthdate ?? null,
      genderId: profile.gender?.id ?? null,
    },
  })

  async function handleSubmit(values: ProfileFormValues) {
    await updateProfile.mutateAsync(values)
  }

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(handleSubmit)} className="space-y-4">
        <FormField
          control={form.control}
          name="username"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Username</FormLabel>
              <FormControl>
                <Input placeholder="your_username" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="email"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Email</FormLabel>
              <FormControl>
                <Input type="email" placeholder="you@example.com" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="bio"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Bio</FormLabel>
              <FormControl>
                <Textarea
                  placeholder="Tell us about yourself…"
                  rows={3}
                  {...field}
                  value={field.value ?? ''}
                  onChange={(e) => field.onChange(e.target.value || null)}
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="birthdate"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Birthdate</FormLabel>
              <FormControl>
                <Input
                  type="date"
                  {...field}
                  value={field.value ?? ''}
                  onChange={(e) => field.onChange(e.target.value || null)}
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="genderId"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Gender</FormLabel>
              <Select
                onValueChange={(value) =>
                  field.onChange(value === NO_GENDER_VALUE ? null : Number(value))
                }
                value={field.value !== null ? String(field.value) : NO_GENDER_VALUE}
              >
                <FormControl>
                  <SelectTrigger>
                    <SelectValue placeholder="Select gender" />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  <SelectItem value={NO_GENDER_VALUE}>Prefer not to say</SelectItem>
                  {genderOptions.map((option) => (
                    <SelectItem key={option.id} value={String(option.id)}>
                      {option.value}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FormMessage />
            </FormItem>
          )}
        />

        {updateProfile.error && (
          <p className="text-sm text-destructive">{updateProfile.error.message}</p>
        )}

        {updateProfile.isSuccess && (
          <p className="text-sm text-green-600">Profile updated successfully.</p>
        )}

        <Button type="submit" disabled={updateProfile.isPending}>
          {updateProfile.isPending ? 'Saving…' : 'Save changes'}
        </Button>
      </form>
    </Form>
  )
}
```

---

## Task 8: Assemble `/settings` route — `src/routes/settings.tsx`

**Files:**
- Modify: `src/routes/settings.tsx`

**Step 1: Replace stub with real page**

```tsx
import { createFileRoute, redirect } from '@tanstack/react-router'
import { Card, CardContent, CardHeader, CardTitle } from '#/components/ui/card'
import { Separator } from '#/components/ui/separator'
import { AvatarUpload } from '#/features/users/components/AvatarUpload'
import { ProfileForm } from '#/features/users/components/ProfileForm'
import { useCurrentUser } from '#/features/users/hooks'
import { tokenStore } from '#/shared/lib/token-store'

export const Route = createFileRoute('/settings')({
  beforeLoad: () => {
    const isAuthenticated = tokenStore.get() !== null
    if (!isAuthenticated) {
      throw redirect({ to: '/' })
    }
  },
  component: SettingsPage,
})

function SettingsPage() {
  const { data: profile, isPending, isError, error } = useCurrentUser()

  if (isPending) {
    return <main className="page-container py-8"><p>Loading…</p></main>
  }

  if (isError) {
    return (
      <main className="page-container py-8">
        <p className="text-destructive">{error.message}</p>
      </main>
    )
  }

  return (
    <main className="page-container py-8">
      <h1 className="mb-6 text-2xl font-bold">Settings</h1>

      <div className="max-w-lg space-y-6">
        <Card>
          <CardHeader>
            <CardTitle>Avatar</CardTitle>
          </CardHeader>
          <CardContent>
            <AvatarUpload profile={profile} />
          </CardContent>
        </Card>

        <Separator />

        <Card>
          <CardHeader>
            <CardTitle>Profile</CardTitle>
          </CardHeader>
          <CardContent>
            <ProfileForm profile={profile} />
          </CardContent>
        </Card>
      </div>
    </main>
  )
}
```

---

## Task 9: Tests — `src/tests/users.test.ts`

Tests for `useCurrentUser`, `useUpdateProfile`, `useUploadAvatar`, `useDeleteAvatar` via mock fetch. Uses `@testing-library/react` with minimal Query wrapper.

**Files:**
- Create: `src/tests/users.test.ts`

**Step 1: Check existing testing utilities**

Look at `src/tests/auth.test.ts` for `mockResponse` pattern. Replicate it.

**Step 2: Create the test file**

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore } from '#/shared/lib/token-store'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

function mockResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

const profileFixture = {
  id: 'user-1',
  username: 'testuser',
  email: 'test@example.com',
  bio: null,
  birthdate: null,
  avatarUrl: null,
  gender: null,
  createdAt: '2024-01-01T00:00:00Z',
}

beforeEach(() => {
  tokenStore.set('test-token')
  mockFetch.mockReset()
})

afterEach(() => {
  tokenStore.clear()
  vi.restoreAllMocks()
})

describe('users api', () => {
  it('getMe returns the current user profile', async () => {
    mockFetch.mockResolvedValueOnce(mockResponse({ data: profileFixture }))
    const { getMe } = await import('#/features/users/api')

    const result = await getMe()

    expect(result).toEqual(profileFixture)
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/users/me'),
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('updateMe sends PUT and returns updated profile', async () => {
    const updatedProfile = { ...profileFixture, username: 'newname' }
    mockFetch.mockResolvedValueOnce(mockResponse({ data: updatedProfile }))
    const { updateMe } = await import('#/features/users/api')

    const result = await updateMe({
      username: 'newname',
      email: 'test@example.com',
      bio: null,
      birthdate: null,
      genderId: null,
    })

    expect(result.username).toBe('newname')
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/users/me'),
      expect.objectContaining({ method: 'PUT' }),
    )
  })

  it('uploadAvatar sends FormData and returns avatarUrl', async () => {
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { avatarUrl: 'https://cdn.example.com/avatar.jpg' } }),
    )
    const { uploadAvatar } = await import('#/features/users/api')

    const file = new File(['img'], 'avatar.jpg', { type: 'image/jpeg' })
    const result = await uploadAvatar(file)

    expect(result.avatarUrl).toBe('https://cdn.example.com/avatar.jpg')
    const [, options] = mockFetch.mock.calls[0]
    expect(options.body).toBeInstanceOf(FormData)
    expect(options.headers['Content-Type']).toBeUndefined()
  })

  it('deleteAvatar sends DELETE to /v1/users/me/avatar', async () => {
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { deleteAvatar } = await import('#/features/users/api')

    await deleteAvatar()

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/users/me/avatar'),
      expect.objectContaining({ method: 'DELETE' }),
    )
  })

  it('getGenderOptions returns array of EnumValue', async () => {
    const genders = [
      { id: 1, value: 'Male' },
      { id: 2, value: 'Female' },
    ]
    mockFetch.mockResolvedValueOnce(mockResponse({ data: genders }))
    const { getGenderOptions } = await import('#/features/users/api')

    const result = await getGenderOptions()

    expect(result).toEqual(genders)
  })
})
```

**Step 3: Run the tests**

```bash
bun run test src/tests/users.test.ts
```

Expected: 5 tests passing.

**Step 4: Run full test suite**

```bash
bun run test
```

Expected: all tests passing (14 existing + 5 new = 19 total).

---

## Task 10: Update docs

**Files:**
- Modify: `docs/plans/2026-04-07-frontend-design.md` — mark Phase 3 complete in implementation order table.

Update table row:
```
| 3 | Settings (perfil do usuário, avatar) | ✅ Usuário editável para testes |
```

---

## Checklist

- [x] shadcn components installed: select, textarea, avatar, card, separator
- [x] `src/features/users/types.ts` — UserProfile, UpdateProfileRequest
- [x] `apiClient.upload` method added (multipart, no Content-Type header)
- [x] `src/features/users/api.ts` — getMe, updateMe, uploadAvatar, deleteAvatar, getGenderOptions
- [x] `src/features/users/hooks.ts` — useCurrentUser, useGenderOptions, useUpdateProfile, useUploadAvatar, useDeleteAvatar
- [x] `src/features/users/components/AvatarUpload.tsx`
- [x] `src/features/users/components/ProfileForm.tsx`
- [x] `/settings` route assembled with real content
- [x] `src/tests/users.test.ts` — 5 tests passing
- [x] `bun run test` — all tests passing (19 total)
- [x] `docs/plans/2026-04-07-frontend-design.md` updated