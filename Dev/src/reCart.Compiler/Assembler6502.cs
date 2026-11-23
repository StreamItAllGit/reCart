// @module: reCart.Compiler.Assembler6502
// @purpose: Minimal deterministic 6502 mini-assembler used solely to construct 100% clean-room synthetic test ROMs.
// @depends-on: System, System.Collections.Generic
// @exports: Assembler6502

namespace reCart.Compiler;

internal sealed class Assembler6502
{
    private readonly byte[] _prg = new byte[32768];
    private readonly Dictionary<string, ushort> _labels = new(StringComparer.Ordinal);
    private readonly List<(int Offset, string Label, bool IsRelative)> _fixups = new();
    private int _cursor;

    public Assembler6502()
    {
        Array.Fill(_prg, (byte)0xEA); // Fill unused PRG with clean NOPs
    }

    public ushort CurrentAddress => (ushort)(0x8000 + _cursor);

    public void MarkLabel(string label) => _labels[label] = CurrentAddress;

    public void Emit(params byte[] bytes)
    {
        foreach (byte b in bytes) _prg[_cursor++] = b;
    }

    public void EmitDataBlock(string label, ReadOnlySpan<byte> data)
    {
        MarkLabel(label);
        data.CopyTo(_prg.AsSpan(_cursor));
        _cursor += data.Length;
    }

    public void LdaImm(byte v) => Emit(0xA9, v);
    public void LdxImm(byte v) => Emit(0xA2, v);
    public void LdyImm(byte v) => Emit(0xA0, v);
    public void LdaZp(byte zp) => Emit(0xA5, zp);
    public void StaZp(byte zp) => Emit(0x85, zp);
    public void LdaAbs(ushort a) => Emit(0xAD, (byte)a, (byte)(a >> 8));
    public void StaAbs(ushort a) => Emit(0x8D, (byte)a, (byte)(a >> 8));
    public void StaAbsX(ushort a) => Emit(0x9D, (byte)a, (byte)(a >> 8));
    public void StxAbs(ushort a) => Emit(0x8E, (byte)a, (byte)(a >> 8));
    public void BitAbs(ushort a) => Emit(0x2C, (byte)a, (byte)(a >> 8));
    public void CmpImm(byte v) => Emit(0xC9, v);
    public void CpxImm(byte v) => Emit(0xE0, v);
    public void AndImm(byte v) => Emit(0x29, v);
    public void AdcImm(byte v) => Emit(0x69, v);
    public void SbcImm(byte v) => Emit(0xE9, v);
    public void IncZp(byte zp) => Emit(0xE6, zp);
    public void RolZp(byte zp) => Emit(0x26, zp);
    public void LdaAbsXLabel(string label) { Emit(0xBD); AddAbsFixup(label); }
    public void Jmp(string label) { Emit(0x4C); AddAbsFixup(label); }
    public void Jsr(string label) { Emit(0x20); AddAbsFixup(label); }
    public void Beq(string label) => EmitBranch(0xF0, label);
    public void Bne(string label) => EmitBranch(0xD0, label);
    public void Bcc(string label) => EmitBranch(0x90, label);
    public void Bcs(string label) => EmitBranch(0xB0, label);

    private void AddAbsFixup(string label)
    {
        _fixups.Add((_cursor, label, false));
        _cursor += 2;
    }

    private void EmitBranch(byte opcode, string label)
    {
        Emit(opcode);
        _fixups.Add((_cursor, label, true));
        _cursor += 1;
    }

    public byte[] FinalizeRom(string nmiLabel, string resetLabel, string irqLabel)
    {
        foreach ((int offset, string label, bool isRel) in _fixups)
        {
            if (!_labels.TryGetValue(label, out ushort target))
            {
                throw new InvalidOperationException($"Unresolved 6502 label '{label}'.");
            }
            if (isRel)
            {
                int pcAfter = 0x8000 + offset + 1;
                int delta = target - pcAfter;
                if (delta is < -128 or > 127)
                {
                    throw new InvalidOperationException($"Branch target '{label}' out of 8-bit relative range ({delta}).");
                }
                _prg[offset] = (byte)(sbyte)delta;
            }
            else
            {
                _prg[offset] = (byte)target;
                _prg[offset + 1] = (byte)(target >> 8);
            }
        }

        WriteVector(0x7FFA, _labels[nmiLabel]);
        WriteVector(0x7FFC, _labels[resetLabel]);
        WriteVector(0x7FFE, _labels[irqLabel]);
        return _prg;
    }

    private void WriteVector(int prgOffset, ushort address)
    {
        _prg[prgOffset] = (byte)address;
        _prg[prgOffset + 1] = (byte)(address >> 8);
    }
}
