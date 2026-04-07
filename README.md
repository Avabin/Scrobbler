# Scrobbler

Linux Last.fm scrobbler built around an MPRIS-monitoring daemon (`scrbl`) and a companion CLI (`scrbl-cli`).

The daemon watches MPRIS-compatible players, tracks playback progress, and scrobbles to Last.fm once the configured threshold is reached. The CLI handles setup, configuration, service installation, and status checks.

## Requirements

- Linux desktop session with MPRIS-compatible players
- .NET 10 SDK (`10.0.0` or compatible latest minor)
- systemd user services
- A Last.fm API key and secret

For release publishing of the CLI, NativeAOT tooling is also required. In practice that means a working native toolchain with `clang` available on `PATH`.

## How to build

Build both projects from the repository root:

```bash
dotnet build Scrobbler.slnx
```

This builds:

- `Scrobbler` -> `scrbl`
- `Scrobbler.Cli` -> `scrbl-cli`

Published binaries can be produced separately:

```bash
dotnet publish Scrobbler/Scrobbler.csproj -c Release -r linux-x64
dotnet publish Scrobbler.Cli/Scrobbler.Cli.csproj -c Release
```

Notes:

- The daemon publish command succeeds on a standard .NET SDK install.
- The CLI project is configured with `PublishAot=true`, so release publish depends on NativeAOT prerequisites. If `clang` is missing, the publish step fails.

## How to install

There are two parts to installation:

1. Put `scrbl` and `scrbl-cli` somewhere on your `PATH`.
2. Install a systemd user unit for the daemon.

The repository does not ship tracked binaries. Build or publish the projects locally, then install from those outputs.

### Install from your own build output

After building, copy the binaries to a directory on your `PATH`, for example:

```bash
install -Dm755 Scrobbler/bin/Debug/net10.0/scrbl ~/.local/bin/scrbl
install -Dm755 Scrobbler.Cli/bin/Debug/net10.0/scrbl-cli ~/.local/bin/scrbl-cli
```

If you published the daemon instead, the release binary is at:

```text
Scrobbler/bin/Release/net10.0/linux-x64/publish/scrbl
```

Then install the unit file:

```bash
scrbl-cli install
systemctl --user daemon-reload
systemctl --user enable scrobbler.service
systemctl --user start scrobbler.service
```

If `scrbl` is not yet on `PATH`, pass the daemon path explicitly:

```bash
scrbl-cli install /absolute/path/to/scrbl
```

### Uninstall

To remove the unit file:

```bash
scrbl-cli uninstall
systemctl --user stop scrobbler.service
systemctl --user disable scrobbler.service
systemctl --user daemon-reload
```

Remove the binaries manually if you copied them into `~/.local/bin`.

## How to use

Configuration is stored in:

```text
~/.config/scrobbler/appsettings.json
```

On first daemon start, a default config file is created automatically.

### Help

Top-level CLI help:

```text
Usage: [command] [-h|--help] [--version]

Commands:
  auth                    Authenticate with Last.fm by opening the browser for authorization.
     If API key/secret are already saved in config, they are used automatically.
  install                 Generate and install a systemd user unit file for the scrobbler daemon.
  list sources            List all discovered music players available for scrobbling.
  set api-key             Set the Last.fm API key.
  set api-secret          Set the Last.fm API secret.
  set scrobble-percent    Set the minimum scrobble percentage (0-100).
  set source              Set the preferred music player for scrobbling.
  status                  Show scrobbler daemon status: current track, last scrobbled, connection, players.
  uninstall               Remove the systemd user unit file.
```

### Typical setup flow

1. Save your Last.fm API credentials:

```bash
scrbl-cli set api-key YOUR_API_KEY
scrbl-cli set api-secret YOUR_API_SECRET
```

2. Authenticate in the browser:

```bash
scrbl-cli auth
```

3. Install and start the daemon if you have not already:

```bash
scrbl-cli install
systemctl --user daemon-reload
systemctl --user enable scrobbler.service
systemctl --user start scrobbler.service
```

4. Discover available players:

```bash
scrbl-cli list sources
```

This now prints 1-based indices, for example:

```text
Available music players:
  1. spotify *
  2. vlc
```

5. Optionally pin one player and change the threshold:

```bash
scrbl-cli set source 1
scrbl-cli set source --name spotify
scrbl-cli set scrobble-percent 50
```

6. Check daemon status:

```bash
scrbl-cli status
```

### What the main commands do

- `scrbl-cli auth` opens the Last.fm authorization page, waits for confirmation, stores the session key, and notifies the daemon if it is running.
- `scrbl-cli status` shows authentication state, selected player, available players, current track, playback progress, and the last scrobbled track.
- `scrbl-cli list sources` lists detected MPRIS players with 1-based indices and marks the currently selected one.
- `scrbl-cli set source <index>` stores the preferred player by index, and `scrbl-cli set source --name <player>` does the same by player name.
- `scrbl-cli set scrobble-percent <0-100>` changes how much of a track must be played before it is scrobbled.

### Troubleshooting

- If `scrbl-cli install` says it cannot find `scrbl`, either copy `scrbl` onto your `PATH` first or pass the full path as the command argument.
- If `scrbl-cli status` says the daemon is not running, check `systemctl --user status scrobbler.service` and `journalctl --user -u scrobbler.service -f`.
- If CLI release publish fails with a NativeAOT linker error, install `clang` and retry the publish command.