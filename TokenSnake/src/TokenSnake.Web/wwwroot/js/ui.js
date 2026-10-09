// Screens, HUD and the glue between auth.js and game.js.
//
// The HUD shows numbers from the token's CLAIMS, not from local variables: the signed
// token is the scoreboard. The only local state is what is needed to draw and to ask for the next token.

import { Game, bindControls } from './game.js';
import { initJwtPanel } from './jwt-panel.js';
import {
  login, completeLogin, isLoginCallback, signOut, onSignOut, start, eat, onToken, getToken, decodeClaims, isExpired, TokenError,
} from './auth.js';

const $ = (id) => document.getElementById(id);
const PELLETS_PER_LEVEL = 5; // display only; the server's LevelRules decides the level in the token
const SCREENS = ['screen-start', 'screen-game', 'screen-over'];

let game = null;
let session = null; // { gameId, pelletId, seq }: what the next eat request must say
let playing = false;
let pendingEat = null; // in-flight eat exchange; finishing the game waits for it

// --- screens -----------------------------------------------------------------------------

function show(screenId) {
  SCREENS.forEach((id) => { $(id).hidden = id !== screenId; });
}

function setMessage(text) {
  $('start-message').textContent = text;
}

/** Start screen: "sign in" when we have no token, "start" when we do. */
function showStart() {
  const token = getToken();
  const signedIn = Boolean(token) && !isExpired(token);
  $('login-btn').hidden = signedIn;
  $('play-btn').hidden = !signedIn;
  $('welcome').textContent = signedIn ? `Signed in as ${decodeClaims(token).nickname ?? 'player'}` : '';
  show('screen-start');
}

// --- HUD (claims only) -------------------------------------------------------------------

function renderHud(token) {
  const claims = decodeClaims(token);
  $('hud-name').textContent = claims.nickname ?? '';
  $('hud-score').textContent = claims.score ?? 0;
  $('hud-length').textContent = claims.length ?? 0;
  $('hud-level').textContent = claims.level ?? 1;
  const left = PELLETS_PER_LEVEL - ((claims.pellets ?? 0) % PELLETS_PER_LEVEL);
  $('hud-next').textContent = `${left} ${left === 1 ? 'pellet' : 'pellets'}`;
  $('user-name').textContent = claims.nickname ?? 'player';
  $('user-box').hidden = false;
}

/** Display-only countdown. It never calls the server: expiry is the end of the game. */
function watchExpiry() {
  const token = getToken();
  if (!playing || !token) return;

  const secondsLeft = decodeClaims(token).exp - Date.now() / 1000;
  $('hud-timer').textContent = `${Math.max(0, secondsLeft).toFixed(1)}s`;
  $('hud-timer').classList.toggle('danger', secondsLeft < 5);

  if (secondsLeft <= 0) return void gameOver('token_expired');
  requestAnimationFrame(watchExpiry);
}

// --- game flow ---------------------------------------------------------------------------

async function beginGame() {
  setMessage('');
  try {
    const response = await start();
    show('screen-game');
    launch(response);
  } catch (error) {
    // e.g. the lobby token expired while the player was reading
    setMessage('Could not start the game. Please sign in again.');
    $('login-btn').hidden = false;
    $('play-btn').hidden = true;
  }
}

function launch(response) {
  remember(response);
  game = new Game($('board'), response.rules, { onEat: handleEat, // Local death ends the UI only; the server is not told (see README, anti-cheat).
    onDeath: () => gameOver('collision') });
  game.setServerState(response);
  playing = true;
  game.run();
  watchExpiry();
}

const remember = (r) => { session = { gameId: r.game_id, pelletId: r.pellet_id, seq: r.seq }; };

/** The snake has already grown (optimistically). Now the server confirms or ends the game. */
function handleEat(moves) {
  pendingEat = exchangeEat(moves);
  return pendingEat;
}

async function exchangeEat(moves) {
  try {
    const response = await eat({ ...session, seq: session.seq + 1, moves });
    remember(response);
    game.setServerState(response);
  } catch (error) {
    gameOver(error instanceof TokenError ? error.code : 'network');
  } finally {
    pendingEat = null;
  }
}

const GAME_OVER_TEXT = {
  token_expired: 'Your token expired. Eat faster!',
  collision: 'You crashed. Mind the walls and your tail!',
};

async function gameOver(reason) {
  if (!playing) return;
  playing = false;
  game.stop();

  // An eat may still be in flight (expiry or a crash right after eating). Let it settle first so the
  // score shown and submitted come from the LATEST token. A failed eat leaves the previous token.
  // Never calls auth from a timer: this only waits for a request the player already made.
  if (pendingEat) await pendingEat.catch(() => {});

  $('over-title').textContent = reason === 'token_expired' ? 'Token expired' : 'Game over';
  $('over-message').textContent = GAME_OVER_TEXT[reason] ?? 'Nice try, the server disagreed.';
  const claims = decodeClaims(getToken());
  $('over-score').textContent = claims.score ?? 0;
  show('screen-over');
  submitScore();
}

// --- leaderboard (textContent only: nicknames are user input) ----------------------------

async function submitScore() {
  const status = $('submit-status');
  status.textContent = 'Submitting your score…';
  const token = getToken();
  try {
    const response = await fetch('/api/leaderboard', { method: 'POST', headers: { Authorization: `Bearer ${token}` } });
    status.textContent = response.ok ? 'Score submitted.' : 'Your token was too old to submit a score.';
  } catch {
    status.textContent = 'Could not submit your score.';
  }
  await loadLeaderboard();
}

async function loadLeaderboard() {
  const list = $('leaderboard');
  const entries = await (await fetch('/api/leaderboard')).json();
  const token = getToken();
  const myName = token && playing === false && !$('screen-over').hidden ? decodeClaims(token).nickname : null;
  const myScore = myName ? decodeClaims(token).score ?? 0 : null;
  let mine = false;
  list.replaceChildren(...entries.map((entry) => {
    const item = document.createElement('li');
    item.textContent = `${entry.nickname}: ${entry.score}`;
    // Highlight the player's own row (first match on nickname and score). Display only.
    if (!mine && entry.nickname === myName && entry.score === myScore) { item.className = 'mine'; mine = true; }
    return item;
  }));
}

// --- wiring ------------------------------------------------------------------------------

function playAgain() {
  // A token that is still valid can start another game; an expired one means signing in again.
  if (isExpired(getToken())) return login();
  show('screen-start');
  beginGame();
}

/** Forget the nickname: tokens go, IdentityServer's session ends, and the next sign-in asks for a nickname again. */
function changeNickname() {
  playing = false;
  game?.stop();
  game = null;
  session = null;
  $('user-box').hidden = true;
  return signOut();
}

async function main() {
  initJwtPanel($('jwt-panel'));
  onToken((token) => renderHud(token));
  bindControls(() => game, $('board'));
  $('login-btn').addEventListener('click', login);
  $('play-btn').addEventListener('click', beginGame);
  $('again-btn').addEventListener('click', playAgain);
  $('signout-btn').addEventListener('click', changeNickname);
  $('change-btn').addEventListener('click', changeNickname);
  onSignOut(() => { $('welcome').textContent = ''; });
  loadLeaderboard().catch(() => {});

  if (isLoginCallback()) {
    try {
      await completeLogin();
    } catch (error) {
      setMessage(`Sign-in failed: ${error.message}`);
    }
  }
  showStart();
}

main();
