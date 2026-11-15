// Recomp Launcher friends server: a Cloudflare Worker with a D1 database (both free). Each launcher profile gets
// a friend code (RL-XXXX-XXXX); friends add each other by code, and the launcher tells the server what it is playing
// once a minute while it runs. Nothing else is stored: a name, a character (the launcher's own character string),
// what is being played (only if the player shares it), and when they were last seen.
//
// Every request is a POST with a JSON body. Except /register, each one carries "Authorization: Bearer <id>:<secret>".
//   /register {name, look}           -> {id, code, secret}     a new player (the secret is only ever shown here)
//   /me {name, look, playing, share} -> {ok}                    I'm here; what I'm playing (null: nothing)
//   /list {}                         -> {me, friends, incoming, outgoing}
//   /add {code}                      -> {ok, state}             a friend request (or friends, when they asked too)
//   /accept {id}  /remove {id}       -> {ok}                    accept a request; decline, cancel or unfriend
//   /forget {}                       -> {ok}                    removes this player and their friendships
// See README.md for setting it up.

const ONLINE_MS = 3 * 60 * 1000;       // seen in the last three minutes: online
const NAME_MAX = 40, LOOK_MAX = 2000, GAME_MAX = 80;
const CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O or 1/I

export default {
  async fetch(req, env) {
    try {
      if (req.method !== "POST") return json({ name: "Recomp Launcher friends", ok: true });
      const path = new URL(req.url).pathname.replace(/\/+$/, "");
      const body = await req.json().catch(() => ({}));
      if (path === "/register") return register(env, body);
      const me = await auth(req, env);
      if (!me) return json({ error: "unknown player" }, 401);
      switch (path) {
        case "/me": return presence(env, me, body);
        case "/list": return list(env, me);
        case "/add": return add(env, me, body);
        case "/accept": return accept(env, me, body);
        case "/remove": return remove(env, me, body);
        case "/forget": return forget(env, me);
      }
      return json({ error: "not found" }, 404);
    } catch (e) {
      return json({ error: "server error" }, 500);
    }
  },
};

function json(o, status = 200) { return new Response(JSON.stringify(o), { status, headers: { "content-type": "application/json" } }); }
function text(v, max) { return typeof v === "string" ? v.trim().slice(0, max) : ""; }
function random(n, chars) { const b = crypto.getRandomValues(new Uint8Array(n)); return Array.from(b, x => chars[x % chars.length]).join(""); }
async function sha256(s) { const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s)); return Array.from(new Uint8Array(d), b => b.toString(16).padStart(2, "0")).join(""); }

