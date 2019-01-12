using System;
using System.Runtime.InteropServices;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace CawkEmulatorV4.Instructions.Misc
{
    internal class Stobj
    {
        public static void Emulate(ValueStack valueStack, Instruction instruction)
        {
            var obj = valueStack.CallStack.Pop();
            var address = valueStack.CallStack.Pop();
            var type = (ITypeDefOrRef) instruction.Operand;
            var typeResolve = typeof(string).Module.GetType(type.ReflectionFullName);
            var abc = obj;
            if (typeResolve == typeof(uint))
            {
                abc = (uint) obj;
            }
            else if (typeResolve == typeof(int))
            {
                abc = (int) obj;
            }
            else
            {
               abc = Convert.ChangeType(obj, typeResolve);
            }

            Marshal.StructureToPtr(abc, address, true);


            //    var abc = Marshal.PtrToStructure(address, obj);
        }
    }
}