using de4dot.blocks;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConfuserEx_Unpacker.Protections.RefProxy
{
    class Remover : Base
    {
        public override void Deobfuscate()
        {
            var finder = new DelegateFinder(ModuleDef);
            finder.FindMethods();
            finder.FindFields();
            var fixer = new DelegateFixer(ModuleDef, finder.Initializations);
            if (finder.Initializations != null && finder.Initializations.Count != 0)
            {
                fixer.DecryptMethods();
                fixer.Fix();
            }
            fixer.FixProxyCalls();
            fixer.RemoveDelegateJunk();
            fixer.RemoveProxyJunk();
        }

       
    }
}
