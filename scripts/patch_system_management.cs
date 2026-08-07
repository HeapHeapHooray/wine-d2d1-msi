using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Patcher {
    static void Main(string[] args) {
        try {
            string dllPath = args.Length > 0 ? args[0] : @"C:\windows\mono\mono-2.0\lib\mono\gac\System.Management\4.0.0.0__b03f5f7f11d50a3a\System.Management.dll";
            Console.WriteLine("Patching System.Management.dll at: " + dllPath);

            AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(dllPath);

            TypeDefinition helper = null;
            foreach (TypeDefinition t in assembly.MainModule.Types) {
                if (t.FullName == "System.Management.WmiNetUtilsHelper") {
                    helper = t;
                    break;
                }
            }

            if (helper == null) {
                Console.WriteLine("Error: Could not find WmiNetUtilsHelper!");
                return;
            }

            MethodDefinition target = null;
            foreach (MethodDefinition m in helper.Methods) {
                if (m.Name == "LoadPlatformNotSupportedDelegates") {
                    target = m;
                    break;
                }
            }

            if (target == null) {
                Console.WriteLine("Error: Could not find LoadPlatformNotSupportedDelegates!");
                return;
            }

            Console.WriteLine("Found target method: " + target.FullName);

            ModuleDefinition mod = assembly.MainModule;

            // Add P/Invoke for LoadLibraryW
            ModuleReference kernel32 = new ModuleReference("kernel32.dll");
            mod.ModuleReferences.Add(kernel32);

            MethodDefinition loadLibDef = new MethodDefinition(
                "NativeLoadLibrary",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.PInvokeImpl | Mono.Cecil.MethodAttributes.HideBySig,
                mod.ImportReference(typeof(IntPtr))
            );
            loadLibDef.ImplAttributes = Mono.Cecil.MethodImplAttributes.PreserveSig;
            loadLibDef.Parameters.Add(new ParameterDefinition("lpFileName", Mono.Cecil.ParameterAttributes.None, mod.ImportReference(typeof(string))));
            loadLibDef.PInvokeInfo = new PInvokeInfo(
                PInvokeAttributes.CharSetUnicode | PInvokeAttributes.CallConvWinapi,
                "LoadLibraryW",
                kernel32
            );
            helper.Methods.Add(loadLibDef);

            // Add P/Invoke for GetProcAddress
            MethodDefinition getProcDef = new MethodDefinition(
                "NativeGetProcAddress",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.PInvokeImpl | Mono.Cecil.MethodAttributes.HideBySig,
                mod.ImportReference(typeof(IntPtr))
            );
            getProcDef.ImplAttributes = Mono.Cecil.MethodImplAttributes.PreserveSig;
            getProcDef.Parameters.Add(new ParameterDefinition("hModule", Mono.Cecil.ParameterAttributes.None, mod.ImportReference(typeof(IntPtr))));
            getProcDef.Parameters.Add(new ParameterDefinition("lpProcName", Mono.Cecil.ParameterAttributes.None, mod.ImportReference(typeof(string))));
            getProcDef.PInvokeInfo = new PInvokeInfo(
                PInvokeAttributes.CharSetAnsi | PInvokeAttributes.CallConvWinapi,
                "GetProcAddress",
                kernel32
            );
            helper.Methods.Add(getProcDef);

            MethodReference getDelegateRef = mod.ImportReference(typeof(Marshal).GetMethod("GetDelegateForFunctionPointer", new Type[] { typeof(IntPtr), typeof(Type) }));
            MethodReference getTypeFromHandleRef = mod.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle", new Type[] { typeof(RuntimeTypeHandle) }));

            ILProcessor il = target.Body.GetILProcessor();
            target.Body.Instructions.Clear();
            target.Body.Variables.Clear();
            target.Body.ExceptionHandlers.Clear();

            VariableDefinition vHMod = new VariableDefinition(mod.ImportReference(typeof(IntPtr)));
            VariableDefinition vProc = new VariableDefinition(mod.ImportReference(typeof(IntPtr)));
            target.Body.Variables.Add(vHMod);
            target.Body.Variables.Add(vProc);

            // hMod = NativeLoadLibrary("wminet_utils.dll")
            il.Append(il.Create(OpCodes.Ldstr, "wminet_utils.dll"));
            il.Append(il.Create(OpCodes.Call, loadLibDef));
            il.Append(il.Create(OpCodes.Stloc, vHMod));

            // For each delegate field in helper, if proc != 0, set field
            foreach (FieldDefinition f in helper.Fields) {
                if (!f.IsStatic) continue;

                Instruction skipLabel = il.Create(OpCodes.Nop);

                // proc = NativeGetProcAddress(hMod, f.Name)
                il.Append(il.Create(OpCodes.Ldloc, vHMod));
                il.Append(il.Create(OpCodes.Ldstr, f.Name));
                il.Append(il.Create(OpCodes.Call, getProcDef));
                il.Append(il.Create(OpCodes.Stloc, vProc));

                // if (proc == 0) goto skip
                il.Append(il.Create(OpCodes.Ldloc, vProc));
                il.Append(il.Create(OpCodes.Ldsfld, mod.ImportReference(typeof(IntPtr).GetField("Zero"))));
                il.Append(il.Create(OpCodes.Call, mod.ImportReference(typeof(IntPtr).GetMethod("op_Equality"))));
                il.Append(il.Create(OpCodes.Brtrue, skipLabel));

                // f = Marshal.GetDelegateForFunctionPointer(proc, typeof(FieldType))
                il.Append(il.Create(OpCodes.Ldloc, vProc));
                il.Append(il.Create(OpCodes.Ldtoken, f.FieldType));
                il.Append(il.Create(OpCodes.Call, getTypeFromHandleRef));
                il.Append(il.Create(OpCodes.Call, getDelegateRef));
                il.Append(il.Create(OpCodes.Castclass, f.FieldType));
                il.Append(il.Create(OpCodes.Stsfld, f));

                il.Append(skipLabel);
            }

            il.Append(il.Create(OpCodes.Ret));

            string outPath = args.Length > 1 ? args[1] : dllPath;
            assembly.Write(outPath);
            Console.WriteLine("Successfully wrote patched assembly to: " + outPath);

        } catch (Exception ex) {
            Console.WriteLine("Ex: " + ex);
        }
    }
}
