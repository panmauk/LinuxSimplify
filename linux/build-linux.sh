#!/usr/bin/env bash
# build-linux.sh - build and package LinuxSimplify for Linux, from scratch.
#
#   ./build-linux.sh [stage] [options]
#
# Stages:   all (default) | publish | deb | appimage | clean
# Options:  --arch x64|arm64|both   (default: both)
#           --version X.Y.Z         (default: <Version> from LinuxSimplify.Linux.csproj)
#           --work DIR              scratch directory (default: ~/build/linuxsimplify)
#           --dist DIR              output directory  (default: <repo>/linux/dist)
#
# Output (in dist/):
#   linuxsimplify_<ver>_amd64.deb   linuxsimplify_<ver>_arm64.deb
#   LinuxSimplify-<ver>-x86_64.AppImage   LinuxSimplify-<ver>-aarch64.AppImage
#
# Toolchain (nothing is installed by this script, no sudo needed):
#   * .NET SDK 10          $DOTNET_ROOT (default ~/.dotnet)
#   * dpkg-deb             (fakeroot is not required: --root-owner-group)
#   * appimagetool         $LVTOOLS/appimagetool-x86_64.AppImage  (+ runtime-x86_64 / runtime-aarch64 in $LVTOOLS)
#
# The repository may live on a slow /mnt/<drive> mount: sources are copied to the scratch directory first.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

STAGE="all"
ARCHES="x64 arm64"
VERSION=""
WORK="${WORK_DIR:-$HOME/build/linuxsimplify}"
DIST="${DIST_DIR:-$SCRIPT_DIR/dist}"
LVTOOLS="${LVTOOLS:-$HOME/lvtools}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

MAINTAINER="Panmox <panmauk@users.noreply.github.com>"
HOMEPAGE="https://panmox.org"

log()  { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
info() { printf '    %s\n' "$*"; }
die()  { printf '\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

# ---------------------------------------------------------------------------------------------
while [ $# -gt 0 ]; do
    case "$1" in
        all|publish|deb|appimage|clean) STAGE="$1" ;;
        --arch)
            case "${2:-}" in
                x64|arm64) ARCHES="$2" ;;
                both) ARCHES="x64 arm64" ;;
                *) die "--arch must be x64, arm64 or both" ;;
            esac
            shift ;;
        --version) VERSION="${2:?--version needs a value}"; shift ;;
        --work) WORK="${2:?--work needs a value}"; shift ;;
        --dist) DIST="${2:?--dist needs a value}"; shift ;;
        -h|--help) sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) die "unknown argument: $1 (try --help)" ;;
    esac
    shift
done

CSPROJ="$SCRIPT_DIR/LinuxSimplify.Linux.csproj"
[ -f "$CSPROJ" ] || die "cannot find $CSPROJ"
[ -n "$VERSION" ] || VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$CSPROJ" | head -n1)"
[ -n "$VERSION" ] || VERSION="1.0.1"

rid_of()       { case "$1" in x64) echo linux-x64 ;; arm64) echo linux-arm64 ;; esac; }
deb_arch_of()  { case "$1" in x64) echo amd64 ;; arm64) echo arm64 ;; esac; }
appimg_arch_of(){ case "$1" in x64) echo x86_64 ;; arm64) echo aarch64 ;; esac; }

# ---------------------------------------------------------------------------------------------
stage_clean() {
    log "Cleaning $WORK and $DIST"
    rm -rf "$WORK" "$DIST"
}

check_tools() {
    command -v dotnet >/dev/null || die "dotnet not found (set DOTNET_ROOT; expected $DOTNET_ROOT/dotnet)"
    info "dotnet $(dotnet --version)"
}

stage_sync() {
    log "Copying sources to $WORK/src"
    rm -rf "$WORK/src"
    mkdir -p "$WORK/src"
    (cd "$REPO_DIR" && tar --exclude=./.git --exclude='bin' --exclude='obj' --exclude=./linux/dist \
        --exclude=./installer -cf - .) | tar -xf - -C "$WORK/src"
    chmod 755 "$WORK/src/linux/packaging/linuxsimplify-flash"
}

