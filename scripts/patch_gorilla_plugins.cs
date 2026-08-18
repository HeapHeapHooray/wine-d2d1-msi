using System;
using System.IO;

class GorillaPatch
{
    // Pattern 1: libuv async.c:76 assertion check
    // f6 41 58 01 74 19 41 b8 4c 00 00 00 -> eb 1d 90 90 90 90
    static readonly byte[] TARGET_ASSERT = new byte[] {
        0xf6, 0x41, 0x58, 0x01, 0x74, 0x19, 0x41, 0xb8, 0x4c, 0x00, 0x00, 0x00
    };
    static readonly byte[] REPLACE_ASSERT = new byte[] {
        0xeb, 0x1d, 0x90, 0x90, 0x90, 0x90
    };

    // Pattern 2: Node.js PipeWrap::Open error check (prevents Error: EINVAL: invalid argument, uv_pipe_open)
    // 89 9d a0 00 00 00 85 c0 74 49 -> 89 9d a0 00 00 00 31 c0 eb 49
    static readonly byte[] TARGET_PIPE_WRAP = new byte[] {
        0x89, 0x9d, 0xa0, 0x00, 0x00, 0x00, 0x85, 0xc0, 0x74, 0x49
    };
    static readonly byte[] REPLACE_PIPE_WRAP = new byte[] {
        0x89, 0x9d, 0xa0, 0x00, 0x00, 0x00, 0x31, 0xc0, 0xeb, 0x49
    };

    // Pattern 3: libuv pipe.c INVALID_HANDLE_VALUE return UV_EINVAL
    // 48 83 f9 ff 75 0d 48 89 1f b8 19 f0 ff ff e9 -> 48 83 f9 ff 75 0d 48 89 1f 31 c0 90 90 90 e9
    static readonly byte[] TARGET_PIPE = new byte[] {
        0x48, 0x83, 0xf9, 0xff, 0x75, 0x0d, 0x48, 0x89, 0x1f, 0xb8, 0x19, 0xf0, 0xff, 0xff, 0xe9
    };
    static readonly byte[] REPLACE_PIPE = new byte[] {
        0x48, 0x83, 0xf9, 0xff, 0x75, 0x0d, 0x48, 0x89, 0x1f, 0x31, 0xc0, 0x90, 0x90, 0x90, 0xe9
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
            byte[] data = File.ReadAllBytes(path);
            bool modified = false;

            int pos1 = IndexOf(data, TARGET_ASSERT);
            if (pos1 != -1)
            {
                Array.Copy(REPLACE_ASSERT, 0, data, pos1, REPLACE_ASSERT.Length);
                modified = true;
                Console.WriteLine("[{0}] Patched assertion bypass at 0x{1:x}", Path.GetFileName(path), pos1);
            }

            int pos2 = IndexOf(data, TARGET_PIPE_WRAP);
            if (pos2 != -1)
            {
                Array.Copy(REPLACE_PIPE_WRAP, 0, data, pos2, REPLACE_PIPE_WRAP.Length);
                modified = true;
                Console.WriteLine("[{0}] Patched PipeWrap::Open error check at 0x{1:x}", Path.GetFileName(path), pos2);
            }

            int pos3 = IndexOf(data, TARGET_PIPE);
            if (pos3 != -1)
            {
                Array.Copy(REPLACE_PIPE, 0, data, pos3, REPLACE_PIPE.Length);
                modified = true;
                Console.WriteLine("[{0}] Patched uv_pipe_open invalid handle at 0x{1:x}", Path.GetFileName(path), pos3);
            }

            if (modified)
            {
                File.WriteAllBytes(path, data);
                Console.WriteLine("Successfully updated: " + path + "\n");
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
                if (ext == ".vst3" || ext == ".dll" || ext == ".aaxplugin")
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
        Console.WriteLine("=== Gorilla Engine / Crow Hill Plugin Patcher ===");

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
