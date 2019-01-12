using de4dot.blocks;
using dnlib.DotNet;
using System.Collections.Generic;
using dnlib.DotNet.Emit;
using de4dot.blocks.cflow;
using System.Linq;
using System;
using ConfuserDeobfuscator.Engine.Routines.Ex.x86;
using Tuple = System.Tuple;

namespace ConfuserEx_Unpacker.Protections.RefProxy
{
    public class DelegateFixer
    {
        public ModuleDef Module { get; set; }
        List<DelegateInitInfo> delegateInfos;
        List<TypeDef> delegates = new List<TypeDef>();
        List<MethodDef> proxies = new List<MethodDef>();
        List<TypeDef> delegateAttributes = new List<TypeDef>();
        List<MethodDef> delegateInitializers = new List<MethodDef>();
        List<FieldDef> delegateFields = new List<FieldDef>();
        public DelegateFixer(ModuleDef module, List<DelegateInitInfo> infos)
        {
            Module = module;
            delegateInfos = infos;
            if (delegateInfos == null)
                delegateInfos = new List<DelegateInitInfo>();
            foreach (var info in delegateInfos)
                if (info.Field.FieldSig.Type.TryGetTypeDef() is TypeDef deleg && !delegates.Contains(deleg))
                    delegates.Add(deleg);
            foreach (var info in delegateInfos)
                if (!delegateFields.Contains(info.Field))
                    delegateFields.Add(info.Field);
            foreach (var info in delegateInfos)
                if (!delegateAttributes.Contains((TypeDef)info.Field.CustomAttributes.FirstOrDefault().AttributeType))
                    delegateAttributes.Add((TypeDef)info.Field.CustomAttributes.FirstOrDefault().AttributeType);
            foreach (var info in delegateInfos)
                if (!delegateInitializers.Contains(info.InitMethod))
                    delegateInitializers.Add(info.InitMethod);
        }
        public int Decrypted = 0;
        public void DecryptMethods()
        {
            for (int i = 0; i < delegateInfos.Count; i++)
            {
                var info = delegateInfos[i];
                var emuator = new InstructionEmulator();
                EmulateMethod(info, emuator);
                var local = info.InitMethod.Body.Variables.FirstOrDefault(t => t.Type.FullName == "System.Reflection.MethodBase");
                if (local == null)
                    continue;
                var value = emuator.GetLocal(local);
                if(!(value is ObjectValue objValue) || !(objValue.obj is IMethod method))
                    continue;
                info.Resolved = true;
                info.Decrypted = method;
                info.OpCode = ResolveOpCode(info.InitMethod, info.Field, info.Key);
                Decrypted++;
            }
        }
        OpCode ResolveOpCode(MethodDef method, FieldDef field, byte key)
        {
            var index = GetOpCodeIndex(method);
            if (!index.HasValue)
                return null;
            var fieldName = field.Name.String;
            short value = (short)(fieldName[index.Value] ^ key);
            if (value == OpCodes.Call.Value)
                return OpCodes.Call;
            else if (value == OpCodes.Callvirt.Value)
                return OpCodes.Callvirt;
            else if (value == OpCodes.Newobj.Value)
                return OpCodes.Newobj;
            return null;
        }
        int? GetOpCodeIndex(MethodDef method)
        {
            var instr = method.Body.Instructions;
            for (int i = 0; i < instr.Count - 6; i++)
            {
                if (!IsGetName(instr, i, out int index))
                    continue;
                if (instr[i + 4].OpCode != OpCodes.Conv_U1 && instr[i + 4].OpCode != OpCodes.Conv_I1)
                    continue;
                if (!instr[i + 5].IsLdarg())
                    continue;
                if (instr[i + 6].OpCode != OpCodes.Xor)
                    continue;
                return index;
            }
            return null;
        }
        bool IsGetName(IList<Instruction> instr, int i, out int index)
        {
            index = 0;
            if (instr[i + 1].OpCode != OpCodes.Callvirt)
                return false;
            if (!(instr[i + 1].Operand is MemberRef mref1) || mref1.FullName != "System.String System.Reflection.MemberInfo::get_Name()")
                return false;
            if (!instr[i + 2].IsLdcI4())
                return false;
            index = instr[i + 2].GetLdcI4Value();
            if (instr[i + 3].OpCode != OpCodes.Callvirt)
                return false;
            if (!(instr[i + 3].Operand is MemberRef mref2) || mref2.FullName != "System.Char System.String::get_Chars(System.Int32)")
                return false;
            return true;
        }
        private void EmulateMethod(DelegateInitInfo info, InstructionEmulator emulator)
        {
            var blocks = new Blocks(info.InitMethod);
            var allBlocks = blocks.MethodBlocks.GetAllBlocks();
            emulator.Initialize(blocks, true);
            emulator.SetArg(0, new ObjectValue(info.Field));
            emulator.SetArg(1, new Int32Value(info.Key));
            EmulatoeBlock(emulator, allBlocks[0]);
        }
        private void EmulateBrantchBlock(InstructionEmulator emulator, Block block)
        {
            if (block == null)
                return;
            var instr = block.Instructions;
            var isSwitch = block.LastInstr.OpCode == OpCodes.Switch;
            for (int i = 0; i < instr.Count - (isSwitch ? 1 : 0); i++)
            {
                if (instr[i].OpCode == OpCodes.Call && instr[i].Operand is MethodDef nativeMethod && IsNativeMethod(nativeMethod))
                    EmulateNativeMethod(emulator, nativeMethod);
                else if (instr[i].Instruction.OpCode == OpCodes.Stfld)
                    continue;
                else
                    emulator.Emulate(instr[i].Instruction);
            }

            if (isSwitch)
            {
                var value = GetInt32Value(emulator.Pop());
                if (value == null)
                    return;
                if (value < 0 || value >= block.Targets.Count)
                    EmulateBrantchBlock(emulator, block.FallThrough);
                else
                    EmulateBrantchBlock(emulator, block.Targets[value.Value]);
                return;
            }
            if (block.IsConditionalBranch() || (block.Targets != null && block.Targets.Count != 0))
                return;
            EmulateBrantchBlock(emulator, block.FallThrough);
        }
        private void EmulatoeBlock(InstructionEmulator emulator, Block block)
        {
            if (block == null)
                return;
            var instr = block.Instructions;
            var isSwitch = block.LastInstr.OpCode == OpCodes.Switch;
            var isBrantch = block.LastInstr.IsConditionalBranch();
            for (int i = 0; i < instr.Count - ((isSwitch || isBrantch) ? 1 : 0); i++)
            {
                if (IsFieldFromHandle(instr[i]))
                    EmulateFieldFromHandle(emulator);
                else if (IsGetModule(instr[i]))
                    EmulateGetModule(emulator);
                else if (IsGetMDToken(instr[i]))
                    EmulateIsGetMDToken(emulator);
                else if (IsResolveSignature(instr[i]))
                    EmulateIsResolveSignature(emulator);
                else if (instr[i].OpCode == OpCodes.Ldlen)
                    EmulateLdLen(emulator);
                else if (IsGetOptionalCustomModifiers(instr[i]))
                    EmulateIsGetOptionalCustomModifiers(emulator);
                else if (instr[i].OpCode == OpCodes.Ldelem_Ref)
                    EmulateLdLelem_Ref(emulator);
                else if (IsGetName(instr[i]))
                    EmulateIsGetName(emulator);
                else if (IsGetChars(instr[i]))
                    EmulateIsGetChars(emulator);
                else if (instr[i].OpCode == OpCodes.Ldelem_I1 || instr[i].OpCode == OpCodes.Ldelem_U1)
                    EmulateLdelem8(emulator);
                else if (instr[i].OpCode == OpCodes.Call && instr[i].Operand is MethodDef nativeMethod && IsNativeMethod(nativeMethod))
                    EmulateNativeMethod(emulator, nativeMethod);
                else if (IsGetCustomAttributes(instr[i]))
                    EmulateIsGetCustomAttributes(emulator);
                else if (IsGetHashCode(instr[i]))
                    EmulateIsGetHashCode(emulator);
                else if (IsResolveMethod(instr[i]))
                    EmulateIsResolveMethod(emulator);
                else if (IsConvertFromBase64(instr[i]))
                    EmulateConvertFromBase64(emulator);
                else if (IsGetUTF8(instr[i]))
                    EmulateGetUTF8(emulator);
                else if (IsGetString(instr[i]))
                    EmulateGetString(emulator);
                else if(isOkInstruction(instr[i].Instruction))
                    emulator.Emulate(instr[i].Instruction);
                else
                    emulator.Emulate(instr[i].Instruction);
            }
            if(isSwitch)
            {
                var value = GetInt32Value(emulator.Pop());
                if (value == null)
                    return;
                if(value < 0 || value >= block.Targets.Count)
                    EmulatoeBlock(emulator, block.FallThrough);
                else
                    EmulatoeBlock(emulator, block.Targets[value.Value]);
                return;
            }
            if(isBrantch)
            {
                if (instr.Count > 1 && instr[instr.Count - 2].Operand.ToString().Contains("get_IsStatic()"))
                    return;

                var value = GetInt32Value(emulator.Pop());
                if (value == null)
                    return;
                //if (value < 0 || value >= block.Targets.Count)
                //    EmulatoeBlock(emulator, block.FallThrough);
                //else
                //    EmulatoeBlock(emulator, block.Targets[value.Value]);
                //TODO: Support brantching
                return;
            }
            if ((block.Targets != null && block.Targets.Count != 0))
                return;
            EmulatoeBlock(emulator, block.FallThrough);
        }
        #region Emulating


