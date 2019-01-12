using CawkEmulatorV4;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConfuserEx_Unpacker.Protections.Constants
{
    class Remover:Base
    {
        #region FindInitalise

        public override void Deobfuscate()
        {
            byte[] bytes = InitaliseBytes(FindInitialiseMethod());
            if (bytes == null)
                return;
            DecryptionMethod(ModuleDef, bytes);
        }
        public static List<Instruction> C = new List<Instruction>();

        private static bool GetCalls(ModuleDefMD module, MethodDef method)
        {
            for (var y = 0; y < method.Body.Instructions.Count; y++)
            {
                if (method.Body.Instructions[y].OpCode != OpCodes.Call) continue;
                C.Add(method.Body.Instructions[y]);
            }

            return SortList();
        }

        private static bool SortList()
        {
            const string dgrfs = "System.Reflection.Assembly System.Reflection.Assembly::Load(System.Byte[])";
            return C.All(t => !t.Operand.ToString().Contains(dgrfs));
        }

        public static MethodDef FindInitialiseMethod()
        {
            foreach (var method in ModuleDef.GlobalType.Methods)
            {
                if (!method.HasBody) continue;
                if (!method.IsConstructor) continue;
                if (!method.FullName.ToLower().Contains("module")) continue;
                for (var i = 0; i < method.Body.Instructions.Count; i++)
                    if (method.Body.Instructions[i].OpCode == OpCodes.Call&&method.Body.Instructions[i].Operand is MethodDef)
                    {
                        var initMethod = (MethodDef)method.Body.Instructions[i].Operand;
                        if (!initMethod.HasBody) continue;
                        if (initMethod.Body.Instructions.Count < 300) continue;
                        for (var y = 0; y < initMethod.Body.Instructions.Count; y++)
                            if (initMethod.Body.Instructions[y].OpCode == OpCodes.Stloc_0)
                                if (initMethod.Body.Instructions[y - 1].IsLdcI4())
                                {
                                    C.Clear();
                                    var calls = GetCalls(ModuleDef, initMethod);
                                    if (calls == false) continue;
                                    return initMethod;
                                }
                    }
            }

            return null;
        }
        #endregion

        #region Initalise

        public static byte[] InitaliseBytes(MethodDef methods)
        {
            byte[] bytes = null;
            if (methods == null)
                return bytes;
            Emulation emulator = new Emulation(methods);
            emulator.OnCallPrepared += (emulation, args) =>
            {
                var instr = args.Instruction;
                Utils.HandleCall(args, emulation);
            };
            emulator.OnInstructionPrepared += (emulation, args) =>
            {
                if (args.Instruction.OpCode == OpCodes.Stsfld)
                {
                    bytes = emulation.ValueStack.CallStack.Pop();
                    args.Break = true;
                }
            };
            emulator.Emulate();

            return bytes;
        }

        #endregion

        #region Decrypt

        public static object DecryptConstant(MethodDef decryptionMethod, object[] param, byte[] bytes)
        {
            Emulation emulator = new Emulation(decryptionMethod);
            emulator.OnCallPrepared += (emulation, args) =>
            {
                var instr = args.Instruction;

                Utils.HandleCall(args, emulation);
            };
            emulator.OnInstructionPrepared += (emulation, args) =>
            {
                if (args.Instruction.OpCode == OpCodes.Ldsfld)
                {
                    emulator.ValueStack.CallStack.Push(bytes);
                    args.Cancel = true;
                }
            };
            foreach (Parameter decryptionMethodParameter in decryptionMethod.Parameters)
            {
                emulator.ValueStack.Parameters[decryptionMethodParameter] = param[decryptionMethodParameter.Index];
            }

            emulator.Emulate();
            return emulator.ValueStack.CallStack.Pop();
        }

        #endregion

        #region Cleaner

        public static void DecryptionMethod(ModuleDefMD module, byte[] bytes)
        {
            foreach (TypeDef typeDef in module.GetTypes())
            {
                foreach (MethodDef methods in typeDef.Methods)
                {
                    if (!methods.HasBody) continue;
                    for (int i = 0; i < methods.Body.Instructions.Count; i++)
                    {
                        if (methods.Body.Instructions[i].OpCode == OpCodes.Call &&
                            methods.Body.Instructions[i].Operand is MethodSpec)
                        {
                            MethodDef decryptionMethod =
                                ((MethodSpec)methods.Body.Instructions[i].Operand).ResolveMethodDef();
                            if (decryptionMethod != null && decryptionMethod.ReturnType.IsGenericMethodParameter &&
                                decryptionMethod.ReturnType.IsGenericParameter)
                            {
                                if (methods.Body.Instructions[i].Operand.ToString().Contains("System.String"))
                                {
                                    var paramsCount = decryptionMethod.Parameters.Count;
                                    var paramss = new object[paramsCount];
                                    if (paramsCount == 1)
                                    {
                                        if (methods.Body.Instructions[i - 1].IsLdcI4())
                                        {
                                            paramss[0] = methods.Body.Instructions[i - 1].Operand;
                                            methods.Body.Instructions[i - 1].OpCode = OpCodes.Nop;
                                        }
                                    }

                                    else
                                    {
                                        for (int y = 0; y < paramsCount; y++)
                                        {
                                            if (methods.Body.Instructions[i - y - 1].IsLdcI4())
                                            {
                                                paramss[y] = methods.Body.Instructions[i - y - 1].Operand;
                                                methods.Body.Instructions[i - y - 1].OpCode = OpCodes.Nop;
                                            }
                                        }
                                    }

                                    if (paramss.Any(g => g == null))
                                    {
                                        continue;
                                    }
                                    var result = DecryptConstant(decryptionMethod, paramss, bytes);
                                    if (result != null)
                                    {
                                        methods.Body.Instructions[i].OpCode = OpCodes.Ldstr;
                                        methods.Body.Instructions[i].Operand = result.ToString();
                                        Console.WriteLine("Decrypted string {0}", result);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}
