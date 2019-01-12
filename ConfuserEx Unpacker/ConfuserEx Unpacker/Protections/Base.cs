using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConfuserEx_Unpacker.Protections
{
   abstract class Base
    {
        public abstract void Deobfuscate();
        public static ModuleDefMD ModuleDef;
        public static bool CompressorRemoved = true;

    }
}