        bool isOkInstruction(Instruction instr)
        {
            switch (instr.OpCode.Code)
            {
                case Code.Starg:
                case Code.Starg_S:
                case Code.Stloc:
                case Code.Stloc_S:
                case Code.Stloc_0:
                case Code.Stloc_1:
                case Code.Stloc_2:
                case Code.Stloc_3:

                case Code.Ldarg:
                case Code.Ldarg_S:
                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:

                case Code.Ldarga:
                case Code.Ldarga_S:
                case Code.Ldloca:
                case Code.Ldloca_S:

                case Code.Dup:

                case Code.Ldc_I4:
                case Code.Ldc_I4_S:
                case Code.Ldc_I8:
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8:
                case Code.Ldc_I4_M1:
                case Code.Ldnull:
                case Code.Ldstr:
                case Code.Box:

                case Code.Conv_U1:
                case Code.Conv_U2:
                case Code.Conv_U4:
                case Code.Conv_U8:
                case Code.Conv_I1:
                case Code.Conv_I2:
                case Code.Conv_I4:
                case Code.Conv_I8:
                case Code.Add:
                case Code.Sub:
                case Code.Mul:
                case Code.Div:
                case Code.Div_Un:
                case Code.Rem:
                case Code.Rem_Un:
                case Code.Neg:
                case Code.And:
                case Code.Or:
                case Code.Xor:
                case Code.Not:
                case Code.Shl:
                case Code.Shr:
                case Code.Shr_Un:
                case Code.Ceq:
                case Code.Cgt:
                case Code.Cgt_Un:
                case Code.Clt:
                case Code.Clt_Un:
                case Code.Unbox_Any:

                case Code.Call:
                case Code.Callvirt:

                case Code.Castclass:
                case Code.Isinst:

                case Code.Add_Ovf:
                case Code.Add_Ovf_Un:
                case Code.Sub_Ovf:
                case Code.Sub_Ovf_Un:
                case Code.Mul_Ovf:
                case Code.Mul_Ovf_Un:

                case Code.Conv_Ovf_I1:
                case Code.Conv_Ovf_I1_Un:
                case Code.Conv_Ovf_I2:
                case Code.Conv_Ovf_I2_Un:
                case Code.Conv_Ovf_I4:
                case Code.Conv_Ovf_I4_Un:
                case Code.Conv_Ovf_I8:
                case Code.Conv_Ovf_I8_Un:
                case Code.Conv_Ovf_U1:
                case Code.Conv_Ovf_U1_Un:
                case Code.Conv_Ovf_U2:
                case Code.Conv_Ovf_U2_Un:
                case Code.Conv_Ovf_U4:
                case Code.Conv_Ovf_U4_Un:
                case Code.Conv_Ovf_U8:
                case Code.Conv_Ovf_U8_Un:

                case Code.Ldelem_I1:
                case Code.Ldelem_I2:
                case Code.Ldelem_I4:
                case Code.Ldelem_I8:
                case Code.Ldelem_U1:
                case Code.Ldelem_U2:
                case Code.Ldelem_U4:
                case Code.Ldelem:

                case Code.Ldind_I1:
                case Code.Ldind_I2:
                case Code.Ldind_I4:
                case Code.Ldind_I8:
                case Code.Ldind_U1:
                case Code.Ldind_U2:
                case Code.Ldind_U4:

                case Code.Ldlen:
                case Code.Sizeof:

                case Code.Ldfld:
                case Code.Ldsfld:

                case Code.Ldftn:
                case Code.Ldsflda:
                case Code.Ldtoken:
                case Code.Ldvirtftn:
                case Code.Ldflda:

                case Code.Unbox:

                case Code.Conv_R_Un:
                case Code.Conv_R4:
                case Code.Conv_R8:
                    return true;
            }
            return false;
        }

