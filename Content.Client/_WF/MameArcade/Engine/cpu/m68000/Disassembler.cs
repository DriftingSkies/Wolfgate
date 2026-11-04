#nullable disable
using System.Text;

namespace Content.Client._WF.MameArcade.Engine.cpu.m68000
{
    public sealed class DisassemblyInfo
    {
        public int PC;
        public string Mnemonic;
        public string Args;
        public string RawBytes;
        public int Length;

        public override string ToString()
        {
            return string.Format("{0:X6}: {3,-20}  {1,-8} {2}", new object[] { PC, Mnemonic, Args, RawBytes });
        }
    }

    partial class MC68000
    {
        // Wolfgate: the dispatch compared opcode delegates, which the client sandbox bans. Only debug dumps used it.
        public DisassemblyInfo Disassemble(int pc)
        {
            return new DisassemblyInfo { Mnemonic = "UNKNOWN", PC = pc, Length = 2 };
        }
    }
}