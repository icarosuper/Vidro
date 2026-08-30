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
import { getApiErrorMessage } from '#/shared/lib/error-messages'
import { useSignIn, useSignUp } from '../hooks'

const signUpSchema = z.object({
  username: z
    .string()
    .min(3, 'Username must be at least 3 characters')
    .max(25, 'Username must be at most 25 characters')
    .regex(/^[a-z0-9_]+$/, 'Only lowercase letters, numbers and _ allowed'),
  email: z.string().min(1, 'Email is required').email('Invalid email'),
  password: z.string().min(8, 'Password must be at least 8 characters'),
})

type SignUpFormValues = z.infer<typeof signUpSchema>

type SignUpFormProps = {
  onSuccess: () => void
  onSwitchToSignIn: () => void
}

export function SignUpForm({ onSuccess, onSwitchToSignIn }: SignUpFormProps) {
  const signUp = useSignUp()
  const signIn = useSignIn()

  const form = useForm<SignUpFormValues>({
    resolver: zodResolver(signUpSchema),
    defaultValues: { username: '', email: '', password: '' },
  })

  async function handleSubmit(values: SignUpFormValues) {
    await signUp.mutateAsync(values)
    await signIn.mutateAsync({ email: values.email, password: values.password })
    onSuccess()
  }

  const isPending = signUp.isPending || signIn.isPending
  const error = signUp.error ?? signIn.error

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
                <Input placeholder="yourname" {...field} />
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
          name="password"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Password</FormLabel>
              <FormControl>
                <Input type="password" placeholder="••••••••" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        {error && (
          <p className="text-sm text-destructive">{getApiErrorMessage(error)}</p>
        )}

        <Button type="submit" className="w-full" disabled={isPending}>
          {isPending ? 'Creating account…' : 'Create account'}
        </Button>

        <p className="text-center text-sm text-muted-foreground">
          Already have an account?{' '}
          <button
            type="button"
            onClick={onSwitchToSignIn}
            className="text-foreground underline underline-offset-4 hover:text-primary"
          >
            Sign in
          </button>
        </p>
      </form>
    </Form>
  )
}