async function register(env, body) {
  const id = random(16, "abcdefghijklmnopqrstuvwxyz0123456789");
  const secret = random(40, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
  let code;
  for (let i = 0; i < 5; i++) {
    code = "RL-" + random(4, CODE_CHARS) + "-" + random(4, CODE_CHARS);
    if (!(await env.DB.prepare("SELECT 1 FROM users WHERE code = ?").bind(code).first())) break;
  }
  const now = Date.now();
  await env.DB.prepare("INSERT INTO users (id, code, secret, name, look, playing, since, seen, share, created) VALUES (?, ?, ?, ?, ?, NULL, 0, ?, 1, ?)")
    .bind(id, code, await sha256(secret), text(body.name, NAME_MAX) || "Player", text(body.look, LOOK_MAX), now, now).run();
  return json({ id, code, secret });
}

async function auth(req, env) {
  const m = /^Bearer ([a-z0-9]{16}):([A-Za-z0-9]{40})$/.exec(req.headers.get("authorization") || "");
  if (!m) return null;
  const u = await env.DB.prepare("SELECT * FROM users WHERE id = ?").bind(m[1]).first();
  return u && u.secret === (await sha256(m[2])) ? u : null;
}

async function presence(env, me, body) {
  const playing = body.share === false ? null : text(body.playing, GAME_MAX) || null;
  const since = playing && playing === me.playing ? me.since : (playing ? Date.now() : 0);
  await env.DB.prepare("UPDATE users SET name = ?, look = ?, playing = ?, since = ?, seen = ?, share = ? WHERE id = ?")
    .bind(text(body.name, NAME_MAX) || me.name, text(body.look, LOOK_MAX) || me.look, playing, since, Date.now(), body.share === false ? 0 : 1, me.id).run();
  return json({ ok: true });
}

function card(u, now) {
  const online = now - u.seen < ONLINE_MS;
  return { id: u.id, code: u.code, name: u.name, look: u.look, online, playing: online && u.share ? u.playing : null, since: online && u.share ? u.since : 0, seen: u.seen };
}

async function list(env, me) {
  const now = Date.now();
  const rows = (sql) => env.DB.prepare(sql).bind(me.id).all().then(r => r.results || []);
  const friends = await rows("SELECT u.* FROM friends f JOIN users u ON u.id = f.b WHERE f.a = ? AND f.state = 'friends'");
  const incoming = await rows("SELECT u.* FROM friends f JOIN users u ON u.id = f.a WHERE f.b = ? AND f.state = 'pending'");
  const outgoing = await rows("SELECT u.* FROM friends f JOIN users u ON u.id = f.b WHERE f.a = ? AND f.state = 'pending'");
  return json({
    me: { code: me.code, name: me.name },
    friends: friends.map(u => card(u, now)),
    incoming: incoming.map(u => ({ id: u.id, code: u.code, name: u.name, look: u.look })),
    outgoing: outgoing.map(u => ({ id: u.id, code: u.code, name: u.name, look: u.look })),
  });
}

async function add(env, me, body) {
  const code = text(body.code, 20).toUpperCase().replace(/[^A-Z0-9]/g, "");
  const full = code.length === 10 && code.startsWith("RL") ? "RL-" + code.slice(2, 6) + "-" + code.slice(6) : null;
  const them = full ? await env.DB.prepare("SELECT * FROM users WHERE code = ?").bind(full).first() : null;
  if (!them) return json({ error: "No one has that code." }, 404);
  if (them.id === me.id) return json({ error: "That's your own code." }, 400);
  const theirs = await env.DB.prepare("SELECT state FROM friends WHERE a = ? AND b = ?").bind(them.id, me.id).first();
  const now = Date.now();
  if (theirs) {
    // they asked first (or you're friends already): friends both ways
    await env.DB.batch([
      env.DB.prepare("INSERT OR REPLACE INTO friends (a, b, state, created) VALUES (?, ?, 'friends', ?)").bind(me.id, them.id, now),
      env.DB.prepare("UPDATE friends SET state = 'friends' WHERE a = ? AND b = ?").bind(them.id, me.id),
    ]);
    return json({ ok: true, state: "friends", name: them.name });
  }
  await env.DB.prepare("INSERT OR IGNORE INTO friends (a, b, state, created) VALUES (?, ?, 'pending', ?)").bind(me.id, them.id, now).run();
  return json({ ok: true, state: "pending", name: them.name });
}

async function accept(env, me, body) {
  const id = text(body.id, 16);
  const req = await env.DB.prepare("SELECT 1 FROM friends WHERE a = ? AND b = ? AND state = 'pending'").bind(id, me.id).first();
  if (!req) return json({ error: "No such request." }, 404);
  const now = Date.now();
  await env.DB.batch([
    env.DB.prepare("UPDATE friends SET state = 'friends' WHERE a = ? AND b = ?").bind(id, me.id),
    env.DB.prepare("INSERT OR REPLACE INTO friends (a, b, state, created) VALUES (?, ?, 'friends', ?)").bind(me.id, id, now),
  ]);
  return json({ ok: true });
}

async function remove(env, me, body) {
  const id = text(body.id, 16);
  await env.DB.prepare("DELETE FROM friends WHERE (a = ? AND b = ?) OR (a = ? AND b = ?)").bind(me.id, id, id, me.id).run();
  return json({ ok: true });
}

async function forget(env, me) {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM friends WHERE a = ? OR b = ?").bind(me.id, me.id),
    env.DB.prepare("DELETE FROM users WHERE id = ?").bind(me.id),
  ]);
  return json({ ok: true });
}
