LinuxSimplify - PMX — PMX OS Installer

A simple app that gets PMX OS onto a USB drive from Windows, in one flow.
It scans your hardware, downloads the latest PMX OS release, and flashes it to a USB drive — ready to boot and install.

PMX OS is a peer-to-peer internet, in an operating system. Debian-based, KDE Plasma. No cloud. No telemetry. No boss.
Official home: panmox.org — releases mirrored on GitHub (github.com/panmauk/PMX-OS).

How it works:

Slide to scan your hardware,
Download PMX OS (always grabs the newest release)
Flash it to a USB drive
Boot from USB and install

Features:

Detects CPU, RAM, GPU, storage, and boot mode.
Always checks GitHub for the latest PMX OS release — no hardcoded versions.
Handles split ISOs automatically: downloads every part and joins them back into one ISO,
no matter how the parts are named in future releases.
Verifies the assembled ISO with the published SHA-256 checksum.
Raw USB flashing (like dd/Rufus).
Auto-detects USB drives when plugged in.
Cleans up the ISO after flashing.
Two looks, toggle in the top-right corner: RETRO (the iOS-era skeuomorphic UI) and PMX OS (the PANMOX dark/serif look). The button shows the theme you'll switch to.

Install note: disable Secure Boot, then boot the UEFI USB entry and install PMX OS.
Requirements: Windows 10/11, admin rights for USB flashing
License: GNU GPL v3
Created by @actuallypanmauk (X/Twitter) — panmox.org
