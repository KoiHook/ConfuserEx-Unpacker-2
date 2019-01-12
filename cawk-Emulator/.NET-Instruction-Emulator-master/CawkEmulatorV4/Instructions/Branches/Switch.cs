using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet.Emit;

namespace CawkEmulatorV4.Instructions.Branches
{
    internal class Switch
    {
        public static int Emulate(ValueStack valueStack, Instruction ins, IList<Instruction> instructions)
        {
            var value1 = valueStack.CallStack.Pop();
            var branchTo = (Instruction[]) ins.Operand;
            try
            {
                var location = branchTo[value1];
                return instructions.IndexOf(location) - 1;
            }
            catch
            {
                return -1;
            }
          
            
        }
    }
}