"""A pretend TV provider, for trying Recomp TV in a PC browser without a real login.

    python samsung-tv/dev/mock_provider.py      then open http://localhost:8765/

It serves the app itself (samsung-tv/app) and an Xtream-style API on the same address. Log in with the server
http://localhost:8765, username demo and password demo (or the playlist link http://localhost:8765/get.php?username=demo&password=demo).
The channels play public test streams; channel 7 is broken on purpose (to see what a failing channel looks like).
"""
import base64, json, os, random, sys, time
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

APP = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'app')
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
STREAMS = [
    'https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8',
    'https://devstreaming-cdn.apple.com/videos/streaming/examples/bipbop_16x9/bipbop_16x9_variant.m3u8',
    'https://demo.unified-streaming.com/k8s/features/stable/video/tears-of-steel/tears-of-steel.ism/.m3u8',
]
CATS = ['UK | Entertainment', 'UK | News', 'Sports', 'Movies 24/7', 'Kids', 'NL | Algemeen', 'PL | Polska']
SHOWS = ['Morning Live', 'The News at Six', 'Football Tonight', 'Big Film: The Long Road', 'Cartoon Hour', 'Top 40', 'Wild Planet', 'Quiz Night', 'Weather', 'Late Show']
random.seed(4)
CHANNELS = []
for i in range(1, 241):
    c = (i - 1) % len(CATS)
    CHANNELS.append({'num': i, 'name': '%s %d%s' % (['One', 'News', 'Sport', 'Cinema', 'Kids', 'Hits', 'Planet'][c], i, ' HD' if i % 3 == 0 else ''),
                     'stream_id': 1000 + i, 'stream_icon': '' if i % 5 == 0 else 'http://localhost:%d/logo/%d.svg' % (PORT, i),
                     'category_id': str(c + 1), 'epg_channel_id': 'ch%d' % i, 'tv_archive': 1 if i % 4 == 1 else 0, 'tv_archive_duration': 3 if i % 4 == 1 else 0})


# films and series with real titles (so Cinemeta finds their genres, pictures and trailers); posters by IMDb id
FILMS = [('The Matrix', 1999, 'tt0133093'), ('Inception', 2010, 'tt1375666'), ('Interstellar', 2014, 'tt0816692'), ('The Dark Knight', 2008, 'tt0468569'),
         ('Pulp Fiction', 1994, 'tt0110912'), ('Fight Club', 1999, 'tt0137523'), ('Forrest Gump', 1994, 'tt0109830'), ('Gladiator', 2000, 'tt0172495'),
         ('The Godfather', 1972, 'tt0068646'), ('Jurassic Park', 1993, 'tt0107290'), ('Titanic', 1997, 'tt0120338'), ('Avatar', 2009, 'tt0499549'),
         ('The Lion King', 1994, 'tt0110357'), ('Toy Story', 1995, 'tt0114709'), ('Shrek', 2001, 'tt0126029'), ('Finding Nemo', 2003, 'tt0266543'),
         ('Up', 2009, 'tt1049413'), ('Coco', 2017, 'tt2380307'), ('Frozen', 2013, 'tt2294629'), ('Joker', 2019, 'tt7286456'), ('Parasite', 2019, 'tt6751668'),
         ('Dune', 2021, 'tt1160419'), ('Oppenheimer', 2023, 'tt15398776'), ('Barbie', 2023, 'tt1517268'), ('Top Gun: Maverick', 2022, 'tt1745960'),
         ('Mad Max: Fury Road', 2015, 'tt1392190'), ('John Wick', 2014, 'tt2911666'), ('Alien', 1979, 'tt0078748'), ('Aliens', 1986, 'tt0090605'),
         ('The Shining', 1980, 'tt0081505'), ('Get Out', 2017, 'tt5052448'), ('Hereditary', 2018, 'tt7784604'), ('It', 2017, 'tt1396484'),
         ('A Quiet Place', 2018, 'tt6644200'), ('The Conjuring', 2013, 'tt1457767'), ('La La Land', 2016, 'tt3783958'), ('The Notebook', 2004, 'tt0332280'),
         ('Superbad', 2007, 'tt0829482'), ('The Hangover', 2009, 'tt1119646'), ('Step Brothers', 2008, 'tt0838283'), ('Anchorman: The Legend of Ron Burgundy', 2004, 'tt0357413'),
         ('Knives Out', 2019, 'tt8946378'), ('Se7en', 1995, 'tt0114369'), ('Zodiac', 2007, 'tt0443706'), ('Gone Girl', 2014, 'tt2267998'),
         ('The Departed', 2006, 'tt0407887'), ('Heat', 1995, 'tt0113277'), ('Goodfellas', 1990, 'tt0099685'), ('Saving Private Ryan', 1998, 'tt0120815'),
         ('1917', 2019, 'tt8579674'), ('Dunkirk', 2017, 'tt5013056')]
