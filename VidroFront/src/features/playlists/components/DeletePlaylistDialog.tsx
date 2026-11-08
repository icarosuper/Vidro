import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '#/components/ui/alert-dialog'

type Props = {
  // The playlist being confirmed; null keeps the dialog closed.
  playlistName: string | null
  onCancel: () => void
  onConfirm: () => void
}

// The one question asked before a playlist goes — from its own page and from the dashboard.
export function DeletePlaylistDialog({
  playlistName,
  onCancel,
  onConfirm,
}: Props) {
  const isOpen = playlistName !== null

  return (
    <AlertDialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) onCancel()
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete this playlist?</AlertDialogTitle>
          <AlertDialogDescription>
            “{playlistName}” will be deleted for good. The videos in it stay
            where they are.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={onConfirm}>
            Delete playlist
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
