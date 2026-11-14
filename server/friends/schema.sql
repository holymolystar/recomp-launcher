-- Recomp Launcher friends server: the two tables (paste into the D1 database's Console and run it once)
CREATE TABLE IF NOT EXISTS users (
  id TEXT PRIMARY KEY,
  code TEXT UNIQUE NOT NULL,
  secret TEXT NOT NULL,
  name TEXT NOT NULL,
  look TEXT,
  playing TEXT,
  since INTEGER DEFAULT 0,
  seen INTEGER DEFAULT 0,
  share INTEGER DEFAULT 1,
  created INTEGER
);
CREATE TABLE IF NOT EXISTS friends (
  a TEXT NOT NULL,
  b TEXT NOT NULL,
  state TEXT NOT NULL,
  created INTEGER,
  PRIMARY KEY (a, b)
);
CREATE INDEX IF NOT EXISTS friends_b ON friends (b);
