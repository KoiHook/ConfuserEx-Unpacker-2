using CawkEmulatorV4;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;

using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConfuserEx_Unpacker.Protections.Compressor
{
    internal class Remover : Base
    {
        
        public static int ModuleEp;

        public static byte[] ModuleBytes { get; set; }

        public override void Deobfuscate()
        {
            StepOne(ModuleDef);
        }

        private void StepOne(ModuleDefMD module)
        {
            var ep = module.EntryPoint;
            var emulator = new Emulation(ep);
            emulator.OnCallPrepared += (emulation, args) =>
            {
                var instr = args.Instruction;
                Utils.HandleCall(args, emulation);
            };
            emulator.OnInstructionPrepared += (emulation, args) =>
            {
                if (args.Instruction.OpCode == OpCodes.Ldftn)
                {
                    emulation.ValueStack.CallStack.Push(null);
                    args.Cancel = true;
                }
            };
            emulator.Emulate();
            if (ModuleBytes == null)
            {
                Base.CompressorRemoved = false;
                return;
            }

            Protections.Base.ModuleDef = ModuleDefMD.Load(ModuleBytes);
            Protections.Base.ModuleDef.EntryPoint = Protections.Base.ModuleDef.ResolveToken(ModuleEp) as MethodDef;
            Base.CompressorRemoved = true;
        }




        public static GCHandle HandleDecryptMethod(Emulation mainEmulation, CallEventArgs mainArgs, MethodDef decryptionMethod)
        {
            var decEmulation = new Emulation(decryptionMethod);
            decEmulation.OnCallPrepared += (emulation, args) => { Utils.HandleCall(args, emulation); };
            decEmulation.ValueStack.Parameters[decryptionMethod.Parameters[1]] = (uint)
                mainEmulation.ValueStack.CallStack.Pop();
            decEmulation.ValueStack.Parameters[decryptionMethod.Parameters[0]] =
                mainEmulation.ValueStack.CallStack.Pop();
            decEmulation.Emulate();
            return (GCHandle)decEmulation.ValueStack.CallStack.Pop();
        }


    }
}
