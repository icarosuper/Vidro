import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { useAuthModal } from '../hooks'
import { SignInForm } from './SignInForm'
import { SignUpForm } from './SignUpForm'

export function AuthModal() {
  const { isOpen, view, openSignIn, openSignUp, close } = useAuthModal()

  const title = view === 'signIn'
    ? 'Sign in to Vidro'
    : 'Create your account'

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        {view === 'signIn'
          ? (
            <SignInForm
              onSuccess={close}
              onSwitchToSignUp={openSignUp}
            />
          )
          : (
            <SignUpForm
              onSuccess={close}
              onSwitchToSignIn={openSignIn}
            />
          )}
      </DialogContent>
    </Dialog>
  )
}
