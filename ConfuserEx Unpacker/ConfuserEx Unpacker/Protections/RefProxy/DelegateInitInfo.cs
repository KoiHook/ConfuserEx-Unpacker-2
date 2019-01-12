using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace ConfuserEx_Unpacker.Protections.RefProxy
{
    public class DelegateInitInfo
    {
        public byte Key { get; set; }
        public int InitalizedAt { get; set; }
        public FieldDef Field { get; set; }
        public MethodDef InitMethod { get; set; }
        public MethodDef Method { get; set; }
        public IMethod Decrypted { get; set; }
        public OpCode OpCode { get; set; }
        public bool Resolved = false;
        public DelegateInitInfo(FieldDef field, MethodDef initMethod, int key,  MethodDef method, int initialized)
        {
            Field = field;
            Method = method;
            Key = (byte)key;
            InitMethod = initMethod;
            InitalizedAt = initialized;
            Decrypted = null;
            OpCode = null;
        }
        public override string ToString() => $"{Field.Name}, {Key}, {Decrypted}, {OpCode}";
    }
}
