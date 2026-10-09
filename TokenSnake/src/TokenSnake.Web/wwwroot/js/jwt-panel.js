// The JWT panel: shows the player's real access token, decoded, next to the game.
//
// It only DECODES. It does not verify the signature: the browser has no business trusting
// a token on its own, the servers do that. Everything derived from the token goes in through
// textContent, never innerHTML, so a hostile claim value (a nickname, say) cannot become markup.
// The countdown runs on requestAnimationFrame and only draws; it never talks to the server.

import { onToken, onSignOut, decodeJwtPart, decodeClaims } from './auth.js';

const MAX_TIMELINE = 10;
const URGENT_SECONDS = 5;

const CAPTION = 'In memory only \u00b7 can\'t be refreshed here \u00b7 decoded, not signature-verified.';
const BFF_URL = 'https://duendesoftware.com/products/capabilities/backend-for-frontend';
const TEACHING = [
  'The token lives only in this tab\'s memory and lasts seconds.',
  'This panel only decodes the token. It does not verify the signature; the servers do that.',
];
// The claims worth watching, in display order. Numeric ones show old -> new; the others just flag a change.
const WATCHED = ['score', 'length', 'level', 'pellets', 'path_hash', 'jti'];
const NUMERIC = new Set(['score', 'length', 'level', 'pellets']);

/** a91c3e...f02d style shortening for long opaque values. */
function shorten(value) {
  const text = String(value);
  return text.length > 12 ? `${text.slice(0, 4)}\u2026${text.slice(-4)}` : text;
}

/** Builds <tag class="cls">text</tag>. Text always goes in via textContent. */
function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

/** Splits a compact JWT into its three encoded parts plus the decoded header and payload. */
function decodeToken(token) {
  const [header, payload, signature = ''] = token.split('.');
  return { header, payload, signature, headerJson: decodeJwtPart(header), claims: decodeClaims(token) };
}

/** One pretty-printed object: one line per property, so single claims can be highlighted. */
function renderJson(object, changedKeys = new Set()) {
  const block = el('div', 'json');
  block.append(el('div', 'json-line', '{'));
  const entries = Object.entries(object);
  entries.forEach(([key, value], index) => {
    const comma = index < entries.length - 1 ? ',' : '';
    const line = el('div', 'json-line json-prop', `${JSON.stringify(key)}: ${JSON.stringify(value)}${comma}`);
    if (changedKeys.has(key)) line.classList.add('changed');
    block.append(line);
  });
  block.append(el('div', 'json-line', '}'));
  return block;
}

/** Claims whose value differs from the previous token (or that are new). */
function changedClaims(claims, previous) {
  if (!previous) return new Set();
  return new Set(Object.keys(claims).filter((key) => JSON.stringify(claims[key]) !== JSON.stringify(previous[key])));
}

