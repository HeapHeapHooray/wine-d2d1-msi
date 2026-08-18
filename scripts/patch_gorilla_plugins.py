#!/usr/bin/env python3
"""
patch_gorilla_plugins.py
Fixes embedded Node.js / libuv errors in Gorilla Engine VST/AAX plugins (Crow Hill, Westwood, UJAM) running under Wine:
1. Skips debug assertion `!(handle->flags & UV_HANDLE_CLOSING)` in libuv async.c:76.
2. Returns success (0) when `uv_pipe_open` encounters an invalid stdio handle (`INVALID_HANDLE_VALUE`),
   preventing `Error: EINVAL: invalid argument, uv_pipe_open` when `console.log` / `console.warn` is called in GUI DAWs.
"""

import sys
import os

# Pattern 1: libuv async.c line 76 assertion check
# testb $0x1, 0x58(%rcx); je +0x19; mov $0x4c, %r8d ... call _wassert
TARGET_ASSERT = bytes.fromhex("f6415801741941b84c000000")
REPLACE_ASSERT = bytes.fromhex("eb1d90909090") # jmp over assertion

# Pattern 2: libuv pipe.c INVALID_HANDLE_VALUE return UV_EINVAL
# cmp %rcx, -1; jne +0x0d; mov (%rdi), %rbx; mov $0xfffff019, %eax; jmp +0x17e
TARGET_PIPE = bytes.fromhex("4883f9ff750d48891fb819f0ffffe9")
REPLACE_PIPE = bytes.fromhex("4883f9ff750d48891f31c0909090e9") # return 0 instead of UV_EINVAL

def patch_file(filepath):
    try:
        with open(filepath, "rb") as fp:
            data = bytearray(fp.read())
        
        modified = False
        
        # Patch Pattern 1
        pos1 = data.find(TARGET_ASSERT)
        if pos1 != -1:
            data[pos1 : pos1 + len(REPLACE_ASSERT)] = REPLACE_ASSERT
            modified = True
            print(f"[{os.path.basename(filepath)}] Patched assertion bypass at {hex(pos1)}")

        # Patch Pattern 2
        pos2 = data.find(TARGET_PIPE)
        if pos2 != -1:
            data[pos2 : pos2 + len(REPLACE_PIPE)] = REPLACE_PIPE
            modified = True
            print(f"[{os.path.basename(filepath)}] Patched uv_pipe_open invalid handle at {hex(pos2)}")

        if modified:
            with open(filepath, "wb") as fp:
                fp.write(data)
            print(f"Successfully updated: {filepath}\n")
    except Exception as e:
        print(f"Error processing {filepath}: {e}")

def main():
    target_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser("~/.mozapp/cheapwine/.cheapwine/drive_c/")
    print(f"Scanning directory: {target_dir}")
    for root, dirs, files in os.walk(target_dir):
        for f in files:
            if f.endswith(".vst3") or f.endswith(".dll") or f.endswith(".aaxplugin"):
                patch_file(os.path.join(root, f))

if __name__ == "__main__":
    main()