SERIES = [('Breaking Bad', 'tt0903747', 'Crime, Drama, Thriller'), ('Game of Thrones', 'tt0944947', 'Action & Adventure, Drama, Sci-Fi & Fantasy'),
          ('Stranger Things', 'tt4574334', 'Drama, Sci-Fi & Fantasy, Mystery'), ('The Office', 'tt0386676', 'Comedy'), ('Friends', 'tt0108778', 'Comedy'),
          ('The Crown', 'tt4786824', 'Drama'), ('Dark', 'tt5753856', 'Crime, Drama, Sci-Fi & Fantasy, Mystery'), ('The Mandalorian', 'tt8111088', 'Sci-Fi & Fantasy, Action & Adventure'),
          ('The Last of Us', 'tt3581920', 'Drama, Sci-Fi & Fantasy, Action & Adventure'), ('Succession', 'tt7660850', 'Drama'), ('The Bear', 'tt14452776', 'Comedy, Drama'),
          ('Severance', 'tt11280740', 'Drama, Mystery, Sci-Fi & Fantasy'), ('Chernobyl', 'tt7366338', 'Drama, History'), ('Sherlock', 'tt1475582', 'Crime, Drama, Mystery'),
          ('The Witcher', 'tt5180504', 'Sci-Fi & Fantasy, Drama, Action & Adventure'), ('Peaky Blinders', 'tt2442560', 'Drama, Crime'), ('True Detective', 'tt2356777', 'Drama, Crime, Mystery'),
          ('Westworld', 'tt0475784', 'Sci-Fi & Fantasy, Western, Drama'), ('Better Call Saul', 'tt3032476', 'Crime, Drama'), ('The Boys', 'tt1190634', 'Sci-Fi & Fantasy, Action & Adventure, Comedy'),
          ('Wednesday', 'tt13443470', 'Sci-Fi & Fantasy, Mystery, Comedy'), ('Squid Game', 'tt10919420', 'Action & Adventure, Mystery, Drama'), ('Money Heist', 'tt6468322', 'Crime, Drama'),
          ('Ted Lasso', 'tt10986410', 'Comedy, Drama')]
VOD_CATS = ['EN | New releases', 'EN | Action & Thriller', 'EN | Family', 'NL | Films', 'Classics']
SER_CATS = ['EN | Series', 'NL | Series', 'Netflix Originals']
MP4 = ['https://archive.org/download/ElephantsDream/ed_1024_512kb.mp4']
POSTER = 'https://images.metahub.space/poster/medium/%s/img'
MOVIES = []
for i, (t, y, imdb) in enumerate(FILMS):
    nl = i % 9 == 4
    MOVIES.append({'stream_id': 5000 + i, 'name': ('NL - ' if nl else '') + '%s (%d)' % (t, y), 'stream_icon': '' if i % 7 == 3 else POSTER % imdb,
                   'category_id': '4' if nl else str(1 + i % 3 if y >= 2000 else 5), 'container_extension': 'mp4', 'added': str(1700000000 + (len(FILMS) - i) * 86400), 'rating': '7.5'})
SHOWLIST = []
for i, (t, imdb, g) in enumerate(SERIES):
    SHOWLIST.append({'series_id': 7000 + i, 'name': ('NL | ' if i % 8 == 5 else '') + t, 'cover': POSTER % imdb, 'category_id': '2' if i % 8 == 5 else ('3' if i % 3 == 0 else '1'),
                     'genre': g, 'plot': '', 'rating': '8.2', 'releaseDate': '2016-01-01', 'last_modified': str(1700000000 + (len(SERIES) - i) * 3600)})


def b64(s):
    return base64.b64encode(s.encode('utf-8')).decode('ascii')


