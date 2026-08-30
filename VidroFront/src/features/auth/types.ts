export type SignUpRequest = {
  username: string
  email: string
  password: string
}

export type SignInRequest = {
  email: string
  password: string
}

export type AuthTokens = {
  accessToken: string
  refreshToken: string
  secondsToExpiration: number
}
