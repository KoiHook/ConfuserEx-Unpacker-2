using CawkEmulatorV4;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ConfuserDeobfuscator.Engine.Routines.Ex.x86;

namespace ConfuserEx_Unpacker.Protections
{
    internal class Utils : Base
    {
        public static byte[] ModuleBytes { get; private set; }

        public override void Deobfuscate()
        {
            throw new NotImplementedException();
        }
        public static bool IsProxyMethod(MethodDef methods, out Instruction ins)
        {
            if (methods.IsStatic)
            {
                if (methods.IsReuseSlot)
                {
                    if (methods.IsPrivateScope)
                    {
                        ins = methods.Body.Instructions[methods.Body.Instructions.Count - 2];
                        if (ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Call ||
                            ins.OpCode == OpCodes.Newobj)
                        {
                            if (methods.Body.Instructions.Count < 10)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            ins = null;
            return false;
        }

        public static void HandleCall(CallEventArgs args, Emulation emulation)
        {
            if (args.Instruction.Operand is MethodDef &&
                IsProxyMethod((MethodDef)args.Instruction.Operand, out var ins))
            {
                args.Instruction = ins;
                HandleCall(args, emulation);
                return;
            }

            if (args.Instruction.Operand.ToString()
                .Contains(
                    "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle")
            )
            {
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                emulation.ValueStack.CallStack.Pop();
                var fielddef = ModuleDef.ResolveToken(stack2) as FieldDef;
                var test = fielddef.InitialValue;
                var decoded = new uint[test.Length / 4];
                Buffer.BlockCopy(test, 0, decoded, 0, test.Length);
                stack1 = decoded;
                emulation.ValueStack.CallStack.Push(stack1);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand is MethodSpec &&
                     IsUintDecyption(((MethodSpec)args.Instruction.Operand).ResolveMethodDef()))
            {
                var method = ((MethodSpec)args.Instruction.Operand).ResolveMethodDef();
                var initaliseMethod = Constants.Remover.FindInitialiseMethod();
                var initBytes = Constants.Remover.InitaliseBytes(initaliseMethod);
                var param = new object[args.Pops];
                for (var i = 0; i < param.Length; i++) param[i] = emulation.ValueStack.CallStack.Pop();

                emulation.ValueStack.CallStack.Push(Constants.Remover.DecryptConstant(method, param, initBytes));
                args.bypassCall = true;
            }
            
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()"))
            {
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.String System.String::Intern(System.String)"))
            {
                emulation.ValueStack.CallStack.Push(string.Intern(emulation.ValueStack.CallStack.Pop()));
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.String System.Text.Encoding::GetString(System.Byte[],System.Int32,System.Int32)"))
            {
                var stack4 = emulation.ValueStack.CallStack.Pop();
                var stack3 = emulation.ValueStack.CallStack.Pop();
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                var result = stack1.GetString(stack2, stack3, stack4);
                emulation.ValueStack.CallStack.Push(result);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Text.Encoding System.Text.Encoding::get_UTF8()"))
            {
                emulation.ValueStack.CallStack.Push(Encoding.UTF8);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Array System.Array::CreateInstance(System.Type,System.Int32)"))
            {
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                var result = Array.CreateInstance(stack1, stack2);
                emulation.ValueStack.CallStack.Push(result);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Type System.Type::GetElementType()"))
            {
                var stack = emulation.ValueStack.CallStack.Pop();
                emulation.ValueStack.CallStack.Push(stack.GetElementType());
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)"))
            {
                var stack = emulation.ValueStack.CallStack.Pop();
                emulation.ValueStack.CallStack.Push(typeof(uint[]));
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains(
                    "System.Void System.Buffer::BlockCopy(System.Array,System.Int32,System.Array,System.Int32,System.Int32)")
            )
            {
                var stack5 = emulation.ValueStack.CallStack.Pop();
                var stack4 = emulation.ValueStack.CallStack.Pop();
                var stack3 = emulation.ValueStack.CallStack.Pop();
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                Buffer.BlockCopy(stack1, stack2, stack3, stack4, stack5);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Reflection.Module System.Reflection.Assembly::get_ManifestModule()"))
            {
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand is MethodDef && isDecryptMethod((MethodDef)args.Instruction.Operand))
            {
                var bytes = Compressor.Remover.HandleDecryptMethod(emulation, args,
                    (MethodDef)args.Instruction.Operand);
                emulation.ValueStack.CallStack.Push(bytes);
                var bytes2 = (byte[])bytes.Target;
                Compressor.Remover.ModuleBytes = new byte[bytes2.Length];
                Buffer.BlockCopy(bytes2, 0, Compressor.Remover.ModuleBytes, 0, bytes2.Length);

                
                
               
                ModuleBytes = bytes2;
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Void System.Array::Clear(System.Array,System.Int32,System.Int32)"))
            {
                var stack3 = emulation.ValueStack.CallStack.Pop();
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                //             File.WriteAllBytes("arrayemu", stack1);
                if (stack1 is Array)
                    Array.Clear(stack1, stack2, stack3);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand is MethodDef && isDecompressMethod((MethodDef)args.Instruction.Operand))
            {
                var stack = emulation.ValueStack.CallStack.Pop();
                var decrypted = LzmaDecompress(stack);
                //        Protections.Compressor.Remover.ModuleBytes = decrypted;
                //        File.WriteAllBytes("arrayemu",stack);
                emulation.ValueStack.CallStack.Push(decrypted);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Byte[] System.Reflection.Module::ResolveSignature(System.Int32)"))
            {
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                emulation.ValueStack.CallStack.Push(ModuleDef.ReadBlob((uint)stack2));
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains("System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)"))
            {
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                Compressor.Remover.ModuleEp = stack2;
                args.endMethod = true;
            }
            else if (args.Instruction.Operand.ToString()
                .Contains(
                    "System.Runtime.InteropServices.GCHandle System.Runtime.InteropServices.GCHandle::Alloc(System.Object,System.Runtime.InteropServices.GCHandleType")
            )
            {
                var stack2 = emulation.ValueStack.CallStack.Pop();
                var stack1 = emulation.ValueStack.CallStack.Pop();
                emulation.ValueStack.CallStack.Push(GCHandle.Alloc(stack1, GCHandleType.Pinned));
                args.bypassCall = true;
            }

            else if (args.Instruction.Operand.ToString()
                .Contains(
                    "System.Object System.Runtime.InteropServices.GCHandle::get_Target()")
            )
            {
                
                var stack1 = emulation.ValueStack.CallStack.Pop();
                emulation.ValueStack.CallStack.Push(stack1.Target);
                args.bypassCall = true;
            }
            else if (args.Instruction.Operand is MethodDef&&((MethodDef)args.Instruction.Operand).IsNative)
            {

                var stack1 = emulation.ValueStack.CallStack.Pop();
                X86Method x86 = new X86Method(args.Instruction.Operand as MethodDef);
                var abc = x86.Execute(stack1);
                emulation.ValueStack.CallStack.Push(abc);
                args.bypassCall = true;
            }
            //
        }

        private static bool isDecryptMethod(MethodDef methods)
        {
            if (methods.ReturnType.ToString().Contains("System.Runtime.InteropServices.GCHandle"))
                if (methods.Parameters.Count == 2)
                    return true;

            return false;
        }

        private static bool IsUintDecyption(MethodDef methods)
        {
            if (methods.ReturnType.IsGenericParameter)
                if (methods.ReturnType.IsGenericMethodParameter)
                    return true;

            return false;
        }

        public static byte[] LzmaDecompress(byte[] bytes)
        {
            return Lzma.Decompress(bytes);
        }

        private static bool isDecompressMethod(MethodDef methods)
        {
            if (!methods.ReturnType.IsSZArray || methods.ReturnType.Next == null ||
                methods.ReturnType.Next != methods.Module.CorLibTypes.Byte) return false;
            return methods.Parameters.Count == 1;
        }
    }
}
