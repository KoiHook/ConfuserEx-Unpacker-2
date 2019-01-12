using ConfuserEx_Unpacker.Protections;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EasyPredicateKiller;

namespace ConfuserEx_Unpacker
{
    class Program
    {
        private static Base[] bases = new Base[]
        {
            new Protections.Antitamper.Remover(),
                 new Protections.Control_Flow.Remover(),
            new Protections.Compressor.Remover(),

               new Protections.Antitamper.Remover(),
               new Protections.Control_Flow.Remover(),
               new Protections.RefProxy.Remover(),
               new Protections.Control_Flow.Remover(),
               new Protections.Constants.Remover(),
               new Protections.Control_Flow.Remover(),
               new Protections.RefProxy.Remover(),
        };
        static void Main(string[] args)
        {
            if (args.Length != 1)
                throw new Exception("Invalid arguments.");
            filename = args[0];
            if (!File.Exists(filename))
                throw new FileNotFoundException($"{Path.GetFileName(filename)} doesn't exist.");
            module = ModuleDefMD.Load(filename);
            LoadAsmRef();
            Base.ModuleDef = module;
            MethodDefExt2.OriginalMD = Base.ModuleDef;
            foreach (Base base1 in bases)
            {
                base1.Deobfuscate();
            }

            if (Protections.Compressor.Remover.ModuleEp != 0)
            {
                Base.ModuleDef.EntryPoint =
                    Base.ModuleDef.ResolveToken(Protections.Compressor.Remover.ModuleEp) as MethodDef;
            }

            ModuleWriterOptions ModOpts = new ModuleWriterOptions(Base.ModuleDef);
            ModOpts.MetadataOptions.Flags = MetadataFlags.PreserveAll;
            ModOpts.Logger = DummyLogger.NoThrowInstance;
            Console.WriteLine("Writing the file...");
            Base.ModuleDef.Write(NewPath(filename), ModOpts);
            Console.ReadLine();
        }
        public static string NewPath(string path)
        {
            return $"{Path.GetDirectoryName(path)}\\{Path.GetFileNameWithoutExtension(path)}-cleaned{Path.GetExtension(path)}";
        }
        private static string filename;
        private static ModuleDefMD module;
        public static void LoadAsmRef()
        {
            var asmResolver = new AssemblyResolver();
            var modCtx = new ModuleContext(asmResolver);
            asmResolver.DefaultModuleContext = modCtx;
            asmResolver.EnableTypeDefCache = true;

            module.Location = filename;
            var asmRefs = module.GetAssemblyRefs().ToList();
            module.Context = modCtx;
            foreach (var asmRef in asmRefs)
            {
                if (asmRef == null)
                    continue;
                var asma = asmResolver.Resolve(asmRef.FullName, module);
                //	Protections.Protections.ModuleDef.Context.AssemblyResolver.AddToCache(asma);
                ((AssemblyResolver)module.Context.AssemblyResolver).AddToCache(asma);
            }
        }
    }
}