class H(SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=APP, **k)

    def log_message(self, f, *a):
        pass

    def send_json(self, o):
        b = json.dumps(o).encode('utf-8')
        self.send_response(200); self.send_header('Content-Type', 'application/json'); self.send_header('Content-Length', str(len(b))); self.end_headers(); self.wfile.write(b)

    def do_GET(self):
        u = urlparse(self.path); q = {k: v[0] for k, v in parse_qs(u.query).items()}
        if u.path == '/player_api.php':
            time.sleep(0.3)
            if q.get('username') != 'demo' or q.get('password') != 'demo':
                return self.send_json({'user_info': {'auth': 0}})
            a = q.get('action')
            if not a:
                return self.send_json({'user_info': {'auth': 1, 'status': 'Active', 'exp_date': str(int(time.time()) + 86400 * 200), 'max_connections': '2', 'allowed_output_formats': ['m3u8', 'ts']}})
            if a == 'get_live_categories':
                return self.send_json([{'category_id': str(i + 1), 'category_name': n} for i, n in enumerate(CATS)])
            if a == 'get_live_streams':
                return self.send_json(CHANNELS)
            if a == 'get_short_epg':
                sid = int(q.get('stream_id', '0')); now = int(time.time()); start = now - now % 1800 - (sid % 3) * 600
                l = []
                for j in range(int(q.get('limit', '2'))):
                    s = start + j * 1800
                    l.append({'title': b64(SHOWS[(sid + j) % len(SHOWS)]), 'description': b64('About this programme.'), 'start_timestamp': str(s), 'stop_timestamp': str(s + 1800)})
                return self.send_json({'epg_listings': l if sid % 4 else []})
            if a == 'get_simple_data_table':
                sid = int(q.get('stream_id', '0')); now = int(time.time()); arch = (sid - 1000) % 4 == 1
                if sid % 4 == 0:
                    return self.send_json({'epg_listings': []})
                start = now - now % 1800 - 3 * 86400; l = []; t = start; j = 0
                while t < now + 86400:
                    dur = 1800 if (sid + j) % 3 else 3600
                    l.append({'title': b64(SHOWS[(sid + j) % len(SHOWS)]), 'description': b64('About this programme on channel %d.' % (sid - 1000)),
                              'start': time.strftime('%Y-%m-%d %H:%M:%S', time.localtime(t)), 'end': time.strftime('%Y-%m-%d %H:%M:%S', time.localtime(t + dur)),
                              'start_timestamp': str(t), 'stop_timestamp': str(t + dur), 'has_archive': 1 if arch and t + dur < now else 0, 'now_playing': 1 if t <= now < t + dur else 0})
                    t += dur; j += 1
                return self.send_json({'epg_listings': l})
            if a == 'get_vod_categories':
                return self.send_json([{'category_id': str(i + 1), 'category_name': n} for i, n in enumerate(VOD_CATS)])
            if a == 'get_vod_streams':
                return self.send_json(MOVIES)
            if a == 'get_series_categories':
                return self.send_json([{'category_id': str(i + 1), 'category_name': n} for i, n in enumerate(SER_CATS)])
            if a == 'get_series':
                return self.send_json(SHOWLIST)
            if a == 'get_vod_info':
                m = next((x for x in MOVIES if str(x['stream_id']) == q.get('vod_id')), None)
                return self.send_json({'info': {'plot': '', 'duration': '01:58:00'} if m else []})
            if a == 'get_series_info':
                sid = int(q.get('series_id', '0'))
                eps = {}
                for s in (1, 2):
                    eps[str(s)] = [{'id': str(sid * 100 + s * 10 + e), 'episode_num': e, 'season': s, 'title': 'Episode %d' % e, 'container_extension': 'mp4',
                                    'info': {'plot': 'What happens in episode %d of season %d.' % (e, s), 'duration': '00:%02d:00' % (10 + e)}} for e in range(1, 7)]
                return self.send_json({'info': {}, 'episodes': eps})
            return self.send_json([])
        if u.path == '/get.php':
            lines = ['#EXTM3U']
            for c in CHANNELS:
                lines.append('#EXTINF:-1 tvg-id="%s" tvg-logo="%s" group-title="%s",%s' % (c['epg_channel_id'], c['stream_icon'], CATS[int(c['category_id']) - 1], c['name']))
                lines.append('http://localhost:%d/live/demo/demo/%d.m3u8' % (PORT, c['stream_id']))
            b = '\n'.join(lines).encode('utf-8')
            self.send_response(200); self.send_header('Content-Type', 'audio/x-mpegurl'); self.send_header('Content-Length', str(len(b))); self.end_headers(); self.wfile.write(b); return
        if u.path.startswith('/live/'):
            sid = int(os.path.splitext(u.path.rsplit('/', 1)[1])[0])
            if sid == 1007:
                self.send_response(404); self.end_headers(); return
            self.send_response(302); self.send_header('Location', STREAMS[sid % len(STREAMS)]); self.end_headers(); return
        if u.path.startswith('/timeshift/'):
            self.send_response(302); self.send_header('Location', MP4[0]); self.end_headers(); return
        if u.path.startswith('/movie/') or u.path.startswith('/series/'):
            sid = int(os.path.splitext(u.path.rsplit('/', 1)[1])[0])
            self.send_response(302); self.send_header('Location', MP4[sid % len(MP4)]); self.end_headers(); return
        if u.path.startswith('/logo/'):
            n = int(u.path[6:].split('.')[0]); hue = (n * 47) % 360
            svg = '<svg xmlns="http://www.w3.org/2000/svg" width="168" height="100"><rect rx="14" width="168" height="100" fill="hsl(%d,55%%,38%%)"/><text x="84" y="64" font-family="Arial" font-weight="bold" font-size="40" fill="#fff" text-anchor="middle">%d</text></svg>' % (hue, n)
            b = svg.encode(); self.send_response(200); self.send_header('Content-Type', 'image/svg+xml'); self.send_header('Content-Length', str(len(b))); self.end_headers(); self.wfile.write(b); return
        return super().do_GET()


if __name__ == '__main__':
    print('Recomp TV test provider on http://localhost:%d/  (login: server http://localhost:%d, demo / demo)' % (PORT, PORT))
    ThreadingHTTPServer(('127.0.0.1', PORT), H).serve_forever()
