# Friends server

The launcher's friend list (friend codes, requests, and what your friends are playing) goes through this small
server. It runs on Cloudflare's free plan, on your own account, so nobody else holds the data.

What it stores, per profile: a friend code, a name, the character, what is being played (only when "Show what I'm
playing" is on), and when the launcher was last seen. Nothing else.

## Setting it up (about 5 minutes, all in the browser)

1. Sign in at **dash.cloudflare.com** (a free account is enough).
2. **The database:** Storage & Databases → **D1 SQL Database** → **Create**. Name it `recomp-friends`.
   Open it, go to **Console**, paste everything from `schema.sql` and press **Execute**.
3. **The worker:** Compute (Workers) → **Workers & Pages** → **Create** → **Start with Hello World** → name it
   `recomp-friends` → **Deploy**.
4. Open the worker → **Edit code**. Replace everything in `worker.js` with this folder's `worker.js`, then
   **Deploy**.
5. **Connect them:** in the worker, **Bindings** → **Add binding** → **D1 database**. Variable name: `DB`
   (capitals), database: `recomp-friends` → **Add binding**. (It redeploys by itself.)
6. Copy the worker's address, like `https://recomp-friends.<your-name>.workers.dev`.
7. In the launcher: **Settings → Friends → Server**, paste the address, **Save**.

Opening the address in a browser shows `{"name":"Recomp Launcher friends","ok":true}` when it works.

## Your friends

Friends need the same server address in their launcher. When you build the installer, the address you saved is put
in it, so friends who install your build are connected straight away.

## Limits

The free plan allows 100,000 requests a day. Each running launcher sends about one a minute, so that's enough for
around 50 friends with their launchers on all day.
