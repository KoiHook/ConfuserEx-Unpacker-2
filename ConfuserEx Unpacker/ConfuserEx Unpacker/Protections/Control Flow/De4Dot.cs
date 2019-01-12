using ConfuserDeobfuscator.Engine.Routines.Ex.x86;
using de4dot.blocks;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConfuserEx_Unpacker.Protections.Control_Flow
{
    class Cflow : BlockDeobfuscator
    {
        private Block switchBlock;
        private Local localSwitch;
        private string currentMethod;

        protected override bool Deobfuscate(Block block)
        {
            // allSwitches.Clear();
            allVars = blocks.Method.Body.Variables.Locals;
            // findAllSwitches(allBlocks);
            bool modified = false;
            
            if (block.LastInstr.OpCode == OpCodes.Switch)
            {


                isSwitchBlock(block);
                isExpressionBlock(block);
                isNative(block);
                if (switchBlock != null || localSwitch != null)
                {

                    //if normal switch block failed check to see if its expression

                    modified = tester();
                }



                //check if normal switch block

                currentMethod = blocks.Method.FullName;
            }
            else
            {
                //make sure switch block isnt null so we can continue

            }
            if (switchBlock != null || localSwitch != null)
            {

                //if normal switch block failed check to see if its expression

                modified = tester();
            }
            return modified;
        }
        public List<Block> allSwitches = new List<Block>();
        void findAllSwitches(List<Block> allBlocks)
        {
            foreach (Block blo in allBlocks)
            {
                if (blo.LastInstr.OpCode == OpCodes.Switch)
                    allSwitches.Add(blo);
            }
            allSwitches.Reverse();
            if (allSwitches.Count == 3)
                ben = true;
        }
        bool isXor(Block block)
        {
            //check to confirm it is indeed the correct block 
            //credits to TheProxy for this method since mine wasnt as efficient 
            int l = block.Instructions.Count - 1;
            var instr = block.Instructions;
            if (l < 4)
                return false;
            if (instr[l].OpCode != OpCodes.Xor)
                return false;
            if (!instr[l - 1].IsLdcI4())
                return false;
            if (instr[l - 2].OpCode != OpCodes.Mul)
                return false;
            if (!instr[l - 3].IsLdcI4())
                return false;


            return true;
        }
        public List<Block> processed = new List<Block>();
        private IList<Local> allVars;
        private bool native;
        private bool isolder;
        private bool ben;

        bool tester()
        {

            bool edited = false;

            //we need all the blocks that are connected to the switch block so all the cases
            List<Block> allblocks = new List<Block>();
            foreach (var block in allBlocks)
            {
                if (block.FallThrough == switchBlock)
                {
                    allblocks.Add(block);
                }

            }

            foreach (Block blo in allblocks)
            {
                /*
                 * now there are different types of ways the next arg is set
                 * you have a block like this arg = num1*num2^num3
                 * you have just arg = num1
                 * conditional blocks arg = (flag) ? num1 : num2) ^ num3 * num4) the num1 and num2 are dependant on whether the flag is true or false
                 * and finally we have just flag ? num1:num2 yet again depending on the flag
                 * these are all the switch types */
                //   if (processed.Contains(blo)) continue;
                if (blo.LastInstr.IsLdcI4())
                {
                    //if its just a arg = num then the last value of the block is a ldc so we go here
                    int val;
                    //we emulate the switch block this helps with it being more universal also makes life easier look in emulate method for more comments
                    int nextCase = emulate(blo.LastInstr.GetLdcI4Value(), out val);
                    //after we have the next case value we can use de4dot again to replace the new block to follow on from the current block
                    try
                    {
                        blo.ReplaceLastNonBranchWithBranch(0, switchBlock.Targets[nextCase]);
                    }
                    catch
                    {

                    }

                    //we get the next block through the switchblocks targets and we find the local in the next block and replace it to ldc value so it can be solved into a single arg value for the next case
                    if (isolder)
                    {

                    }
                    else
                    {
                        replace(switchBlock.Targets[nextCase], nextCase, val);
                    }

                    //we add a pop instruction to tell de4dot that this block is now useless so it is removed with deadblockremover
                    blo.Add(new Instr(OpCodes.Pop.ToInstruction()));
                    processed.Add(blo);
                    edited = true;
                    //  goto start;
                }
                //if it looks like num1*num2^num3 then its this the isXor just confirms this, this method is from TheProxy 
                else if (isXor(blo))
                {
                    var instr = blo.Instructions;
                    //we check to see if the ldloc has been replaced with the ldc as if it hasnt it means we havent got to that block yet
                    if (instr[instr.Count - 5].IsLdcI4())
                    {
                        //we now get all the keys so we can work out the next arg value to emulate the switch to get next case
                        int l = instr.Count;
                        int key1 = instr[l - 4].GetLdcI4Value();
                        int key2 = instr[l - 2].GetLdcI4Value();
                        int key3 = instr[l - 5].GetLdcI4Value();
                        int value = key3 * key1 ^ key2;
                        int val;
                        int nextCase = emulate(value, out val);
                        //we do the same as before to replace the block again 
                        blo.ReplaceLastNonBranchWithBranch(0, switchBlock.Targets[nextCase]);
                        if (isolder)
                        {

                        }
                        else
                        {
                            replace(switchBlock.Targets[nextCase], nextCase, val);
                        }
                        blo.Add(new Instr(OpCodes.Pop.ToInstruction())); processed.Add(blo);
                        //    goto start;
                    }
                }
                //this check is for conditional blocks 
                else if (blo.LastInstr.OpCode == OpCodes.Xor)
                {
                    if (blo.Instructions[blo.Instructions.Count - 2].OpCode == OpCodes.Mul)
                    {
                        var instr = blo.Instructions;
                        //we check if the ldloc opcode has been replaced with ldc
                        int l = instr.Count;
                        if (!(instr[l - 4].IsLdcI4())) continue;
                        int key1 = instr[l - 3].GetLdcI4Value();
                        int key3 = instr[l - 4].GetLdcI4Value();
                        //now we get the two keys from this block however the other keys arent actually in this block
                        //they are in this blocks sources so we go through each source there should be 2

                        var sources = new List<Block>(blo.Sources);
                        foreach (var source in sources)
                        {
                            //now this will give us the final key we need to get the next arg value and next case
                            int key2 = source.FirstInstr.GetLdcI4Value();
                            int value = key2 ^ key3 * key1;


                            int val;
                            int nextCase = emulate(value, out val);
                            if (isolder)
                            {

                            }
                            else
                            {
                                replace(switchBlock.Targets[nextCase], nextCase, val);
                            }
                            try
                            {
                                source.Instructions[1] = new Instr(OpCodes.Pop.ToInstruction());
                            }
                            catch
                            {
                                source.Instructions.Add(new Instr(OpCodes.Pop.ToInstruction()));
                            }


                            source.ReplaceLastNonBranchWithBranch(0, switchBlock.Targets[nextCase]);
                        }
                        processed.Add(blo);
                        //  goto start;
                    }
                }
                //finally we check if its a simple flag ? num1:num2
                else if (blo.Sources.Count == 2 && blo.Instructions.Count == 1
                    )
                {
                    var instr = blo.Instructions;

                    int l = instr.Count;
                    //same again it will be in the sources of a pop block
                    var sources = new List<Block>(blo.Sources);
                    foreach (var source in sources)
                    {
                        if (!source.FirstInstr.IsLdcI4()) continue;
                        if (source.Instructions.Count != 2) continue;
                        //again we go through the sources however this time we dont need to work out any value
                        //we just need to get the ldc value from the source block and emulate
                        int val;
                        int nextCase = emulate(source.FirstInstr.GetLdcI4Value(), out val);
                        if (isolder)
                        {

                        }
                        else
                        {
                            replace(switchBlock.Targets[nextCase], nextCase, val);
                        }

                        source.Instructions[1] = new Instr(OpCodes.Pop.ToInstruction());

                        source.ReplaceLastNonBranchWithBranch(0, switchBlock.Targets[nextCase]);
                    }
                    processed.Add(blo);// goto start;
                }

            }
            //      switchBlock = null;
            //     localSwitch = null;
            return edited;
        }
        int x86emulate(MethodDef methods, int[] val)
        {
            
            var x86 = new X86Method(methods);
            return x86.Execute(val);
        }
        public void replace(Block test, int nextCase, int locVal)
        {
            //we replace the ldloc values with the correct ldc value 
            if (test.IsConditionalBranch())
            {
                //if it happens to be a conditional block then the ldloc wont be in the current block it will be in the fallthrough block
                //normally the fallthrough block is the switch block but then fallthrough again you get the correct block you need to replace
                //however this bit i dont really understand as much but it works so what ever but sometimes the fallthrough block is the first fallthrough not the second so we just set it to the first
                if (test.FallThrough.FallThrough == switchBlock)
                {

                    test = test.FallThrough;
                }
                else
                {
                    test = test.FallThrough.FallThrough;

                }

            }
            if (test == switchBlock) return;

            for (int i = 0; i < test.Instructions.Count; i++)
            {
                if (test.Instructions[i].Instruction.GetLocal(blocks.Method.Body.Variables) == localSwitch)
                {

                    //check to see if the local is the same as the one from the switch block and replace it
                    test.Instructions[i] = new Instr(Instruction.CreateLdcI4(locVal));
                    return;
                }
            }
        }
        public int emulate(int val, out int locValue)
        {
            locValue = 0;
            //we take the value of the arg as a parameter to pass along to the switch block
            InstructionEmulator ins = new InstructionEmulator(blocks.Method);
            ins.Initialize(blocks.Method);
            if (native)
            {
                var test = x86emulate(switchBlock.FirstInstr.Operand as MethodDef, new int[] { val });
                ins.Push(new Int32Value(test));
                //emulates the block however we dont emulate all the block 
                ins.Emulate(switchBlock.Instructions, 1, switchBlock.Instructions.Count - 1);
                //we now get the local value this will contain the num value used to work out the next arg in the next case
                if (!isolder)
                {
                    locValue = (ins.GetLocal(localSwitch) as Int32Value).Value;
                }
            }
            else
            {
                ins.Push(new Int32Value(val));
                //emulates the block however we dont emulate all the block 
                ins.Emulate(switchBlock.Instructions, 0, switchBlock.Instructions.Count - 1);
                //we now get the local value this will contain the num value used to work out the next arg in the next case
                if (!isolder)
                {
                    locValue = (ins.GetLocal(localSwitch) as Int32Value).Value;
                }


            }
            //we use de4dots instructionEmulator to emulate the block
            //we push the arg value to the stack

            //we peek at the stack value and this is the next case so we return this to continue
  
            var caseValue = ins.Peek() as Int32Value;
            return caseValue.Value;

        }
        void isExpressionBlock(Block block)
        {
            if (block.Instructions.Count < 7)
                return;
            if (!block.FirstInstr.IsLdloc())
                return;
            //we check to see if the switch block is confuserex cflow expression

            switchBlock = block;
            //set the local to a variable to compare to later
            localSwitch = Instr.GetLocalVar(blocks.Method.Body.Variables.Locals, block.Instructions[block.Instructions.Count - 4]);
            return;


        }
        void isNative(Block block)
        {

            if (block.Instructions.Count <= 5)
                return;
            if (block.FirstInstr.OpCode != OpCodes.Call)
                return;
            switchBlock = block;
            native = true;

            //set the local to a variable to compare to later
            localSwitch = Instr.GetLocalVar(allVars, block.Instructions[block.Instructions.Count - 4]);
            return;
        }
        void isolderCflow(Block block)
        {
            if (block.Instructions.Count <= 2)
                return;
            if (!block.FirstInstr.IsLdcI4())
                return;
            //check to see if its confuserex switch block
            isolder = true;
            switchBlock = block;
            //set the local to a variable to compare to later
            //  localSwitch = Instr.GetLocalVar(allVars, block.Instructions[block.Instructions.Count - 4]);
            return;
        }
        void isolderNatCflow(Block block)
        {
            if (block.Instructions.Count != 2)
                return;
            if (block.FirstInstr.OpCode != OpCodes.Call)
                return;
            //check to see if its confuserex switch block
            isolder = true;
            switchBlock = block;
            native = true;
            //set the local to a variable to compare to later
            //  localSwitch = Instr.GetLocalVar(allVars, block.Instructions[block.Instructions.Count - 4]);
            return;
        }
        void isolderExpCflow(Block block)
        {
            if (block.Instructions.Count <= 2)
                return;
            if (!block.FirstInstr.IsStloc())
                return;
            //check to see if its confuserex switch block
            isolder = true;
            switchBlock = block;
            //set the local to a variable to compare to later
            //  localSwitch = Instr.GetLocalVar(allVars, block.Instructions[block.Instructions.Count - 4]);
            return;
        }
        void isSwitchBlock(Block block)
        {
            if (block.Instructions.Count <= 6)
                return;
            if (!block.FirstInstr.IsLdcI4())
                return;
            //check to see if its confuserex switch block

            switchBlock = block;
            //set the local to a variable to compare to later
            localSwitch = Instr.GetLocalVar(allVars, block.Instructions[block.Instructions.Count - 4]);
            return;




        }
    }
}
