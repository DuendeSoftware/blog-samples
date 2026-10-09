// Public-client auth for TokenSnake: authorization code + PKCE (S256), hand-rolled.
//
// Rules this file lives by:
//  * Tokens exist ONLY in the module-scope variable below. Never in web storage, never in a URL.
//  * sessionStorage holds the PKCE verifier and state only across the login redirect,
//    and both are deleted before the code exchange even starts.
//  * The id token (only needed as id_token_hint when signing out) is kept the same way: module memory only.
//  * There is no refresh token and no timer in this file. A new token only ever comes
//    from login() or from eating a pellet (see eat() below). When the token expires, the game ends.

const EAT_GRANT = 'urn:snake:eat';
const VERIFIER_KEY = 'pkce_verifier';
const STATE_KEY = 'pkce_state';

let config = null;
let accessToken = null; // the only place a token is kept
let idToken = null;     // only sent back to IdentityServer on sign-out
const listeners = [];
const signOutListeners = [];

// --- token hook --------------------------------------------------------------------------

/** Subscribe to every new token: listener(token, previousToken). Used by the JWT panel. */
export function onToken(listener) {
  listeners.push(listener);
}

/** Subscribe to sign-out: listener() runs once the in-memory tokens are gone. Used by the UI and the JWT panel. */
export function onSignOut(listener) {
  signOutListeners.push(listener);
}

function setToken(token) {
  const previous = accessToken;
  accessToken = token;
  listeners.forEach((listener) => listener(token, previous));
}

export const getToken = () => accessToken;

/** Decodes one base64url JWT part (header or payload) to an object. Unverified. */
export function decodeJwtPart(part) {
  const bytes = Uint8Array.from(atob(base64UrlToBase64(part)), (c) => c.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes));
}

/** Reads the (unverified) payload. The server verifies; the browser only displays. */
export const decodeClaims = (token) => decodeJwtPart(token.split('.')[1]);

export function isExpired(token) {
  return !token || decodeClaims(token).exp * 1000 <= Date.now();
}

// --- PKCE helpers ------------------------------------------------------------------------

function base64UrlToBase64(text) {
  return text.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(text.length / 4) * 4, '=');
}

function base64UrlEncode(bytes) {
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function randomString(byteCount = 32) {
  return base64UrlEncode(crypto.getRandomValues(new Uint8Array(byteCount)));
}

async function challengeFor(verifier) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  return base64UrlEncode(new Uint8Array(digest));
}

async function loadConfig() {
  config ??= await (await fetch('/config.json')).json();
  return config;
}

// --- login -------------------------------------------------------------------------------

/** Step 1: send the browser to IdentityServer, where the player picks a nickname. */
export async function login() {
  const cfg = await loadConfig();
  const verifier = randomString(64);
  const state = randomString(16);
  sessionStorage.setItem(VERIFIER_KEY, verifier);
  sessionStorage.setItem(STATE_KEY, state);

  const query = new URLSearchParams({
    client_id: cfg.client_id,
    redirect_uri: cfg.redirect_uri,
    response_type: 'code',
    scope: cfg.scope,
    state,
    code_challenge: await challengeFor(verifier),
    code_challenge_method: 'S256',
  });
  location.assign(`${cfg.authorization_endpoint}?${query}`);
}

/** True when this page load is the redirect back from IdentityServer. */
export const isLoginCallback = () => /[?&](code|error)=/.test(location.search);

/** Step 2: check state, swap the code for the first token, then clean the URL. */
export async function completeLogin() {
  const cfg = await loadConfig();
  const params = new URLSearchParams(location.search);
  const expectedState = sessionStorage.getItem(STATE_KEY);
  const verifier = sessionStorage.getItem(VERIFIER_KEY);

  // One-shot secrets: remove them first, so no failure path can leave them behind.
  sessionStorage.removeItem(STATE_KEY);
  sessionStorage.removeItem(VERIFIER_KEY);
  history.replaceState(null, '', '/'); // the code in the address bar is single-use; hide it

  if (params.get('error')) throw new Error(params.get('error_description') || params.get('error'));
  if (!expectedState || params.get('state') !== expectedState || !verifier) throw new Error('state_mismatch');

  const response = await postToken({
    grant_type: 'authorization_code',
    code: params.get('code'),
    redirect_uri: cfg.redirect_uri,
    client_id: cfg.client_id,
    code_verifier: verifier,
  });
  idToken = response.id_token ?? null;
  setToken(response.access_token);
}

// --- sign out ----------------------------------------------------------------------------

/**
 * Drops the tokens, then asks IdentityServer to end its session (the cookie that remembers the nickname).
 * IdentityServer sends the browser back to post_logout_redirect_uri, i.e. the start screen.
 * The id token goes in the URL as id_token_hint: it is only a hint, it is not an access token.
 */
export async function signOut() {
  const cfg = await loadConfig();
  const hint = idToken;
  accessToken = null;
  idToken = null;
  signOutListeners.forEach((listener) => listener());

  const query = new URLSearchParams({ post_logout_redirect_uri: cfg.post_logout_redirect_uri });
  if (hint) query.set('id_token_hint', hint);
  location.assign(`${cfg.end_session_endpoint}?${query}`);
}

// --- the game's token calls --------------------------------------------------------------

/** Begins a game. The current token goes in, a game token (and the board rules) come out. */
export async function start() {
  return exchange({ action: 'start' });
}

/** Proves the path to the pellet. Returns the next pellet, level and tick; the new token is stored. */
export async function eat({ gameId, pelletId, seq, moves }) {
  return exchange({ action: 'eat', game_id: gameId, pellet_id: pelletId, seq, moves });
}

async function exchange(fields) {
  const cfg = await loadConfig();
  const response = await postToken({
    grant_type: EAT_GRANT,
    client_id: cfg.client_id,
    subject_token: accessToken,
    ...fields,
  });
  setToken(response.access_token);
  return response;
}

/** Thrown for OAuth errors; `code` is the error_description, e.g. "token_expired". */
export class TokenError extends Error {
  constructor(error, description) {
    super(description || error);
    this.error = error;
    this.code = description || error;
  }
}

async function postToken(fields) {
  const cfg = await loadConfig();
  const response = await fetch(cfg.token_endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams(Object.entries(fields).map(([k, v]) => [k, String(v)])),
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new TokenError(body.error ?? 'request_failed', body.error_description);
  return body;
}
