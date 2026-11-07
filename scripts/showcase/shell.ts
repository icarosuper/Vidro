// Runs a command with inherited output and fails loudly on a non-zero exit.
export async function run(command: string[]) {
  const child = Bun.spawn(command, { stdout: 'inherit', stderr: 'inherit' })
  const exitCode = await child.exited
  const failed = exitCode !== 0
  if (failed) {
    throw new Error(`${command[0]} exited with ${exitCode}: ${command.join(' ')}`)
  }
}
