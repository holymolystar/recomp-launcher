# Recomp TV (Samsung TV app)

TV & Movies from Recomp Launcher, as an app on a Samsung smart TV (Tizen, 2017 or newer). It works on its own: the PC
doesn't need to be on. You sign in to your TV provider once on the TV, and the login stays on the TV.

**What it has** (the launcher's TV & Movies, in the same look):
- **For You, Movies and Shows**, built from the same rows as the launcher:
  - A big featured title with Play and More info.
  - Continue watching and My list.
  - Top 10 (with the big numbers), Recently added and Recommended for you.
  - "Because you watched ..." rows.
  - A row per genre, Explore by decade, a Categories row, and then every category your provider has.
- **Poster cards.** When you stay on a poster it grows into a card with the wide picture, what it is and its story. The trailer plays over the picture when the title has one.
- **Film and series pages.** These have Play or Resume, From the start and My list. Series also show their seasons and episodes, with your progress and which ones you've watched (green marks one as watched).
- **The player.** It resumes where you stopped and remembers your place. You can seek (it shows where you'll land), pause, and choose audio and subtitles. In the last 20 seconds of an episode, it counts down to the next one.
  - **No dubs:** a film or series from elsewhere starts in its own language (Korean for a Korean film), when your provider has that audio. Settings > Original language first.
  - **Subtitles:** Dutch by default, English as an extra. The film's own when it has them, else found on OpenSubtitles (your free API key, in Settings) and kept on the TV, so they're never fetched twice.
  - **Subtitle timing and look:** ▲ in the player, then Timing (a tenth or a whole second, or "sync to a line") and Style (size, colour, a dark box). The timing is remembered per film or episode.
  - **Pause screen:** paused a while, it shows what you're watching and its story. After three episodes in a row without a button pressed, it asks "Are you still watching?".
- **TV Live**:
  - Categories, channels and a small preview with what's on now and next.
  - Full screen with fast zapping, number keys and PRE-CH.
  - My channels (yellow) and Watched lately.
- **TV Guide** (a Guide tab, or green in TV Live):
  - The picked channel plays small, with what's on.
  - Under it are the channels, with a timeline that starts half an hour ago.
  - OK on what's on now watches it, OK on a past programme replays it from the **TV Archive** (when your provider keeps it, marked ↺), and OK on a later programme sets a **reminder**.
  - Blue changes the category.
- **Profiles ("Who's watching?").** Each profile has its own My list, Continue watching and settings. Green renames a profile, blue changes its colour, red removes it.
- **Languages, as in the launcher.**
  - Language tags such as "NL ◉", "|PL|" and "UK:" are read and taken off names and categories.
  - Each profile shows only the languages it picks, and can choose whether titles without a tag show.
  - Each profile can also hide adult content.
  - Each profile has an audio language and a subtitle language to start with.
- **Search** with letters on the screen (or the TV's keyboard, or the number buttons), your recent searches under them, and what's found on the right as you type. Accents don't matter, and a name finds what someone plays in.
- **Versions:** a film's page has a Versions button when your provider has it more than once (another language, another quality).
- **New:** posters added in the last two weeks have a "New" badge; For You and Movies have **New this week** and **Collections** (films of one series together).
- **Cinemeta**, as in the launcher, fills in what your provider doesn't send: genres, wide pictures, logos, stories and trailers.

**Not (yet) here:** Discover, and the launcher's free channels and films. The guide needs an Xtream login; a playlist link has no guide yet. Recording and downloads can't be done on a TV app.

## Putting it on the TV (once)

1. **Turn on Developer Mode on the TV.**
   - Open **Apps** and press **1 2 3 4 5** on the remote.
   - Switch Developer Mode **On** and type this PC's IP address. To find it, run `ipconfig` and look for the "IPv4 Address".
   - Turn the TV off and on again.
   - Note the TV's own IP address: Settings > General > Network > Network Status > IP Settings.
2. **Install Tizen Studio** on the PC (free, from developer.samsung.com/smarttv). In its Package Manager, install:
   - Under **Main SDK**: "Tizen SDK tools".
   - Under **Extension SDK**: "TV Extensions" and "Samsung Certificate Extension".
3. **Make a certificate.**
   - In Tizen Studio, open **Tools > Certificate Manager** and add a profile.
   - Choose **Samsung**, then **TV**, and name it `RecompTV`.
   - Sign in with a Samsung account when it asks.
   - When it asks for the device, connect to your TV so its DUID is added.
4. **Install the app:**
   ```
   powershell -ExecutionPolicy Bypass -File samsung-tv\install-on-tv.ps1 -TvIp <the TV's IP address>
   ```
   Recomp TV then shows up among the TV's apps. Run the same command again to update it.

## The remote

Anywhere: up from the top of a page reaches the header (For You, TV Live, Movies, Shows, Search, and your profile for Settings). On a poster, OK opens it, Play plays it and yellow adds it to My list. Back twice on the header leaves the app.

TV Live:

| Button | TV Live list | Full screen |
|---|---|---|
| OK | play the channel small; again: full screen | show / hide the banner |
| Up / Down | move | previous / next channel |
| CH+ / CH- | a page up / down | previous / next channel |
| Left / Right | categories / channels | banner |
| Yellow | add to / remove from My channels | same |
| Green | the guide, at this channel (past programmes: TV Archive) | same |
| 0-9 | go to that channel number | same |
| PRE-CH | the channel before | same |
| Back | back to categories, then the header | back to the list |

## Trying it on the PC

```
python samsung-tv\dev\mock_provider.py
```

Then open http://localhost:8765/ and sign in with the server `http://localhost:8765`, username `demo` and password `demo`.

- This is a pretend provider with test streams, and channel 7 is broken on purpose.
- The keyboard stands in for the remote:
  - Arrows and Enter.
  - Esc for Back.
  - Page Up / Page Down for CH+/CH-.
  - F1-F4 for the colour buttons.
  - P for PRE-CH.
