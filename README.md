# wine-d2d1-msi — plain-Wine runner with d2d1-dcomp + the MSI string-pool fix

> **Credits:** this project — the MSI string-pool analysis and patch, and the
> whole build/packaging setup — was done entirely by **Kimi K3** (an AI
> assistant by Moonshot AI).

A standalone **Wine 11.0** runner built from [giang17](https://github.com/giang17/wine)'s
`d2d1-dcomp-11.0` branch — the same Direct2D 1.3 + DirectComposition patch series that
fixes JUCE 8 / VSTGUI / SynthEdit plugin GUIs rendering as **black windows** under Wine
(Pianoteq 9, Serum 2, Korg Trinity/Prophecy, EZkeys 2, Garritan CFX, …) — **plus** an
additional patch that fixes Wine's MSI string-table corruption, which breaks **Native
Instruments InstallAware installers** (Kontakt 8, …).

This is the **non-Soda** sibling of [HeapHeapHooray/soda-d2d1](https://github.com/HeapHeapHooray/soda-d2d1):
the same d2d1/dcomp functionality, but built on giang17's **plain Wine 11.0** branch
(the exact source [mklnln/wine-d2d1-dcomp](https://github.com/mklnln/wine-d2d1-dcomp)
packages) instead of Valve's Proton/Soda tree.

> **Related:** [HeapHeapHooray/KontaktInstallWine](https://github.com/HeapHeapHooray/KontaktInstallWine)
> — the standalone Kontakt 8 installer-for-Wine package (root-cause analysis + a ready-to-run
> install procedure). The MSI fix in *this* repo is the same bug, patched at the Wine level so
> the stock NI installer works unmodified.

## What's in the box

- **Base**: `giang17/wine` branch `d2d1-dcomp-11.0` @ `46c43a2db62ceeac1b33b31bccdebda65ef7f770`
  (the branch state at **2026-07-03** — plain Wine 11.0 + the d2d1/dcomp/dwrite/dxgi/wined3d/win32 fixes, already in the branch).
- **Patches**:
  - `patches/0007-msi-rewrite-all-tables-on-long-strref.mypatch` — the MSI fix.
  - `patches/0008-wined3d-only-map-host-visible-bo.mypatch` — the wined3d Vulkan buffer mapping fix for Kontakt 8 D3D backend.

## The MSI fix (Option A)

Wine's MSI engine corrupts a database when a transaction makes its string pool grow past
**65,535** entries. `st_find_free_entry()` grows the table 1.5× and `msi_save_string_table()`
persists *all* slots (including empty ones), flipping the pool to "long string reference"
(`LONG_STR_BYTES`) mode. But `MSI_CommitTables()` only rewrote the tables touched by the
transaction in the new format — the rest (including the `_Tables`/`_Columns` catalogs) kept
their original short references. The committed file is then internally inconsistent and
cannot be read back.

NI's InstallAware installers edit their (large) MSI in place, which inflates the pool past
the threshold, so the install aborts during package creation. The patch makes
`MSI_CommitTables()` load **every** table from the `_Tables` catalog and rewrite them all in
the long-reference format, keeping the database consistent.

## The wined3d Vulkan host-visible buffer mapping fix

In `dlls/wined3d/adapter_vk.c`, `adapter_vk_alloc_bo()` unconditionally called `wined3d_bo_vk_map()` when allocating Vulkan buffer objects. For GPU-only buffers allocated without `VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT`, this caused `vkMapMemory()` to fail with `VK_ERROR_MEMORY_MAP_FAILED`, producing allocation errors (`ERR("Failed to map bo.\n")`) and breaking Direct3D backend setup in Kontakt 8 under Wine's Vulkan backend. The patch checks for `VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT` before attempting to map buffer objects.

## Build

### Locally

```bash
./build.sh
```

Clones giang17's branch (pinned), applies the patch, builds with new-WoW64
(`--enable-archs=i386,x86_64`, no lib32 deps), and produces
`dist/wine-d2d1-msi-11.0-x86_64.tar.xz` (~30–60 min, ~4 GB). The script offers to install
the build dependencies via apt first (asks before using sudo; set `INSTALL_DEPS=yes` to
skip the prompt in non-interactive use).

### CI (GitHub Actions)

`.github/workflows/build-wine-d2d1-msi.yml` mirrors
[soda-d2d1](https://github.com/HeapHeapHooray/soda-d2d1)'s workflow: it installs the build
deps on `ubuntu-latest`, clones giang17's branch (pinned), applies the patch, builds, and
publishes the runner tarball as a GitHub release. Push to `master`/`main` or run it
manually with **Run workflow**.

## Install

```bash
# cheapwine
tar -xJf dist/wine-d2d1-msi-11.0-x86_64.tar.xz -C ~/.local/share/cheapwine/runners/
# or Bottles
tar -xJf dist/wine-d2d1-msi-11.0-x86_64.tar.xz -C ~/.local/share/bottles/runners/
```

Then select **wine-d2d1-msi-11.0** as the runner.

## Notes

- **Don't install DXVK for DComp plugins** — the DComp/DXGI patches live in Wine's builtin
  `dxgi.dll`; DXVK replaces it and bypasses them.
- **Kontakt 8 Graphics Backend**: You need to run `winetricks renderer=vulkan` (or use the default `wined3d` Vulkan renderer) to properly use Kontakt 8 without graphics backend initialization errors.
- The MSI fix is self-contained in `dlls/msi/table.c` and is safe for installers that don't
  cross the threshold (it only changes behavior when the pool exceeds 65,535 entries).

## Credits

- d2d1/dcomp patch series: **giang17** — [github.com/giang17/wine](https://github.com/giang17/wine)
- Standalone packaging this base is taken from: [mklnln/wine-d2d1-dcomp](https://github.com/mklnln/wine-d2d1-dcomp)
- MSI string-pool analysis + patch, and this build tooling: **Kimi K3** (Moonshot AI)
- wined3d Vulkan host-visible BO mapping patch (Kontakt 8 D3D backend fix): **Gemini 3.6 Flash** (Google DeepMind)

License: LGPL-2.1-or-later, same as Wine.
