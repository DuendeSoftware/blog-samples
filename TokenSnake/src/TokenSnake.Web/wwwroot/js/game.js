// The Snake itself: a grid, a move buffer, and a local mirror of the server's path replay.
//
// The server decides everything that counts. This file predicts the same rules locally
// (walls kill, no reversing, tail moves before the self-check, growth on the pellet)
// so the game feels instant. Every applied step is also recorded as U/D/L/R; that
// string is the "proof" sent to the server with each eat.

const VECTORS = {
  up: { x: 0, y: -1, letter: 'U' },
  down: { x: 0, y: 1, letter: 'D' },
  left: { x: -1, y: 0, letter: 'L' },
  right: { x: 1, y: 0, letter: 'R' },
};
const OPPOSITE = { up: 'down', down: 'up', left: 'right', right: 'left' };

const same = (a, b) => a.x === b.x && a.y === b.y;

/** Mirrors PathReplayEngine for one step. Mutates the snake; returns 'ok' | 'eat' | 'wall' | 'self'. */
export function stepSnake(snake, direction, pellet, rules) {
  const v = VECTORS[direction];
  const head = { x: snake.body[0].x + v.x, y: snake.body[0].y + v.y };
  if (head.x < 0 || head.y < 0 || head.x >= rules.width || head.y >= rules.height) return 'wall';

  const eats = same(head, pellet);
  // The tail moves first unless we grow, so chasing your own tail is legal (as on the server).
  const remaining = eats ? snake.body : snake.body.slice(0, -1);
  if (remaining.some((cell) => same(cell, head))) return 'self';

  snake.body = [head, ...remaining];
  snake.direction = direction;
  return eats ? 'eat' : 'ok';
}

/** The start position comes from the server's rules, never from constants here. */
export function createSnake(rules) {
  const direction = rules.start_direction;
  const back = VECTORS[OPPOSITE[direction]];
  const body = Array.from({ length: rules.start_length }, (_, i) => ({
    x: rules.start_x + back.x * i,
    y: rules.start_y + back.y * i,
  }));
  return { body, direction };
}

export class Game {
  /**
   * @param canvas the board
   * @param rules the `rules` object from the start response
   * @param handlers { onEat(moves): called once per pellet with the move string, onDeath(): local collision }
   */
  constructor(canvas, rules, handlers) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.rules = rules;
    this.handlers = handlers;
    this.cell = 24;
    canvas.width = rules.width * this.cell;
    canvas.height = rules.height * this.cell;

    this.snake = createSnake(rules);
    this.pellet = null;
    this.tickMs = 200;
    this.moves = [];   // letters applied since the last eat
    this.queue = [];   // buffered turns
    this.running = false;
    this.waiting = false; // true while the server is answering an eat
    this.elapsed = 0;
    this.lastFrame = 0;
  }

  /** The server gives us the pellet and the speed; we never invent them. */
  setServerState({ next_pellet: pellet, tick_ms: tickMs }) {
    this.pellet = pellet;
    this.tickMs = tickMs;
    this.waiting = false;
  }

  run() {
    this.running = true;
    this.lastFrame = performance.now();
    requestAnimationFrame((t) => this.frame(t));
  }

  stop() {
    this.running = false;
    this.draw();
  }

  /** Queue a turn. Same-direction and reversing inputs are ignored (the server would reject a reversal). */
  turn(direction) {
    const last = this.queue.at(-1) ?? this.snake.direction;
    if (direction === last || direction === OPPOSITE[last] || this.queue.length >= 2) return;
    this.queue.push(direction);
  }

  frame(now) {
    if (!this.running) return;
    this.elapsed += now - this.lastFrame;
    this.lastFrame = now;
    if (this.elapsed >= this.tickMs && !this.waiting && this.pellet) {
      this.elapsed %= this.tickMs;
      this.tick();
    }
    this.draw();
    requestAnimationFrame((t) => this.frame(t));
  }

  tick() {
    const direction = this.queue.shift() ?? this.snake.direction;
    const outcome = stepSnake(this.snake, direction, this.pellet, this.rules);
    if (outcome === 'wall' || outcome === 'self') {
      // Local death only ends the UI. There is deliberately no "die" request: a client that never
      // reports its death gains nothing (every eat must be a legal path from the server's own
      // snake, and the token expires), so the server game simply expires.
      this.stop();
      this.handlers.onDeath(outcome);
      return;
    }
    this.moves.push(VECTORS[direction].letter);
    if (outcome === 'eat') this.sendEat();
  }

  /** Optimistic growth already happened in stepSnake; now freeze until the server answers. */
  sendEat() {
    this.waiting = true;
    const proof = this.moves.join('');
    this.moves = [];
    this.handlers.onEat(proof);
  }

  draw() {
    const colors = getComputedStyle(this.canvas);
    const color = (name, fallback) => colors.getPropertyValue(name).trim() || fallback;
    const { ctx, cell } = this;

    ctx.fillStyle = color('--board', '#1a1a17');
    ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);

    // Only the pellet the server gave us is ever drawn.
    if (this.pellet) this.fillCell(this.pellet, color('--pellet', '#74acfb'));
    this.snake.body.forEach((part, i) =>
      this.fillCell(part, i === 0 ? color('--snake-head', '#81ff8c') : color('--snake', '#61fb92')));
  }

  fillCell(cell, fill) {
    const size = this.cell;
    this.ctx.fillStyle = fill;
    this.ctx.fillRect(cell.x * size + 1, cell.y * size + 1, size - 2, size - 2);
  }
}

const KEYS = {
  ArrowUp: 'up', w: 'up', ArrowDown: 'down', s: 'down',
  ArrowLeft: 'left', a: 'left', ArrowRight: 'right', d: 'right',
};

/** Keyboard and swipe controls. `getGame` returns the current Game (or null), so we bind only once. */
export function bindControls(getGame, canvas) {
  const turn = (direction) => getGame()?.turn(direction);
  window.addEventListener('keydown', (event) => {
    const direction = KEYS[event.key];
    if (!direction) return;
    event.preventDefault(); // arrows would otherwise scroll the page
    turn(direction);
  });

  let origin = null;
  canvas.addEventListener('touchstart', (e) => { origin = e.touches[0]; }, { passive: true });
  canvas.addEventListener('touchend', (e) => {
    if (!origin) return;
    const dx = e.changedTouches[0].clientX - origin.clientX;
    const dy = e.changedTouches[0].clientY - origin.clientY;
    origin = null;
    if (Math.max(Math.abs(dx), Math.abs(dy)) < 24) return; // a tap, not a swipe
    turn(Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up'));
  });
}
