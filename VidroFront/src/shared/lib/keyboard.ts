// Page-wide shortcuts ("/" for search, "t" for theater) must not fire while the user types.
export function isTypingInField(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const isFormField = ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)
  return isFormField || target.isContentEditable
}
