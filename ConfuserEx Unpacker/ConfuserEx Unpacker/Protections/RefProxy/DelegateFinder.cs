using de4dot.blocks;
using dnlib.DotNet;
using System.Collections.Generic;
using dnlib.DotNet.Emit;

namespace ConfuserEx_Unpacker.Protections.RefProxy
{
    public class DelegateFinder
    {
        public ModuleDef Module { get; set; }
        public List<MethodDef> Methods { get; set; }
        public List<DelegateInitInfo> Initializations { get; set; }
        public DelegateFinder(ModuleDef module)
        {
            Module = module;
        }
        public void FindMethods()
        {
            var moduleType = DotNetUtils.GetModuleType(Module);
            if (moduleType == null)
                return;
            var delegateMethods = new List<MethodDef>();
            foreach (var method in moduleType.Methods)
            {
                if (!IsDelegateMethod(method))
                    continue;
                delegateMethods.Add(method);
                //Console.WriteLine("Found Delegate Method {0}", method.Name);
            }
            Methods = delegateMethods;
        }
        public void FindFields()
        {
            if (Methods == null || Methods.Count == 0)
                return;
            var initializations = new List<DelegateInitInfo>();
            var mnoduleType = DotNetUtils.GetModuleType(Module);
            if(mnoduleType != null)
            foreach (var method in mnoduleType.Methods)
            {
                var inits = FindFieldInitializations(method);
                if (inits == null || inits.Count == 0)
                    continue;
                initializations.AddRange(inits);
            }
            foreach (var type in Module.GetTypes())
            {
                if (!DotNetUtils.DerivesFromDelegate(type))
                    continue;
                var inits = FindFieldInitializations(type.FindStaticConstructor());
                if (inits == null || inits.Count == 0)
                    continue;
                initializations.AddRange(inits);
            }
            Initializations = initializations;
        }

        private bool IsDelegateMethod(MethodDef method)
        {
            if (!method.HasBody)
                return false;
            if (!DotNetUtils.IsMethod(method, "System.Void", "(System.RuntimeFieldHandle,System.Byte)"))
                return false;
            //1   0001    call    class [mscorlib]
            //System.Reflection.FieldInfo[mscorlib] System.Reflection.FieldInfo::GetFieldFromHandle(valuetype[mscorlib] System.RuntimeFieldHandle)
            if (!DotNetUtils.CallsMethod(method, "System.Reflection.FieldInfo System.Reflection.FieldInfo::GetFieldFromHandle(System.RuntimeFieldHandle)"))
                return false;
            //4   0008    callvirt instance class [mscorlib]
            //System.Reflection.Module[mscorlib] System.Reflection.MemberInfo::get_Module()
            if (!DotNetUtils.CallsMethod(method, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()"))
                return false;
            //6	000E	callvirt instance int32[mscorlib] System.Reflection.MemberInfo::get_MetadataToken()
            if (!DotNetUtils.CallsMethod(method, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()"))
                return false;
            //7	0013	callvirt instance uint8[][mscorlib] System.Reflection.Module::ResolveSignature(int32)
            if (!DotNetUtils.CallsMethod(method, "System.Byte[] System.Reflection.Module::ResolveSignature(System.Int32)"))
                return false;
            //14  001E    callvirt instance class [mscorlib]
            //System.Type[][mscorlib] System.Reflection.FieldInfo::GetOptionalCustomModifiers()
            if (!DotNetUtils.CallsMethod(method, "System.Type[] System.Reflection.FieldInfo::GetOptionalCustomModifiers()"))
                return false;
            //17	0025	callvirt instance int32[mscorlib] System.Reflection.MemberInfo::get_MetadataToken()
            if (!DotNetUtils.CallsMethod(method, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()"))
                return false;
            //21	002D	callvirt instance string[mscorlib] System.Reflection.MemberInfo::get_Name()
            if (!DotNetUtils.CallsMethod(method, "System.String System.Reflection.MemberInfo::get_Name()"))
                return false;
            //23	0033	callvirt instance char[mscorlib] System.String::get_Chars(int32)
            if (!DotNetUtils.CallsMethod(method, "System.Char System.String::get_Chars(System.Int32)"))
                return false;
            //105 00AD callvirt    instance object[][mscorlib] System.Reflection.MemberInfo::GetCustomAttributes(bool)
            if (!DotNetUtils.CallsMethod(method, "System.Object[] System.Reflection.MemberInfo::GetCustomAttributes(System.Boolean)"))
                return false;
            //108 00B4 callvirt    instance int32[mscorlib]System.Object::GetHashCode()
            if (!DotNetUtils.CallsMethod(method, "System.Int32 System.Object::GetHashCode()"))
                return false;
            // 112 00BD callvirt    instance class [mscorlib]
            // System.Reflection.Module[mscorlib] System.Reflection.MemberInfo::get_Module()
            //114	00C4 callvirt    instance class [mscorlib]
            // System.Reflection.MethodBase[mscorlib] System.Reflection.Module::ResolveMethod(int32)
            if (!DotNetUtils.CallsMethod(method, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)"))
                return false;
            //117	00CC	callvirt	instance class [mscorlib]System.Type [mscorlib]System.Reflection.FieldInfo::get_FieldType()
            if (!DotNetUtils.CallsMethod(method, "System.Type System.Reflection.FieldInfo::get_FieldType()"))
                return false;
            //120	00D5	callvirt	instance bool [mscorlib]System.Reflection.MethodBase::get_IsStatic()
            if (!DotNetUtils.CallsMethod(method, "System.Boolean System.Reflection.MethodBase::get_IsStatic()"))
                return false;
            return true;
        }

        private List<DelegateInitInfo> FindFieldInitializations(MethodDef method)
        {
            if (method == null || !method.HasBody)
                return null;
            var buffer = new List<DelegateInitInfo>();
            var instr = method.Body.Instructions;
            for (int i = 2; i < instr.Count; i++)
            {
                if (instr[i].OpCode != OpCodes.Call)
                    continue;
                if (!instr[i - 1].IsLdcI4())
                    continue;
                if (instr[i - 2].OpCode != OpCodes.Ldtoken)
                    continue;
                if (!(instr[i].Operand is MethodDef initMethod) || !Methods.Contains(initMethod))
                    continue;
                if (!(instr[i - 2].Operand is FieldDef field))
                    continue;
                var key = instr[i - 1].GetLdcI4Value();
                buffer.Add(new DelegateInitInfo(field,initMethod, key,method,i));
            }
            return buffer;
        }
    }
}
