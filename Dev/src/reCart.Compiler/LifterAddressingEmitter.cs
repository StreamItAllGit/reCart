// @module: reCart.Compiler.LifterAddressingEmitter
// @purpose: Formats 6502 effective-address and operand expressions for emitted C# basic blocks.
// @depends-on: System
// @exports: LifterAddressingEmitter

namespace reCart.Compiler;

internal static class LifterAddressingEmitter
{
    public static string BuildEffectiveAddressExpr(DecodedInstruction6502 insn)
    {
        byte lo = insn.OperandLo;
        ushort op16 = insn.Operand16;
        string px = insn.Spec.CheckPageCross ? "true" : "false";

        return insn.Spec.Mode switch
        {
            AddressingMode6502.ZeroPage => $"((ushort)0x{lo:X2})",
            AddressingMode6502.ZeroPageX => $"((ushort)(byte)(0x{lo:X2} + cpu.X))",
            AddressingMode6502.ZeroPageY => $"((ushort)(byte)(0x{lo:X2} + cpu.Y))",
            AddressingMode6502.Absolute => $"((ushort)0x{op16:X4})",
            AddressingMode6502.AbsoluteX => $"ReadAddrIdx(cpu, 0x{op16:X4}, cpu.X, {px})",
            AddressingMode6502.AbsoluteY => $"ReadAddrIdx(cpu, 0x{op16:X4}, cpu.Y, {px})",
            AddressingMode6502.IndirectX => $"Read16Zp(bus, (byte)(0x{lo:X2} + cpu.X))",
            AddressingMode6502.IndirectY => $"ReadIndY(bus, cpu, 0x{lo:X2}, {px})",
            _ => throw new InvalidOperationException($"Addressing mode '{insn.Spec.Mode}' does not produce a memory address.")
        };
    }

    public static string BuildReadValueExpr(DecodedInstruction6502 insn) => insn.Spec.Mode switch
    {
        AddressingMode6502.Immediate => $"((byte)0x{insn.OperandLo:X2})",
        AddressingMode6502.Accumulator => "cpu.A",
        _ => $"bus.Read({BuildEffectiveAddressExpr(insn)})"
    };
}