stage_publish() {
    local arch="$1" rid out
    rid="$(rid_of "$arch")"
    out="$WORK/publish/$arch"
    log "Publishing $rid (self-contained) -> $out"
    rm -rf "$out"
    dotnet publish "$WORK/src/linux/LinuxSimplify.Linux.csproj" -c Release -r "$rid" --self-contained true \
        -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false -o "$out" --nologo -v minimal
    chmod 755 "$out/linuxsimplify" "$out/linuxsimplify-flash"
    find "$out" -name '*.pdb' -delete
    info "$(du -sh "$out" | cut -f1) in $(find "$out" -type f | wc -l) files"
}

# ---------------------------------------------------------------------------------------------
stage_deb() {
    local arch="$1" darch root pkg out size
    darch="$(deb_arch_of "$arch")"
    root="$WORK/deb/$arch/root"
    pkg="linuxsimplify_${VERSION}_${darch}.deb"
    log "Building $pkg"
    rm -rf "$WORK/deb/$arch"
    mkdir -p "$root/DEBIAN" "$root/opt/linuxsimplify" "$root/usr/bin" "$root/usr/share/applications" \
             "$root/usr/share/polkit-1/actions" "$root/usr/share/doc/linuxsimplify"

    cp -a "$WORK/publish/$arch/." "$root/opt/linuxsimplify/"
    install -m 0755 "$WORK/src/linux/packaging/linuxsimplify-flash" "$root/opt/linuxsimplify/linuxsimplify-flash"

    # launcher
    printf '#!/bin/sh\nexec /opt/linuxsimplify/linuxsimplify "$@"\n' > "$root/usr/bin/linuxsimplify"
    chmod 755 "$root/usr/bin/linuxsimplify"

    install -m 0644 "$WORK/src/linux/packaging/linuxsimplify.desktop" "$root/usr/share/applications/linuxsimplify.desktop"
    install -m 0644 "$WORK/src/linux/packaging/org.panmox.linuxsimplify.policy" \
        "$root/usr/share/polkit-1/actions/org.panmox.linuxsimplify.policy"

    local s
    for s in 16 24 32 48 64 96 128 256; do
        install -D -m 0644 "$WORK/src/linux/packaging/icons/linuxsimplify-$s.png" \
            "$root/usr/share/icons/hicolor/${s}x${s}/apps/linuxsimplify.png"
    done

    # docs
    cat > "$root/usr/share/doc/linuxsimplify/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: LinuxSimplify
Source: https://panmox.org

Files: *
Copyright: 2025 LinuxSimplify authors
License: GPL-3+

License: GPL-3+
 This program is free software: you can redistribute it and/or modify it under
 the terms of the GNU General Public License as published by the Free Software
 Foundation, either version 3 of the License, or (at your option) any later
 version.
 .
 On Debian systems, the complete text of the GNU General Public License
 version 3 can be found in /usr/share/common-licenses/GPL-3.
EOF
    printf 'linuxsimplify (%s) unstable; urgency=low\n\n  * Linux (Avalonia) build.\n\n -- %s  %s\n' \
        "$VERSION" "$MAINTAINER" "$(date -R)" | gzip -9n > "$root/usr/share/doc/linuxsimplify/changelog.gz"

    # normalise permissions: directories 755, plain files 644, keep the executables
    find "$root" -type d -exec chmod 755 {} +
    find "$root" -type f ! -perm -u+x -exec chmod 644 {} +
    find "$root" -type f -perm -u+x -exec chmod 755 {} +
    chmod 755 "$root/opt/linuxsimplify/linuxsimplify" "$root/opt/linuxsimplify/linuxsimplify-flash" "$root/usr/bin/linuxsimplify"

    size="$(du -sk --exclude=DEBIAN "$root" | cut -f1)"
    cat > "$root/DEBIAN/control" <<EOF
Package: linuxsimplify
Version: $VERSION
Section: utils
Priority: optional
Architecture: $darch
Maintainer: $MAINTAINER
Homepage: $HOMEPAGE
Installed-Size: $size
Depends: libc6 (>= 2.31), libgcc-s1, libstdc++6, zlib1g, libssl3t64 | libssl3, libx11-6, libice6, libsm6, libxcursor1, libxext6, libxfixes3, libxi6, libxrandr2, libfontconfig1, libfreetype6, util-linux, coreutils, pkexec | policykit-1
Recommends: udisks2, pciutils, libgl1
Description: Pick, download and write a Linux distro to a USB drive
 LinuxSimplify scans your hardware, recommends a GNU/Linux distribution that
 fits it, resolves and downloads the latest ISO (verifying its checksum) and
 writes it to a USB drive, ready to boot and install.
 .
 Writing to a drive needs administrator rights and is done by a small helper
 (linuxsimplify-flash) started through polkit; it refuses to touch the disk the
 system is running from and verifies what it wrote.
EOF

    cat > "$root/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
    fi
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
fi
exit 0
EOF
    cat > "$root/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
    fi
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
fi
exit 0
EOF
    chmod 755 "$root/DEBIAN/postinst" "$root/DEBIAN/postrm"

    mkdir -p "$DIST"
    out="$DIST/$pkg"
    rm -f "$out"
    dpkg-deb --root-owner-group -Zxz --build "$root" "$out" >/dev/null
    info "$(basename "$out")  $(du -h "$out" | cut -f1)"
}

