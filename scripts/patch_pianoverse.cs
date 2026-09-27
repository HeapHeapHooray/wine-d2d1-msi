using System;
using System.IO;

class PianoversePatch
{
    // Target pattern to find: 13 bytes of 0xCC padding immediately followed by function prologue:
    // mov [rsp+8], rbx (48 89 5c 24 08)
    // push rdi         (57)
    // sub rsp, 0x70    (48 83 ec 70)
    // mov rdi, [rcx+0x40] (48 8b 79 40)
    static readonly byte[] TARGET_UNPATCHED = new byte[] {
        0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc,
        0x48, 0x89, 0x5c, 0x24, 0x08,
        0x57,
        0x48, 0x83, 0xec, 0x70,
        0x48, 0x8b, 0x79, 0x40
    };

    // Replacement: 13 bytes in padding + 5 bytes jump at function entry
    // In padding (13 bytes):
    //   test rcx, rcx          (48 85 c9)
    //   jz +7 -> ret           (74 07)
    //   mov [rsp+8], rbx       (48 89 5c 24 08)
    //   jmp +6 -> entry_cont   (eb 06)
    //   ret                    (c3)
    // At function entry (5 bytes):
    //   jmp -18 -> padding     (e9 ee ff ff ff)
    static readonly byte[] REPLACE_PATCH = new byte[] {
        0x48, 0x85, 0xc9,
        0x74, 0x07,
        0x48, 0x89, 0x5c, 0x24, 0x08,
        0xeb, 0x06,
        0xc3,
        0xe9, 0xee, 0xff, 0xff, 0xff
    };

    // Check if already patched
    static readonly byte[] TARGET_PATCHED = new byte[] {
        0x48, 0x85, 0xc9, 0x74, 0x07, 0x48, 0x89, 0x5c, 0x24, 0x08, 0xeb, 0x06, 0xc3,
        0xe9, 0xee, 0xff, 0xff, 0xff
    };

    static int IndexOf(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    static void PatchFile(string path)
    {
        try
        {
            string fileName = Path.GetFileName(path);
            if (!fileName.ToLowerInvariant().Contains("pianoverse"))
                return;

            byte[] data = File.ReadAllBytes(path);

            int patchedPos = IndexOf(data, TARGET_PATCHED);
            if (patchedPos != -1)
            {
                Console.WriteLine("[{0}] Already patched at 0x{1:x}", fileName, patchedPos);
                return;
            }

            int unpatchedPos = IndexOf(data, TARGET_UNPATCHED);
            if (unpatchedPos != -1)
            {
                // Create backup if not already present
                string bakPath = path + ".bak";
                if (!File.Exists(bakPath))
                {
                    File.Copy(path, bakPath);
                    Console.WriteLine("[{0}] Created backup: {1}", fileName, bakPath);
                }

                Array.Copy(REPLACE_PATCH, 0, data, unpatchedPos, REPLACE_PATCH.Length);
                File.WriteAllBytes(path, data);
                Console.WriteLine("[{0}] Successfully patched null dereference at 0x{1:x}\n", fileName, unpatchedPos);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error processing {0}: {1}", path, ex.Message);
        }
    }

    static void ScanDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        try
        {
            foreach (string file in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".vst3" || ext == ".dll" || ext == ".exe" || ext == ".aaxplugin")
                {
                    PatchFile(file);
                }
            }

            foreach (string sub in Directory.GetDirectories(dir))
            {
                ScanDirectory(sub);
            }
        }
        catch (Exception)
        {
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("=== IK Multimedia Pianoverse Plugin & Standalone Patcher ===");

        if (args.Length > 0)
        {
            foreach (string path in args)
            {
                if (File.Exists(path))
                    PatchFile(path);
                else if (Directory.Exists(path))
                    ScanDirectory(path);
            }
        }
        else
        {
            string[] commonPaths = new string[] {
                @"C:\Program Files\IK Multimedia\Pianoverse",
                @"C:\Program Files\Common Files\VST3",
                @"C:\Program Files\Common Files\Avid\Audio\Plug-Ins",
                @"C:\Program Files\Vstplugins",
                @"C:\Program Files (x86)\Vstplugins",
                @"C:\Program Files\Steinberg\Vstplugins",
                @"C:\Program Files (x86)\Steinberg\Vstplugins"
            };

            foreach (string p in commonPaths)
            {
                if (Directory.Exists(p))
                {
                    Console.WriteLine("Scanning: " + p);
                    ScanDirectory(p);
                }
            }
        }

        Console.WriteLine("Done!");
    }
}