        private bool IsFieldFromHandle(Instr instr) => instr.OpCode == OpCodes.Call && instr.Operand is MemberRef method &&
            method.FullName == "System.Reflection.FieldInfo System.Reflection.FieldInfo::GetFieldFromHandle(System.RuntimeFieldHandle)";
        private void EmulateFieldFromHandle(InstructionEmulator emulator)
        {
            var fieldValue = emulator.Pop();
            if (fieldValue is ObjectValue objValue && objValue.obj is FieldDef field)
            {
                emulator.Push(new ObjectValue(field));
            }
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsGetModule(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
          method.FullName == "System.Reflection.Module System.Reflection.MemberInfo::get_Module()";
        private void EmulateGetModule(InstructionEmulator emulator)
        {
            var fieldValue = emulator.Pop();
            if (fieldValue is ObjectValue objValue && objValue.obj is FieldDef field)
                emulator.Push(new ObjectValue((ModuleDefMD)field.Module));
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsGetMDToken(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
        method.FullName == "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()";
        private void EmulateIsGetMDToken(InstructionEmulator emulator)
        {
            var fieldValue = emulator.Pop();
            if (fieldValue is ObjectValue objValue && objValue.obj is FieldDef field)
                emulator.Push(new Int32Value(field.MDToken.ToInt32()));
            else if (fieldValue is ObjectValue objValue2 && objValue2.obj is ITypeDefOrRef type)
                emulator.Push(new Int32Value(type.MDToken.ToInt32()));
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsResolveSignature(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
        method.FullName == "System.Byte[] System.Reflection.Module::ResolveSignature(System.Int32)";
        private void EmulateIsResolveSignature(InstructionEmulator emulator)
        {
            var mdTokenValue = emulator.Pop();
            var moduleValue = emulator.Pop();
            if (moduleValue is ObjectValue objValue && objValue.obj is ModuleDefMD module && mdTokenValue is Int32Value mdtoke && mdtoke.AllBitsValid())
            {
                if (!module.TablesStream.TryReadFieldRow(new MDToken(mdtoke.Value).Rid, out var fieldData))
                {
                    emulator.Push(new UnknownValue());
                    return;
                }
                var data = module.BlobStream.Read(fieldData.Signature);
                if (data == null)
                {
                    emulator.Push(new UnknownValue());
                    return;
                }
                emulator.Push(new ObjectValue(data));
            }
            else
                emulator.Push(new UnknownValue());
        }
        private void EmulateLdLen(InstructionEmulator emulator)
        {
            var value = emulator.Pop();
            if (value is StringValue strValue)
                emulator.Push(new Int32Value(strValue.value.Length));
            else if (value is ObjectValue objValue && objValue.obj is System.Array array)
                emulator.Push(new Int32Value(array.Length));
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsGetOptionalCustomModifiers(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
         method.FullName == "System.Type[] System.Reflection.FieldInfo::GetOptionalCustomModifiers()";
        private void EmulateIsGetOptionalCustomModifiers(InstructionEmulator emulator)
        {
            var fieldValue = emulator.Pop();
            if (fieldValue is ObjectValue objValue && objValue.obj is FieldDef field)
            {
                if (!(field.FieldSig.Type is CModOptSig fieldSig)) //CModOptSig ModifierSig
                {
                    emulator.Push(new UnknownValue());
                    return;
                }

                if (fieldSig.Modifier == null)
                {
                    int d = 0;
                }
                emulator.Push(new ObjectValue(new ITypeDefOrRef[] { fieldSig.Modifier }));
            }
            else
                emulator.Push(new UnknownValue());
        }
        private void EmulateLdLelem_Ref(InstructionEmulator emulator)
        {
            var indexValue = emulator.Pop();
            var value = emulator.Pop();
            if (value is ObjectValue objValue && objValue.obj is object[] types && indexValue is Int32Value intVal && intVal.AllBitsValid() && intVal.Value >= 0 && intVal.Value < types.Length)
            {
                if(types[intVal.Value] == null)
                {
                    int d = 0;
                }
                emulator.Push(new ObjectValue(types[intVal.Value]));
            }
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsGetName(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
          method.FullName == "System.String System.Reflection.MemberInfo::get_Name()";
        private void EmulateIsGetName(InstructionEmulator emulator)
        {
            var fieldValue = emulator.Pop();
            if (fieldValue is ObjectValue objValue && objValue.obj is FieldDef field)
                emulator.Push(new StringValue(field.Name));
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsGetChars(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
          method.FullName == "System.Char System.String::get_Chars(System.Int32)";
        private void EmulateIsGetChars(InstructionEmulator emulator)
        {
            var index = emulator.Pop();
            var strValue = emulator.Pop();
            if (strValue is StringValue strVal && index is Int32Value intVal && intVal.AllBitsValid() && intVal.Value >= 0 && intVal.Value < strVal.value.Length)
                emulator.Push(new Int32Value((int)strVal.value[intVal.Value]));
            else
                emulator.Push(new UnknownValue());
        }
        private void EmulateLdelem8(InstructionEmulator emulator)
        {
            var indexValue = emulator.Pop();
            var value = emulator.Pop();
            if (value is ObjectValue objValue && objValue.obj is byte[] array && indexValue is Int32Value intVal && intVal.AllBitsValid() && intVal.Value >= 0 && intVal.Value < array.Length)
                emulator.Push(new Int32Value((int)array[intVal.Value]));
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsNativeMethod(MethodDef method)
        {
            if (method == null || !method.IsStatic || !method.IsNative)
                return false;
            if (method.ReturnType.ElementType != ElementType.I4)
                return false;
            if (method.Parameters == null || method.Parameters.Count != 1)
                return false;
            if (method.Parameters[0].Type.ElementType != ElementType.I4)
                return false;
            return true;
        }

        Dictionary<MethodDef, X86Method> cashedNativeMethod = new Dictionary<MethodDef, X86Method>();
        private void EmulateNativeMethod(InstructionEmulator emulator, MethodDef method)
        {
            var keyValue = emulator.Pop();
            if (keyValue is Int32Value intValue && intValue.AllBitsValid())
            {
                X86Method x86;
                if (cashedNativeMethod.ContainsKey(method))
                    x86 = cashedNativeMethod[method];
                else
                {
                    x86 = new X86Method(method);
                    cashedNativeMethod[method] = x86;
                }
                emulator.Push(new Int32Value(x86.Execute(intValue.Value)));
            }
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsGetCustomAttributes(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
          method.FullName == "System.Object[] System.Reflection.MemberInfo::GetCustomAttributes(System.Boolean)";
        private void EmulateIsGetCustomAttributes(InstructionEmulator emulator)
        {
            var someVal = emulator.Pop();
            var fieldValue = emulator.Pop();
            if (fieldValue is ObjectValue objValue && objValue.obj is FieldDef field)
                emulator.Push(new ObjectValue(field.CustomAttributes.ToArray<object>()));
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsGetHashCode(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
         method.FullName == "System.Int32 System.Object::GetHashCode()";
        private void EmulateIsGetHashCode(InstructionEmulator emulator)
        {
            var obj = emulator.Pop();
            if (obj is ObjectValue objValue && objValue.obj is CustomAttribute attribute)
            {
                if (!(attribute.AttributeType.ResolveTypeDef() is TypeDef caType))
                {
                    emulator.Push(new UnknownValue());
                    return;
                }
                if (attribute.ConstructorArguments == null || attribute.ConstructorArguments.Count < 1 || !(attribute.ConstructorArguments[0].Value is int caValue))
                {
                    emulator.Push(new UnknownValue());
                    return;
                }
                var cctor = new List<MethodDef>(caType.FindConstructors())[0];
                if (!IsSignatureCctor(cctor, caValue, out int sigValue))
                {
                    emulator.Push(new UnknownValue());
                    return;
                }
                emulator.Push(new Int32Value(sigValue.GetHashCode()));
            }
            else
                emulator.Push(new UnknownValue());
        }
        private bool IsSignatureCctor(MethodDef method, int argValue, out int value)
        {
            value = 0;
            if (method == null || !method.HasBody || !method.HasThis)
                return false;
            if (method.ReturnType.ElementType != ElementType.Void)
                return false;
            var parameters = method.Parameters;
            if (parameters == null || parameters.Count != 2)
                return false;
            if (parameters[1].Type.ElementType != ElementType.I4 && parameters[1].Type.ElementType != ElementType.U4)
                return false;
            var instr = method.Body.Instructions;
            if (instr.Count < 3)
                return false;
            int l = instr.Count;
            //if (instr[l - 2].OpCode != OpCodes.Stfld)
            //    return false;
            //if (instr[l - 1].OpCode != OpCodes.Ret)
            //    return false;
            var emulator = new InstructionEmulator();
            var blocks = new Blocks(method);
            var allBlocks = blocks.MethodBlocks.GetAllBlocks();
            emulator.Initialize(blocks, true);
            emulator.SetArg(parameters[1], new Int32Value(argValue));
            EmulateBrantchBlock(emulator, allBlocks[0]);
            //for (int i = 0; i < l - 2; i++)
            //    emulator.Emulate(instr[i]);
            var retValue = emulator.Pop();
            var intVal = GetInt32Value(retValue, false);
            if (!intVal.HasValue)
                return false;
            value = intVal.Value;
            return true;
        }
        public static int? GetInt32Value(Value value, bool checkBits = true)
        {
            if (value == null || !value.IsInt32())
                return null;
            var intValue = value as Int32Value;
            if (intValue == null || (checkBits && !intValue.AllBitsValid()))
                return null;
            return intValue.Value;
        }
        private bool IsResolveMethod(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
         method.FullName == "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)";

        //60	0090	call	uint8[] [mscorlib]System.Convert::FromBase64String(string)
        public int resolvedTimes = 0;
        private void EmulateIsResolveMethod(InstructionEmulator emulator)
        {
            var mdTokenValue = emulator.Pop();
            var moduleValue = emulator.Pop();
            if (moduleValue is ObjectValue objValue && objValue.obj is ModuleDefMD module && mdTokenValue is Int32Value mdtoke && mdtoke.AllBitsValid())
            {
                var tok = module.ResolveToken(mdtoke.Value);
                if (tok is IMethod method)
                {
                    resolvedTimes++;
                    emulator.Push(new ObjectValue(method));
                    //Console.WriteLine("{0}:  Resolved Token: {1}", ++a,tok);
                }
                else
                    emulator.Push(new UnknownValue());
            }
            else
                emulator.Push(new UnknownValue());
        }

        private bool IsConvertFromBase64(Instr instr) => instr.OpCode == OpCodes.Call && instr.Operand is MemberRef method &&
             method.FullName == "System.Byte[] System.Convert::FromBase64String(System.String)";
        private void EmulateConvertFromBase64(InstructionEmulator emulator)
        {
            var fieldValue = emulator.Pop();
            if (fieldValue is StringValue strValue && strValue.value != null)
                emulator.Push(new ObjectValue(System.Convert.FromBase64String(strValue.value)));
            else
                emulator.Push(new UnknownValue());
        }
        //79	00B0	call	class [mscorlib]System.Text.Encoding [mscorlib]System.Text.Encoding::get_UTF8()
        private bool IsGetUTF8(Instr instr) => instr.OpCode == OpCodes.Call && instr.Operand is MemberRef method &&
         method.FullName == "System.Text.Encoding System.Text.Encoding::get_UTF8()";
        private void EmulateGetUTF8(InstructionEmulator emulator)
        {
            emulator.Push(new ObjectValue(System.Text.Encoding.UTF8));
        }

        private bool IsGetString(Instr instr) => instr.OpCode == OpCodes.Callvirt && instr.Operand is MemberRef method &&
       method.FullName == "System.String System.Text.Encoding::GetString(System.Byte[])";
        private void EmulateGetString(InstructionEmulator emulator)
        {
            var dataVal = emulator.Pop();
            var encodingVal = emulator.Pop();
            if (encodingVal is ObjectValue objValue && objValue.obj is System.Text.Encoding encoding && dataVal is ObjectValue objVal2 && objVal2.obj is byte[] data && data != null)
            {
                emulator.Push(new StringValue(encoding.GetString(data)));
            }
            else
                emulator.Push(new UnknownValue());
        }

        #endregion

        public void Fix()
        {
            long fixedCalls = 0;
            long failed = 0;
            foreach (var type in Module.GetTypes())
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;
                    var blocks = new Blocks(method);
                    var allBlocks = blocks.MethodBlocks.GetAllBlocks();
                    var emulator = new InstructionEmulator();
                    emulator.Initialize(blocks, false);
                    foreach (var block in allBlocks)
                    {
                        //emulator.ClearStack();
                        var instr = block.Instructions;
                        for (int i = 0; i < instr.Count; i++)
                        {
                            if (instr[i].OpCode == OpCodes.Ldsfld && instr[i].Operand is FieldDef field && delegateInfos.Any(d => d.Field == field))
                            {
                                var info = delegateInfos.Find(d => d.Field == field);
                                var tuple = System.Tuple.Create<DelegateInitInfo, System.Tuple<Block, int>>(info, Tuple.Create(block,i)) /*{ Item1 = info, Item2 = new Tuple<Block, int>() { Item1 = block, Item2 = i } }*/;
                                emulator.Push(new ObjectValue(tuple));
                            }
                            else if(instr[i].OpCode == OpCodes.Call && instr[i].Operand is MethodDef invokeMethod&& invokeMethod.Name == "Invoke" && DotNetUtils.DerivesFromDelegate(invokeMethod.DeclaringType))
                            {
                                var paramss = invokeMethod.Parameters;
                                instr[i].Instruction.CalculateStackUsage(out int pushes, out int pops);
                                List<object> kurac = new List<object>();
                                for (int j = 0; j < pops - 1; j++)
                                    kurac.Add(emulator.Pop());
                                var val = emulator.Pop();
                                if(!(val is ObjectValue objVal) || !(objVal.obj is System.Tuple<DelegateInitInfo, System.Tuple<Block, int>> info) || info.Item1.Field.FieldType.TryGetTypeDef() != invokeMethod.DeclaringType )
                                {
                                    for (int j = 0; j < pushes; j++)
                                        emulator.Push(new UnknownValue());
                                    failed++;
                                    continue;
                                }
                                if(info.Item1.Decrypted == null || info.Item1.OpCode == null)
                                {
                                    for (int j = 0; j < pushes; j++)
                                        emulator.Push(new UnknownValue());
                                    failed++;
                                    continue;
                                }
                                instr[i]= new Instr( info.Item1.OpCode.ToInstruction(info.Item1.Decrypted));
                                info.Item2.Item1.Instructions[info.Item2.Item2] = new Instr(OpCodes.Nop.ToInstruction());
                                fixedCalls++;
                                for (int j = 0; j < pushes; j++)
                                    emulator.Push(new UnknownValue());
                            }
                            else
                                emulator.Emulate(instr[i].Instruction);
                        }
                    }
                    blocks.GetCode(out var instructions, out var exceptions);
                    DotNetUtils.RestoreBody(method, instructions, exceptions);
                }
            if (fixedCalls != 0)
                Console.WriteLine("Fixed delegate calls: {0}", fixedCalls);
            if (failed != 0)
                Console.WriteLine("Failed to fix delegate calls: {0}");
        }
        public void FixProxyCalls()
        {
            long fixedCalls = 0;
            long failed = 0;
            foreach (var type in Module.GetTypes())
            {
                if (delegates.Contains(type))
                    continue;
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;
                    var instr = method.Body.Instructions;
                    for (int i = 0; i < instr.Count; i++)
                    {
                        if (instr[i].OpCode != OpCodes.Call)
                            continue;
                        if (!(instr[i].Operand is MethodDef proxy))
                            continue;
                        if (!IsProxy(proxy, out var code, out var operand, out var len) && !IsProxy2(proxy, out code, out operand, out len))
                            continue;
                        fixedCalls++;
                        if (proxy.DeclaringType == method.DeclaringType)
                        {
                            instr[i].OpCode = code;
                            instr[i].Operand = operand;
                            if (!proxies.Contains(proxy))
                                proxies.Add(proxy);
                        }
                        else if (delegates.Contains(proxy.DeclaringType))
                        {
                            if (code != OpCodes.Call || !(operand is MethodDef proxy2))
                            {
                                instr[i].OpCode = code;
                                instr[i].Operand = operand;
                            }
                            else if (!IsProxy(proxy2, out var code2, out var operand2, out var len2) || len != len2)
                            {
                                instr[i].OpCode = code;
                                instr[i].Operand = operand;
                            }
                            else
                            {
                                instr[i].OpCode = code2;
                                instr[i].Operand = operand2;
                                if (!proxies.Contains(proxy2))
                                    proxies.Add(proxy2);
                            }
                            if (!proxies.Contains(proxy))
                                proxies.Add(proxy);
                        }
                        else
                        {
                            instr[i].OpCode = code;
                            instr[i].Operand = operand;
                            if (!proxies.Contains(proxy))
                                proxies.Add(proxy);
                        }
                    }
                }
            }
            if (fixedCalls != 0)
                Console.WriteLine("Fixed proxy calls: {0}", fixedCalls);
            if (failed != 0)
                Console.WriteLine("Failed to fix proxy calls: {0}", failed);
        }
        private bool IsProxy(MethodDef method,out OpCode code, out object operand,out int len)
        {
            code = null;
            operand = null;
            len = 0;
            if (!method.HasBody)
                return false;
            if (!method.IsStatic)
                return false;
            var instr = method.Body.Instructions;
            var l = instr.Count - 1;
            if (l < 1)
                return false;
            if (instr[l].OpCode != OpCodes.Ret)
                return false;
            switch (instr[l-1].OpCode.Code)
            {
                case Code.Call:
                case Code.Callvirt:
                case Code.Newobj:
                    break;
                default:
                    return false;
            }
            code = instr[l - 1].OpCode;
            operand = instr[l - 1].Operand;
            len = instr.Where(i => i.OpCode != OpCodes.Nop).Count() - 2;
            if (len != method.Parameters.Count)
                return false;
            int paramLen = 0;
            for (int i = 0; i < instr.Count - 2; i++)
            {
                if (instr[i].OpCode == OpCodes.Nop)
                    continue;
                if (!instr[i].IsLdarg())
                    return false;
                var index = instr[i].GetParameterIndex();
                if (index != paramLen)
                    return false;
                paramLen++;
            }
            return len == paramLen;
        }

        private bool IsProxy2(MethodDef method, out OpCode code, out object operand, out int len)
        {
            code = null;
            operand = null;
            len = 0;
            if (!method.HasBody)
                return false;
            if (!method.IsInternalCall)
                return false;
            var instr = method.Body.Instructions;
            var l = instr.Count - 1;
            if (l < 1)
                return false;
            if (instr[l].OpCode != OpCodes.Ret)
                return false;
            switch (instr[l - 1].OpCode.Code)
            {
                case Code.Ldfld:
                    break;
                default:
                    return false;
            }
            code = instr[l - 1].OpCode;
            operand = instr[l - 1].Operand;
            len = instr.Where(i => i.OpCode != OpCodes.Nop).Count() - 2;
            if (len != 1)
                return false;
            if (len != method.Parameters.Count - 1)
                return false;
            //int paramLen = 0;
            //for (int i = 0; i < instr.Count - 2; i++)
            //{
            //    if (instr[i].OpCode == OpCodes.Nop)
            //        continue;
            //    if (!instr[i].IsLdarg())
            //        return false;
            //    var index = instr[i].GetParameterIndex();
            //    if (index != paramLen)
            //        return false;
            //    paramLen++;
            //}
            //return len == paramLen;
            return true;
        }

        public void RemoveDelegateJunk()
        {
            if (delegateInfos.Any(i => !i.Resolved))
                return;
            var md = Module as ModuleDefMD;

            foreach(var field in delegateFields)
            {
                field.DeclaringType.Fields.Remove(field);
            }

            foreach (var type in delegates)
            {
                if (type.DeclaringType != null)
                    type.DeclaringType.NestedTypes.Remove(type);
                else
                    md.Types.Remove(type);
            }

            foreach (var type in delegateAttributes)
            {
                if (type.DeclaringType != null)
                    type.DeclaringType.NestedTypes.Remove(type);
                else
                    md.Types.Remove(type);
            }




            foreach(var type in Module.GetTypes())
            {
                foreach(var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;
                    foreach(var instr in method.Body.Instructions)
                    {
                        if (instr.OpCode.OperandType != OperandType.InlineMethod)
                            continue;
                        if (!(instr.Operand is MethodDef calledMethod))
                            continue;
                        //if (proxies.Contains(calledMethod))
                        //    proxies.Remove(calledMethod);
                        if (cashedNativeMethod.ContainsKey(calledMethod))
                            cashedNativeMethod.Remove(calledMethod);
                        if (delegateInitializers.Contains(calledMethod))
                            delegateInitializers.Remove(calledMethod);

                    }
                }
            }

            //foreach (var method in proxies)
            //    method.DeclaringType.Remove(method);

            foreach(var info in delegateInfos)
            {
                for (int i = info.InitalizedAt -2; i <= info.InitalizedAt; i++)
                {
                    info.Method.Body.Instructions[i].OpCode = OpCodes.Nop;
                }
            }

            foreach(var method in cashedNativeMethod.Keys)
                method.DeclaringType.Remove(method);

            foreach (var method in delegateInitializers)
                method.DeclaringType.Remove(method);

            //Console.WriteLine("Removed delegate fields: {0}", delegateFields.Count, Color.OrangeRed);
            //Console.WriteLine("Removed delegate types: {0}", delegates.Count, Color.OrangeRed);
            //Console.WriteLine("Removed delegate attributes: {0}", delegateAttributes.Count, Color.OrangeRed);
            //Console.WriteLine("Removed delegate initializers: {0}", delegateInitializers.Count, Color.OrangeRed);
            //Console.WriteLine("Removed native methods: {0}", x86Emulator.NativeMethods.Count, Color.OrangeRed);
            //Console.WriteLine("Removed proxy methods: {0}", proxies.Count, Color.OrangeRed);
        }


        public void RemoveProxyJunk()
        {
            foreach (var type in Module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.OpCode.OperandType != OperandType.InlineMethod)
                            continue;
                        if (!(instr.Operand is MethodDef calledMethod))
                            continue;
                        if (proxies.Contains(calledMethod))
                            proxies.Remove(calledMethod);

                    }
                }
            }

            foreach (var method in proxies)
                method.DeclaringType.Remove(method);
        }
    }
}
