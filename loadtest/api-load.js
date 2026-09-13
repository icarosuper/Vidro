import http from 'k6/http'
import { check, sleep } from 'k6'
import { Counter } from 'k6/metrics'

// One scenario file, three shapes of pressure on the API. Run it against the compose stack:
//   docker run --rm -i --network vidro_default -e BASE_URL=http://api:5000 grafana/k6 run - < api-load.js
// Read the result next to Grafana: the API exports its own meters at /metrics (design-decisions #12).

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000'
const JSON_HEADERS = { 'Content-Type': 'application/json' }

// A 429 is the point of the signInStorm scenario, so it gets counted instead of failed.
const rateLimited = new Counter('rate_limited_responses')

export const options = {
  scenarios: {
    // The read path a viewer actually produces: open a video, poll it, browse listings.
    browse: {
      executor: 'ramping-vus',
      exec: 'browse',
      startVUs: 1,
      stages: [
        { duration: '20s', target: 20 },
        { duration: '40s', target: 20 },
        { duration: '10s', target: 0 },
      ],
    },
    // The API's share of an upload: metadata row + presigned PUT URL. No video bytes — those
    // measure the worker and MinIO, which is P-PERF6's job, not this one's.
    createVideo: {
      executor: 'constant-arrival-rate',
      exec: 'createVideo',
      rate: 2,
      timeUnit: '1s',
      duration: '1m10s',
      preAllocatedVUs: 10,
    },
    // The rate limiter has never been under real pressure — only an integration test with a
    // budget of 3. This is the only scenario where non-200 is the expected answer.
    // Arrival rate, not VUs in a loop: a rejected request costs the API almost nothing, so three
    // VUs without a pause produced 750k rejections at 10k rps and drowned every other number in
    // the summary. 50/s is a brute-force attempt, and the budget is 10/min.
    signInStorm: {
      executor: 'constant-arrival-rate',
      exec: 'signInStorm',
      rate: 50,
      timeUnit: '1s',
      duration: '30s',
      startTime: '20s',
      preAllocatedVUs: 10,
    },
  },
  thresholds: {
    'http_req_failed{scenario:browse}': ['rate<0.01'],
    'http_req_failed{scenario:createVideo}': ['rate<0.01'],
    'http_req_duration{scenario:browse}': ['p(95)<800'],
    'http_req_duration{scenario:createVideo}': ['p(95)<1500'],
    // The limiter must answer 429, not 500, and not let everything through.
    rate_limited_responses: ['count>0'],
    checks: ['rate>0.99'],
  },
}

function unique() {
  return `${Date.now()}${__VU}${Math.floor(Math.random() * 1e6)}`
}

export function setup() {
  const suffix = unique()
  const user = {
    username: `load${suffix}`.slice(0, 20),
    email: `load${suffix}@example.test`,
    password: 'LoadTest123!',
  }

  const signUp = http.post(`${BASE_URL}/v1/auth/signup`, JSON.stringify(user), {
    headers: JSON_HEADERS,
  })
  check(signUp, { 'setup: signup created the user': (r) => r.status === 201 })

  const signIn = http.post(
    `${BASE_URL}/v1/auth/signin`,
    JSON.stringify({ email: user.email, password: user.password }),
    { headers: JSON_HEADERS },
  )
  check(signIn, { 'setup: signin returned a token': (r) => r.status === 200 })

  const accessToken = signIn.json('data.accessToken')
  const auth = { ...JSON_HEADERS, Authorization: `Bearer ${accessToken}` }

  const handle = `ch${suffix}`.slice(0, 20)
  const channel = http.post(
    `${BASE_URL}/v1/channels`,
    JSON.stringify({ handle, name: 'Load test channel' }),
    { headers: auth },
  )
  check(channel, { 'setup: channel created': (r) => r.status === 201 })

  const video = http.post(
    `${BASE_URL}/v1/users/${user.username}/channels/${handle}/videos`,
    JSON.stringify({ title: 'Load test video', tags: [], visibility: 0 }),
    { headers: auth },
  )
  check(video, { 'setup: video created': (r) => r.status === 201 })

  return {
    accessToken,
    username: user.username,
    email: user.email,
    handle,
    videoId: video.json('data.videoId'),
  }
}

function authHeaders(data) {
  return { ...JSON_HEADERS, Authorization: `Bearer ${data.accessToken}` }
}

export function browse(data) {
  const headers = authHeaders(data)

  // The upload page polls this exact call every 3s until the video leaves Processing.
  const video = http.get(`${BASE_URL}/v1/videos/${data.videoId}`, { headers })
  check(video, { 'GetVideo is 200': (r) => r.status === 200 })

  const trending = http.get(`${BASE_URL}/v1/videos/trending?limit=20`, {
    headers,
  })
  check(trending, { 'trending is 200': (r) => r.status === 200 })

  const feed = http.get(`${BASE_URL}/v1/feed?limit=20`, { headers })
  check(feed, { 'feed is 200': (r) => r.status === 200 })

  sleep(1)
}

export function createVideo(data) {
  const created = http.post(
    `${BASE_URL}/v1/users/${data.username}/channels/${data.handle}/videos`,
    JSON.stringify({ title: `Load ${unique()}`, tags: [], visibility: 0 }),
    { headers: authHeaders(data) },
  )
  check(created, { 'CreateVideo is 201': (r) => r.status === 201 })
}

export function signInStorm(data) {
  // Wrong password on purpose: this measures the limiter, not the password hash.
  const res = http.post(
    `${BASE_URL}/v1/auth/signin`,
    JSON.stringify({ email: data.email, password: 'wrong-password' }),
    {
      headers: JSON_HEADERS,
      responseCallback: http.expectedStatuses(401, 429),
      tags: { name: 'signin-rejected' },
    },
  )

  check(res, {
    'limiter answers 401 or 429, never 5xx': (r) =>
      r.status === 401 || r.status === 429,
  })

  if (res.status === 429) rateLimited.add(1)
}
