# wine-d2d1-msi — plain-Wine runner with d2d1-dcomp + the MSI string-pool fix

> [!NOTE]
> **Disclaimer:** The project has since outgrown its original purpose, which was an MSI-fix for Kontakt 8 Installation. Further work on software compatibility will be done on [https://github.com/HeapHeapHooray/wine-mozart](https://github.com/HeapHeapHooray/wine-mozart).

> **Credits:** this project — the MSI string-pool analysis and patch, and the
> whole build/packaging setup — was done entirely by **Kimi K3** (an AI
> assistant by Moonshot AI).

A standalone **Wine 11.0** runner built from [giang17](https://github.com/giang17/wine)'s
`d2d1-dcomp-11.0` branch — the same Direct2D 1.3 + DirectComposition patch series that
fixes JUCE 8 / VSTGUI / SynthEdit plugin GUIs rendering as **black windows** under Wine
(Pianoteq 9, Serum 2, Korg Trinity/Prophecy, EZkeys 2, Garritan CFX, …) — **plus**
additional patches that fix Wine's MSI string-table corruption, managed installers, and WMI/service infrastructure,
fixing applications like **Kontakt 8**, **Crow Hill App**, and **ROOTS Instruments**.

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
  - `patches/0009-mscoree-implement-CLRRuntimeInfo_GetProcAddress-and-IManagedInstaller.mypatch` — the mscoree fix for VS/WiX managed installer Custom Actions (e.g., Heavyocity Portal / HPWin2126.msi).
  - `patches/0010-wbemprox-implement-Win32_Service-Create-and-fix-wmic.mypatch` — `wbemprox` implementation of `Win32_Service.Create` and `wmic.exe` formatting fix for Crow Hill App, ROOTS Instruments, etc.
  - `patches/0011-wminet_utils-implement-COM-delegate-forwarding-and-_f-exports.mypatch` — `wminet_utils.dll` COM methods and `_f` export aliases for Mono `System.Management.dll` P/Invokes (Crow Hill App, ROOTS Instruments, WinSW services).
  - `patches/0012-opengl-support-child-window-and-egl-pfd-draw-to-window.mypatch` — OpenGL child window context creation, EGL `PFD_DRAW_TO_WINDOW` pixel format flags, and safe cross-connection cursor handling for `baseview` / `glutin` plugins (fixes Copycat VST3/CLAP GUI OpenGL crashes).
  - `patches/0013-configure-fallback-soname-libgl-and-libegl.mypatch` — Linux runtime fallback for `SONAME_LIBGL` (`libGL.so.1`) and `SONAME_LIBEGL` (`libEGL.so.1`) in `configure` / `configure.ac`, ensuring `winex11.drv` OpenGL driver is active even without build-time `libgl-dev` symlinks (fixes JUCE OpenGL context creation and black screen in Pianoverse VST3 / FL Studio).
  - `patches/0014-crypt32-preserve-pkcs-attributes-order.mypatch` — Preserves input attribute order in `CRYPT_AsnEncodePKCSAttributes` without sorting, fixing Authenticode signature verification (`TRUST_E_CERT_SIGNATURE` / 0x80096004) in FL Studio 2026 launcher (`FL64.exe` verifying `FLEngine_x64.dll`).
  - `scripts/patch_system_management.cs` — Mono `System.Management.dll` P/Invoke binder script.
  - `scripts/patch_gorilla_plugins.cs` — Gorilla Engine plugin binary patch script (Pocket Strings, Vaults, ROOTS Instruments).
  - `scripts/patch_pianoverse.cs` — IK Multimedia Pianoverse null dereference patch script (Pianoverse Standalone, VST3, VST2, AAX).

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

## The mscoree CLRRuntimeInfo_GetProcAddress & IManagedInstaller fix (HPWin2126.msi)

Visual Studio / WiX installer packages (like `HPWin2126.msi` for Heavyocity Portal) use `InstallUtil.dll` (CustomAction Type 1) to invoke managed `.NET` Custom Actions during installation via `IManagedInstaller::ManagedInstall`.

Two issues caused `mozapp run HPWin2126.msi` to fail with error 1603 (exit code 67):
1. `CLRRuntimeInfo_GetProcAddress` in `dlls/mscoree/metahost.c` was an unimplemented stub returning `E_NOTIMPL`. This caused `InstallUtil.dll`'s request for `"ClrCreateManagedInstance"` to fail, returning error code `-4` (`0xFFFFFFEC`).
2. When `ClrCreateManagedInstance` was called for `System.Configuration.Install.ManagedInstallerClass`, Wine Mono's `ManagedInstallerClass.InstallHelper` / `ManagedInstall` threw `System.NotImplementedException`, causing `InstallUtil.dll` to return `-5` (`0xFFFFFFEB`) and halting the MSI action execution.

The patch implements `CLRRuntimeInfo_GetProcAddress` to look up procedures exported by `mscoree.dll` (such as `ClrCreateManagedInstance`), and provides a built-in `IManagedInstaller` COM interface fallback object in `mscoree.dll` so managed installer actions execute cleanly.

## The wined3d Vulkan host-visible buffer mapping fix

In `dlls/wined3d/adapter_vk.c`, `adapter_vk_alloc_bo()` unconditionally called `wined3d_bo_vk_map()` when allocating Vulkan buffer objects. For GPU-only buffers allocated without `VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT`, this caused `vkMapMemory()` to fail with `VK_ERROR_MEMORY_MAP_FAILED`, producing allocation errors (`ERR("Failed to map bo.\n")`) and breaking Direct3D backend setup in Kontakt 8 under Wine's Vulkan backend. The patch checks for `VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT` before attempting to map buffer objects.

## The WMI & Mono System.Management.dll fixes (Crow Hill App / ROOTS Instruments)

Applications using WinSW or .NET installation services (`crowhillinstallservice.exe`, ROOTS Instruments) rely on WMI (`Win32_Service.Create`) and Mono's `System.Management.dll` P/Invoke binding to `wminet_utils.dll`.

1. **Native Engine Patches**:
   - `patches/0010-wbemprox-implement-Win32_Service-Create-and-fix-wmic.mypatch`: Implements `Win32_Service.Create` method calls and `co->record` signature handling in `wbemprox.dll`, and fixes `wmic.exe` formatting.
   - `patches/0011-wminet_utils-implement-COM-delegate-forwarding-and-_f-exports.mypatch`: Implements COM delegate forwarding methods (`GetMethod`, `GetNames`, `SpawnInstance`, `SetSecurity`, etc.) and exports all `_f` aliases in `wminet_utils.dll`.

2. **Mono System.Management.dll Binder Script**:
   - `scripts/patch_system_management.cs`: Replaces Mono's default `LoadPlatformNotSupportedDelegates()` stub in `System.Management.dll` (which throws `PlatformNotSupportedException`) with a dynamic `LoadLibraryW`/`GetProcAddress` P/Invoke loader that binds Mono's WMI delegates directly to Wine's native `wminet_utils.dll`.

### Usage for `scripts/patch_system_management.cs`

To patch a target prefix or bundled `System.Management.dll`:

```bash
wine mcs -r:"C:\windows\mono\mono-2.0\lib\mono\gac\Mono.Cecil\0.11.1.0__0738eb9f132ed756\Mono.Cecil.dll" \
         -r:System.Management.dll \
         scripts/patch_system_management.cs \
         -out:patch_system_management.exe

wine patch_system_management.exe "path/to/target/System.Management.dll"
```

## Gorilla Engine / Embedded Node.js & React UI Fixes (Pocket Strings, Vaults, ROOTS Instruments)

Instruments built with Gorilla Engine (Crow Hill plugins like Pocket Strings, Vaults, Westwood ROOTS) embed Node.js and React directly in the plugin binaries to render their UI.

1. **Stdio Error (`EINVAL: invalid argument, uv_pipe_open`)**:
   - In GUI DAWs like FL Studio under Wine, standard file descriptors (`stdout` / `stderr`) do not have an attached console, causing `_get_osfhandle(1)` to return `INVALID_HANDLE_VALUE`.
   - When the React component calls `console.log` / `console.warn`, the embedded `libuv` in the plugin checks `if (handle == INVALID_HANDLE_VALUE) return UV_EINVAL;`, throwing an unhandled exception in React that triggers the blue *"Oops! Something went wrong"* screen.
2. **Assertion Failed Dialog (`!(handle->flags & UV_HANDLE_CLOSING)`)**:
   - Statically compiled debug assertions in the plugin binaries trigger an MSVC assertion dialog box (`_wassert`) during handle shutdown.

### Usage for `scripts/patch_gorilla_plugins.cs`

To compile and run the patcher inside any Wine prefix (uses the built-in .NET / Mono C# compiler):

```bash
# Compile inside the target prefix
wine "C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" scripts/patch_gorilla_plugins.cs -out:patch_gorilla_plugins.exe

# Run inside the prefix (automatically scans standard VST3/VST2/AAX plugin folders)
wine patch_gorilla_plugins.exe
```

### Usage for `scripts/patch_pianoverse.cs`

IK Multimedia Pianoverse (standalone and VST3/VST2/AAX plugins) crashes on load under Wine with an unhandled page fault at `0x140683BCA` / `0x18069FE3A` (attempting to read `[rcx+0x40]` without verifying `rcx` is non-null during child window DPI/layout calculation).

The patcher injects a null-pointer safeguard into the alignment padding immediately preceding the window positioning handler:

```bash
# Compile inside the target prefix
wine "C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" scripts/patch_pianoverse.cs -out:patch_pianoverse.exe

# Run inside the prefix (automatically scans Pianoverse install and VST3/VST2/AAX folders)
wine patch_pianoverse.exe
```

## The OpenGL child-window & baseview/glutin context fixes (Copycat VST3/CLAP, Rust audio plugins)

Audio plugins built with Rust (`nih-plug`, `baseview`, `glutin`) such as **Copycat** create embedded `WS_CHILD` OpenGL viewports inside DAWs like FL Studio.

Two issues caused OpenGL plugins to crash:
1. `winex11.drv` and `win32u` stripped the `PFD_DRAW_TO_WINDOW` pixel format descriptor flag whenever an X visual lookup did not return a separate native visual ID for child window DCs, leaving 0 window-drawable pixel formats and causing `baseview` to panic with `Could not create OpenGL context: CreationFailed(())`.
2. When creating child windows, attempting to assign a default font cursor across separate X display connections triggered an X11 protocol `BadCursor` fatal error in `winex11.drv`.

The patch ensures all onscreen and window-capable pixel formats advertise `PFD_DRAW_TO_WINDOW`, falls back to `default_visual` when visual resolution is not directly provided, creates default window cursors on the window's own display connection, and wraps cursor operations with non-fatal X11 error handlers.

## The OpenGL / WGL runtime detection & black screen fix (Pianoverse VST3, JUCE plugins in FL Studio)

Complex JUCE-based audio plugins (such as **IK Multimedia Pianoverse VST3**) create hardware-accelerated OpenGL viewports attached to child HWNDs inside host DAWs (FL Studio, Reaper, Ableton).

When Wine is compiled in environments without `libgl-dev` or `libegl-dev` development link symlinks (e.g. standard build roots or cross-compile environments where only runtime `.so.1` libraries exist), Wine's `./configure` probe fails to link `-lGL` and leaves `SONAME_LIBGL` and `SONAME_LIBEGL` undefined in `config.h`.

As a consequence:
1. `dlls/winex11.drv/opengl.c` is compiled as dummy stubs returning `STATUS_NOT_IMPLEMENTED` (`0xc0000002`).
2. Wine falls back to `nulldrv`, which exposes 0 window-drawable pixel formats (`PFD_DRAW_TO_WINDOW`).
3. JUCE's `ChoosePixelFormat` returns `0`, and `wglCreateContext` fails with `ERROR_INVALID_PIXEL_FORMAT` (code 2000).
4. The plugin's OpenGL rendering thread dies on initialization while `paintingIsLocked` remains `true`, permanently suppressing `WM_PAINT` updates and leaving the plugin window as a solid black rectangle inside the DAW.

Patch `patches/0013-configure-fallback-soname-libgl-and-libegl.mypatch` provides automatic Linux fallbacks for `SONAME_LIBGL` (`"libGL.so.1"`) and `SONAME_LIBEGL` (`"libEGL.so.1"`). Since Wine loads these libraries at runtime via `dlopen()`, this ensures full OpenGL / WGL window rendering support out of the box even when `-dev` packages are absent during compilation.

## The Authenticode signature verification & PKCS attributes sorting fix (FL Studio 2026 launcher)

In newer versions of FL Studio (FL Studio 2026+), the 64-bit launcher executable (`FL64.exe`) verifies the digital signature of the main engine DLL (`FLEngine_x64.dll`) using `WinVerifyTrust` with action `WINTRUST_ACTION_GENERIC_VERIFY_V2` before loading the engine and launching the UI. If verification fails, the launcher presents the dialog box:
> *"Verification error: The validity of the program could not be verified. Please reinstall it and try again."*

During `WinVerifyTrust`, `wintrust.dll` calls `CryptMsgControl(..., CMSG_CTRL_VERIFY_SIGNATURE_EX, ...)` in `crypt32.dll` to verify the signer's digital signature over the authenticated attributes (`AuthAttrs`).

Two issues caused `WinVerifyTrust` to fail with `TRUST_E_CERT_SIGNATURE` (`0x80096004`):
1. `CSignedMsgData_UpdateAuthenticatedAttributes` in `dlls/crypt32/msg.c` re-encodes the authenticated attributes via `CryptEncodeObjectEx(X509_ASN_ENCODING, PKCS_ATTRIBUTES, ...)` to compute the message digest for signature verification.
2. In `dlls/crypt32/encode.c`, `CRYPT_AsnEncodePKCSAttributes` forwarded the attributes to `CRYPT_DEREncodeItemsAsSet`, which unconditionally called `qsort` using `BLOBComp` to sort all elements by DER TLV length and byte value.

While the general X.690 DER specification defines `SET OF` components as sorted, Microsoft Authenticode and Windows `CryptEncodeObject(..., PKCS_ATTRIBUTES, ...)` do **not** sort attributes automatically; they preserve the caller's array order. Furthermore, RFC 5652 (CMS) §5.4 explicitly stipulates:
> *"A separate encoding of the signedAttributes field, performed for message digest calculation, MUST NOT be performed. The authenticatedAttributes value as received from the message MUST be used..."*

When Wine re-sorted the 4 authenticated attributes (`1.3.6.1.4.1.311.2.1.11` statement type, `1.3.6.1.4.1.311.2.1.12` opus info, `contentType`, and `messageDigest`), the attribute order changed, producing a completely different SHA-256 hash. `RSAENH_CPVerifySignature` subsequently failed with `NTE_BAD_SIGNATURE` (`0x80090006`), causing `WinVerifyTrust` to fail.

Patch `patches/0014-crypt32-preserve-pkcs-attributes-order.mypatch` adds a `sort` control flag to `struct DERSetDescriptor` so `CRYPT_DEREncodeItemsAsSet` only sorts `SET OF` components when explicitly required, and sets `sort = FALSE` for `PKCS_ATTRIBUTES`. This preserves the original attribute order and allows Authenticode signatures in FL Studio 2026 (`FL64.exe` / `FLEngine_x64.dll`) to verify cleanly.

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
- wined3d Vulkan host-visible BO mapping patch (Kontakt 8 D3D backend fix), mscoree CLRRuntimeInfo_GetProcAddress + IManagedInstaller patch (HPWin2126.msi VS/WiX managed installer fix), wbemprox Win32_Service.Create & wmic patch (Crow Hill App & ROOTS Instruments service fix), wminet_utils COM delegate forwarding & _f export aliases patch + System.Management binder (Crow Hill App / Mono WMI fix), Gorilla Engine embedded Node.js/libuv patch script (Pocket Strings / Vaults / ROOTS Instruments fix), OpenGL child window context creation, EGL PFD_DRAW_TO_WINDOW flags & safe cursor handling patch (Copycat / baseview / glutin OpenGL plugin fix), Pianoverse null pointer dereference patch script, OpenGL/EGL soname configure fallback patch (Pianoverse VST3 / FL Studio black screen fix), and crypt32 PKCS attributes order preservation patch (FL Studio 2026 launcher signature verification fix): **Gemini 3.7 Flash** (Google DeepMind)

License: LGPL-2.1-or-later, same as Wine.