# ---------------------------------------------------------------------------------------------
stage_appimage() {
    local arch="$1" aarch appdir out tool runtime
    aarch="$(appimg_arch_of "$arch")"
    appdir="$WORK/appimage/$arch/LinuxSimplify.AppDir"
    out="$DIST/LinuxSimplify-${VERSION}-${aarch}.AppImage"
    tool="$LVTOOLS/appimagetool-x86_64.AppImage"
    runtime="$LVTOOLS/runtime-${aarch}"
    [ -f "$tool" ]    || die "missing $tool"
    [ -f "$runtime" ] || die "missing $runtime"

    log "Building $(basename "$out")"
    rm -rf "$WORK/appimage/$arch"
    mkdir -p "$appdir/usr/lib/linuxsimplify" "$appdir/usr/share/icons/hicolor/256x256/apps"
    cp -a "$WORK/publish/$arch/." "$appdir/usr/lib/linuxsimplify/"
    install -m 0755 "$WORK/src/linux/packaging/linuxsimplify-flash" "$appdir/usr/lib/linuxsimplify/linuxsimplify-flash"

    # AppRun: APPDIR is exported so the app can tell it runs from an AppImage (root cannot read the FUSE mount, so
    # the flash helper is then passed to pkexec as script text instead of as a path - see UsbFlasher.cs)
    cat > "$appdir/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export APPDIR="$HERE"
exec "$HERE/usr/lib/linuxsimplify/linuxsimplify" "$@"
EOF
    chmod 755 "$appdir/AppRun"

    install -m 0644 "$WORK/src/linux/packaging/linuxsimplify.desktop" "$appdir/linuxsimplify.desktop"
    install -m 0644 "$WORK/src/linux/packaging/icons/linuxsimplify-256.png" "$appdir/linuxsimplify.png"
    install -m 0644 "$WORK/src/linux/packaging/icons/linuxsimplify-256.png" "$appdir/usr/share/icons/hicolor/256x256/apps/linuxsimplify.png"
    ln -s linuxsimplify.png "$appdir/.DirIcon"

    find "$appdir" -type d -exec chmod 755 {} +

    mkdir -p "$DIST"
    rm -f "$out"
    # FUSE is usually unavailable in containers/WSL: let appimagetool extract itself instead of mounting
    ARCH="$aarch" APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --runtime-file "$runtime" --no-appstream "$appdir" "$out" >"$WORK/appimagetool-$arch.log" 2>&1 \
        || { tail -n 30 "$WORK/appimagetool-$arch.log"; die "appimagetool failed for $aarch"; }
    chmod 755 "$out"
    info "$(basename "$out")  $(du -h "$out" | cut -f1)"
}

# ---------------------------------------------------------------------------------------------
main() {
    if [ "$STAGE" = "clean" ]; then stage_clean; exit 0; fi

    log "LinuxSimplify $VERSION  (stage: $STAGE, arch: $ARCHES)"
    info "repo: $REPO_DIR"
    info "work: $WORK"
    info "dist: $DIST"
    check_tools

    stage_sync
    local a
    for a in $ARCHES; do
        stage_publish "$a"
    done
    [ "$STAGE" = "publish" ] && exit 0

    for a in $ARCHES; do
        case "$STAGE" in
            all|deb)      stage_deb "$a" ;;
        esac
        case "$STAGE" in
            all|appimage) stage_appimage "$a" ;;
        esac
    done

    log "Done"
    ls -lh "$DIST"
}

main