export function initJwtPanel(panel) {
  const timeline = []; // [{ jti, score }], newest last
  let current = null;  // { exp, lifetime } of the token being counted down
  let fullToken = '';

  // --- static skeleton: built once, filled on every token ---------------------------------
  const empty = el('p', 'jwt-empty', 'Waiting for a token. Sign in and your access token will appear here.');
  const body = el('div', 'jwt-body');
  body.hidden = true;

  const learn = el('details', 'jwt-learn');
  const bffNote = el('p', 'jwt-bff');
  bffNote.append(el('strong', undefined, 'This is your real access token.'),
    ' Anyone who copies it gets a few seconds of your game. Real apps keep tokens out of the browser with ');
  const bffLink = el('a', undefined, 'Duende Backend For Frontend (BFF)');
  bffLink.href = BFF_URL;
  bffLink.target = '_blank';
  bffLink.rel = 'noopener';
  bffNote.append(bffLink, '.');
  learn.append(el('summary', undefined, 'What am I looking at?'),
    ...TEACHING.map((t) => el('p', 'jwt-copy', t)));

  const claimRows = el('tbody');
  const claimTable = el('table', 'jwt-claims');
  claimTable.append(
    (() => { const c = el('caption', 'jwt-sr-only', 'Claims in the current access token'); return c; })(),
    (() => {
      const head = el('thead'); const tr = el('tr');
      ['Claim', 'Current', 'Change'].forEach((h) => { const th = el('th', undefined, h); th.scope = 'col'; tr.append(th); });
      head.append(tr); return head;
    })(),
    claimRows,
  );

  const countdownText = el('span', 'jwt-countdown-text');
  const bar = el('div', 'jwt-bar');
  const barFill = el('div', 'jwt-bar-fill');
  bar.append(barFill);
  const expiry = el('div', 'jwt-expiry');
  expiry.append(el('span', 'jwt-label', 'exp'), bar, countdownText);

  const timelineList = el('ol', 'jwt-timeline');
  const history = el('div', 'jwt-history');
  history.append(el('span', 'jwt-label', 'Last 10 tokens'), timelineList);

  const encoded = el('p', 'jwt-encoded');
  const copyBtn = el('button', 'jwt-copy-btn', 'Copy token');
  copyBtn.type = 'button';
  const live = el('span', 'jwt-sr-only jwt-live');
  live.setAttribute('role', 'status');
  live.setAttribute('aria-live', 'polite');
  const headerJson = el('div', 'jwt-scroll');
  const payloadJson = el('div', 'jwt-scroll');

  function section(title, open, ...children) {
    const d = el('details', 'jwt-more');
    d.open = open;
    const inner = el('div', 'jwt-more-body');
    inner.append(...children);
    d.append(el('summary', undefined, title), inner);
    return d;
  }

  const head = el('div', 'jwt-head');
  head.append(el('h2', 'jwt-title', 'JWT'), el('span', 'jwt-sub', 'updated on pellet'));

  body.append(
    bffNote, el('p', 'jwt-caption', CAPTION), learn, claimTable, expiry, history,
    section('Encoded token', true, copyBtn, live, encoded),
    section('Header JSON', false, headerJson),
    section('Full payload JSON', false, payloadJson),
  );
  panel.replaceChildren(head, empty, body);

  copyBtn.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(fullToken);
      live.textContent = 'Copied';
    } catch {
      live.textContent = 'Copy failed';
    }
    copyBtn.textContent = live.textContent;
  });
  copyBtn.addEventListener('blur', () => { copyBtn.textContent = 'Copy token'; });

  // --- rendering ------------------------------------------------------------------------

  function claimRow(key, value, previous) {
    const had = previous && Object.prototype.hasOwnProperty.call(previous, key);
    const isChanged = previous && JSON.stringify(value) !== JSON.stringify(previous[key]);
    let change = '';
    if (isChanged) {
      if (NUMERIC.has(key) && had) change = `${previous[key]} \u2192 ${value}`;
      else change = key === 'jti' ? 'new' : 'changed';
    }
    const tr = el('tr', isChanged ? 'changed' : undefined);
    const th = el('th', 'jwt-name', key); th.scope = 'row';
    const val = el('td', 'jwt-value', key === 'path_hash' || key === 'jti' ? shorten(value) : String(value));
    if (key === 'path_hash' || key === 'jti') val.title = String(value);
    tr.append(th, val, el('td', 'jwt-change', change));
    return tr;
  }

  function render(token, previousToken) {
    const { header, payload, signature, headerJson: hj, claims } = decodeToken(token);
    const previous = previousToken ? decodeToken(previousToken).claims : null;
    const changed = changedClaims(claims, previous);
    fullToken = token;

    empty.hidden = true;
    body.hidden = false;

    claimRows.replaceChildren(...WATCHED.filter((k) => k in claims).map((k) => claimRow(k, claims[k], previous)));

    encoded.replaceChildren(
      el('span', 'jwt-header', header), '.', el('span', 'jwt-payload', payload), '.', el('span', 'jwt-signature', signature),
    );
    headerJson.replaceChildren(renderJson(hj));
    payloadJson.replaceChildren(renderJson(claims, changed));

    timeline.push({ jti: claims.jti ?? '?', score: claims.score ?? 0 });
    timeline.splice(0, timeline.length - MAX_TIMELINE);
    timelineList.replaceChildren(...timeline.map((entry, i) => {
      const li = el('li', i === timeline.length - 1 ? 'newest' : undefined);
      li.tabIndex = 0;
      const text = `score ${entry.score}, jti ${entry.jti}`;
      li.title = text;
      li.setAttribute('aria-label', text);
      return li;
    }));

    const lifetime = claims.exp - (claims.iat ?? claims.nbf ?? claims.exp - 1);
    const wasIdle = current === null;
    current = { exp: claims.exp, lifetime: Math.max(lifetime, 1) };
    if (wasIdle) requestAnimationFrame(drawCountdown);
  }

  /** Display only. Stops itself at expiry; the next token starts it again. */
  function drawCountdown() {
    if (!current) return;
    const secondsLeft = Math.max(0, current.exp - Date.now() / 1000);
    const urgent = secondsLeft < URGENT_SECONDS;
    barFill.style.width = `${Math.min(100, (secondsLeft / current.lifetime) * 100)}%`;
    barFill.classList.toggle('urgent', urgent);
    countdownText.classList.toggle('urgent', urgent);
    countdownText.textContent = secondsLeft > 0 ? `${secondsLeft.toFixed(1)}s left` : 'expired';

    if (secondsLeft > 0) requestAnimationFrame(drawCountdown);
    else current = null;
  }

  onToken(render);

  // Signing out empties the panel: the token is gone from memory, so there is nothing left to show.
  onSignOut(() => {
    current = null;
    fullToken = '';
    timeline.length = 0;
    body.hidden = true;
    empty.hidden = false;
  });
}
