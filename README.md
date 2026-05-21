# osu! (DACH Fork)

This is a fork of [ppy/osu](https://github.com/ppy/osu) - the official osu!(lazer) client; containing some tournament extensions developed for the [DACH Open: Interim Masters](https://osu.ppy.sh/community/forums/topics/2153164) tournament.

For general osu!(lazer) information, issues, and contributions, please refer to the upstream repository.

## Custom Features

This fork adds support for custom EZ/EZHD score multipliers in the tournament client (`osu.Game.Tournament`), driven by live score data from an external custom helper tool, instead of the normal file-based IPC.

**More specifically:**
- **Custom EZ(HD) multipliers** - custom per-beatmap EZ and EZHD score multipliers, configurable in the Round Editor.
- **Helper-based scoring** - reads score data from [osu-tourney-data-reader](https://github.com/medylme/osu-tourney-data-reader) via WebSocket, with the default IPC file reads acting as a fallback.

## Requirements

- [osu-tourney-data-reader](https://github.com/medylme/osu-tourney-data-reader)
- [osu!tourney](https://osu.ppy.sh/wiki/en/osu%21_tournament_client/osu%21tourney) (osu!stable, cutting-edge)

## Running the fork

You can grab the latest release from the [Releases](https://github.com/medylme/osu-dach/releases/latest) page (Windows only), or build from source using the [upstream build instructions](https://github.com/ppy/osu/#building).

## Usage

1. Launch osu!tourney and wait for all instances to load.
2. Start `osu-tourney-data-reader` (defaults to port 25050).
3. In the tournament client Setup screen, enable **"Use helper for gameplay scores"** and set the WebSocket URL (e.g. `127.0.0.1:25050`), then click **Connect**.
4. To use custom multipliers for a beatmap, open the Round Editor, tick **Custom Mod Multipliers** on the beatmap row, and enter your custom values.

## Data directory

This fork stores all data in `%appdata%\osu-dach\` instead of (`%appdata%\osu`), so that you can run both side by side without any risk to your existing osu! data.
