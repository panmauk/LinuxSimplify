LinuxSimplify - PMX  (Linux edition)

The Linux counterpart of the Windows app, built with **Avalonia (.NET)** and
shipped as **one self-contained binary** — no .NET to install, no `apt`, no
dependencies to fetch. Download it, make it executable, run it.

```
chmod +x linuxsimplify-pmx
./linuxsimplify-pmx
```

Run it as your **normal user**. When it writes the USB it elevates just that
step with `pkexec` (you'll get a password prompt). Works on any desktop Linux
(X11 or Wayland) — including PMX OS itself (Debian + KDE).

## What it does

Same flow as the Windows version: scan the machine → grab the latest PMX OS
release from GitHub (github.com/panmauk/PMX-OS) → join the split ISO parts →
verify SHA-256 → flash to a USB drive → done. Two looks toggled top-right:
**PMX OS** (PANMOX dark/serif/cyan, default) and **RETRO** (light).

It always reads the newest release and works out the ISO parts + checksum from
the asset names, so new releases work with no code changes.

## The binary

`dist/linuxsimplify-pmx` (~43 MB) is the whole app — the .NET runtime, Avalonia,
and the Skia renderer are bundled inside. That single file is what you put on
your site.

## Build it yourself

```
./build.sh          # needs the .NET 8 SDK; prints the install one-liner if missing
```

Sources: `Program.cs`, `App.cs`, `MainWindow.cs` (UI + flow), `Theme.cs` (the two
looks), `Core.cs` (GitHub release resolution, download+join+verify, hardware
scan, USB list, `dd` flashing).

Official home: panmox.org · License: GNU GPL v3
