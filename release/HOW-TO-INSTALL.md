# Installing PMX OS

This folder has the **LinuxSimplify - PMX** installer — a small app that puts
PMX OS onto a USB drive for you. Pick the file for your current operating system,
make the USB, then boot from it.

| You're on… | Download this |
|------------|---------------|
| **Windows** (10 / 11) | `LinuxSimplify-PMX-windows-x64.exe` |
| **Linux** (any desktop) | `LinuxSimplify-PMX-linux-x64` |

Each one is a **single file** — nothing else to install, no .NET, no extra
packages. The app always fetches the **latest** PMX OS release on its own.

> ⚠️ **The USB drive gets completely erased.** Back up anything on it first.
> You'll want an 8 GB (or larger) USB stick.

---

## Windows

1. Download **`LinuxSimplify-PMX-windows-x64.exe`**.
2. Double-click it.
   - It needs admin rights to write the USB, so click **Yes** on the
     Windows security (UAC) prompt.
   - If Windows SmartScreen says "Windows protected your PC", click
     **More info → Run anyway** (the app isn't code-signed yet).
3. Follow the steps on screen (see **Using the app** below).

## Linux

Works on any desktop Linux (including PMX OS itself).

1. Download **`LinuxSimplify-PMX-linux-x64`**.
2. Make it executable and run it — as your **normal user**, not root:
   ```bash
   chmod +x LinuxSimplify-PMX-linux-x64
   ./LinuxSimplify-PMX-linux-x64
   ```
   (Or, in a file manager: right-click → Properties → tick **Allow executing
   as program**, then double-click it.)
3. When it's time to write the USB, it asks for your password (via the system
   `pkexec` prompt) — that's so it can write to the drive. Enter it and continue.

---

## Using the app (same on both)

1. **Scan** — the app reads your hardware.
2. **PMX OS** — it shows the latest release. Click **Download PMX OS**. It
   downloads the image (it comes in parts and gets joined automatically) and
   checks it's intact.
3. **Flash to USB Drive** — plug in your USB stick, pick it from the list
   (double-check it's the right one — it will be erased), and start.
4. **Done** — when it finishes, the USB is ready to boot.

Tip: there's a **PMX OS / RETRO** button in the top-right corner that switches
the app's look. Cosmetic only — pick whichever you like.

---

## Booting PMX OS from the USB

1. In your computer's firmware (BIOS/UEFI), **disable Secure Boot**.
2. Boot from the USB — choose the **UEFI** USB entry in the boot menu
   (often F12, F11, Esc, or Del at startup, depending on your PC).
3. PMX OS starts as a live system. Run **Install PMX OS** from the desktop to
   put it on your machine.

---

PMX OS — a peer-to-peer internet, in an operating system. Debian underneath.
No cloud. No telemetry. No boss.

Home: **panmox.org**
