// Request enums are typed as the exact ids (`0 | 1 | 2`), but a <Select> hands back a string.
// This turns that string back into one of the enum's ids, and fails loudly on anything else
// instead of sending the API a value it will reject.
export function toEnumMember<TEnum extends Record<string, number>>(
  enumObject: TEnum,
  raw: string | number,
): TEnum[keyof TEnum] {
  const id = Number(raw)
  const member = Object.values(enumObject).find((value) => value === id)
  if (member === undefined) {
    throw new Error(
      `${raw} is not one of ${Object.values(enumObject).join(', ')}`,
    )
  }
  return member as TEnum[keyof TEnum]
}
