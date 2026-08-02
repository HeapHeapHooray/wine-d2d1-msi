#!/usr/bin/env bash
# =============================================================================
# build.sh — Build a plain-Wine runner with giang17's d2d1-dcomp patch series
#            PLUS the MSI string-pool fix (Option A), *without* being Soda.
#
# This is the non-Soda sibling of HeapHeapHooray/soda-d2d1: the same Direct2D
# 1.3 + DirectComposition plugin-GUI fixes, but built on giang17's plain
# Wine 11.0 branch (not Valve's Proton/Soda tree), plus an additional patch
# that fixes Wine's MSI string-table corruption which breaks Native
# Instruments InstallAware installers (Kontakt 8, etc.).
#
# Base:   https://github.com/giang17/wine  branch d2d1-dcomp-11.0
#         (same source mklnln/wine-d2d1-dcomp packages)
# Patch:  patches/0007-msi-rewrite-all-tables-on-long-strref.mypatch
#
# Result: dist/wine-d2d1-msi-11.0-x86_64.tar.xz — extract into
#         ~/.local/share/cheapwine/runners/ (or bottles/runners/).
#
# Requirements: a C cross-compiler setup for Wine (mingw-w64, bison, flex,
#   autoconf, perl, gettext, libvulkan-dev), ~4 GB free disk, ~30-60 min.
#   On Debian/Ubuntu the script can install these via apt (it will ask first).
# =============================================================================
set -euo pipefail

PKG_BASENAME="wine-d2d1-msi-11.0"
GIANG17_REPO="https://github.com/giang17/wine.git"
GIANG17_BRANCH="d2d1-dcomp-11.0"
# Pinned base commit this adaptation was made and verified against:
GIANG17_COMMIT="8abcdd1fde0866f8d55c57586efc1567c9ce30d6"

cd "$(dirname "$0")"
REPO_ROOT="$PWD"
WORKDIR="$PWD/.work"
SRC="$WORKDIR/wine"
BUILD="$WORKDIR/build"

mkdir -p "$WORKDIR" dist

# --- 0. Build dependencies (optional, asks first) ---------------------------
need_deps() {
    ! command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1 || \
    ! command -v bison >/dev/null 2>&1 || \
    ! command -v flex  >/dev/null 2>&1
}
if need_deps; then
    echo "Some Wine build dependencies are missing (mingw-w64, bison, flex, ...)."
    if [ -t 0 ]; then
        read -r -p "Install them now with apt (uses sudo)? [y/N] " ans
    else
        ans="${INSTALL_DEPS:-no}"
    fi
    case "$ans" in
        y|Y|yes)
            sudo apt-get update
            sudo apt-get install -y --no-install-recommends \
                build-essential mingw-w64 bison flex autoconf perl gettext \
                libfreetype-dev libfontconfig-dev libpng-dev libjpeg-dev \
                libgif-dev libgnutls28-dev libasound2-dev libpulse-dev \
                libxcomposite-dev libxcursor-dev libxrandr-dev libxi-dev \
                libxinerama-dev libvulkan-dev
            ;;
        *) echo "Continuing anyway — the build may fail if deps are missing." ;;
    esac
fi

# --- 1. Fetch giang17's wine branch -----------------------------------------
if [ ! -d "$SRC/.git" ]; then
    git clone --no-checkout "$GIANG17_REPO" "$SRC"
fi
cd "$SRC"
git fetch --depth 1 origin "$GIANG17_COMMIT" || git fetch origin "$GIANG17_BRANCH"
git checkout -q "$GIANG17_COMMIT"

# --- 2. Apply the MSI string-pool fix ---------------------------------------
for p in "$REPO_ROOT"/patches/*.mypatch; do
    echo "Applying $(basename "$p")"
    git apply --stat "$p"
    git apply "$p"
done

# --- 3. Configure + build (new-WoW64, no lib32 deps) ------------------------
mkdir -p "$BUILD"
cd "$BUILD"
"$SRC/configure" --prefix="/opt/$PKG_BASENAME" --enable-archs=i386,x86_64
make -j"$(nproc)"

# --- 4. Stage + package the runner ------------------------------------------
STAGE="$WORKDIR/stage"
rm -rf "$STAGE"
make DESTDIR="$STAGE" install
RUNNER_DIR="$WORKDIR/${PKG_BASENAME}-x86_64"
rm -rf "$RUNNER_DIR"
mkdir -p "$RUNNER_DIR"
cp -a "$STAGE/opt/$PKG_BASENAME/." "$RUNNER_DIR/"
tar cJvf "$REPO_ROOT/dist/${PKG_BASENAME}-x86_64.tar.xz" -C "$WORKDIR" "${PKG_BASENAME}-x86_64"

echo
echo "Done: dist/${PKG_BASENAME}-x86_64.tar.xz"
echo "Install (cheapwine):  tar -xJf dist/${PKG_BASENAME}-x86_64.tar.xz -C ~/.local/share/cheapwine/runners/"
echo "Install (Bottles):    tar -xJf dist/${PKG_BASENAME}-x86_64.tar.xz -C ~/.local/share/bottles/runners/"
