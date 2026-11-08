import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { useAuthModal } from '../hooks'
import { SignInForm } from './SignInForm'
import { SignUpForm } from './SignUpForm'

export function AuthModal() {
  const { isOpen, view, openSignIn, openSignUp, close } = useAuthModal()

  const title = view === 'signIn' ? 'Sign in to Vidro' : 'Create your account'
  const description =
    view === 'signIn'
      ? 'Upload videos, follow channels and comment.'
      : 'Takes a username, an email and a password.'

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>

        {view === 'signIn' ? (
          <SignInForm onSuccess={close} onSwitchToSignUp={openSignUp} />
        ) : (
          <SignUpForm onSuccess={close} onSwitchToSignIn={openSignIn} />
        )}
      </DialogContent>
    </Dialog>
  )
}
