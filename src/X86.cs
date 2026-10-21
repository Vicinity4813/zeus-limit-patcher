// Decodeur x86-32 minimal : longueur de chaque instruction, champs 32 bits (deplacement, immediat,
// adresse directe) et flot de controle. Suffisant pour le code MSVC de Zeus (pas de SSE2+, pas d'adressage 16 bits).
using System;
using System.Collections.Generic;

public struct Field { public int Off; public char Kind; public uint Value; public int Size; }   // Kind : 'd' deplacement, 'i' immediat, 'm' adresse directe

public class Insn
{
    public uint Va; public int Len;
    public byte Op; public bool Two;           // octet d'opcode (apres 0F si Two)
    public int Reg = -1, Mod = -1, Rm = -1;    // champs du ModRM
    public bool Sib; public int Scale, Index, Base;
    public List<Field> Fields = new List<Field>();
    public uint Target;                        // saut ou appel relatif
    public bool Jump, Cond, Call, Stop;        // Stop : ret, jmp indirect, int3...
    public bool IndirectJump, IndirectCall;
}

public static class X86
{
    // Lit une instruction a l'offset p de b (va = adresse virtuelle). null si opcode inconnu.
    public static Insn Decode(byte[] b, int p, uint va)
    {
        int s = p; bool opsz = false;
        while (true)
        {
            if (p >= b.Length) return null;
            byte x = b[p];
            if (x == 0x66) { opsz = true; p++; continue; }
            if (x == 0x67) return null;
            if (x == 0xF0 || x == 0xF2 || x == 0xF3 || x == 0x26 || x == 0x2E || x == 0x36 || x == 0x3E || x == 0x64 || x == 0x65) { p++; continue; }
            break;
        }
        Insn n = new Insn(); n.Va = va;
        byte op = b[p++];
        bool modrm = false; int imm = 0; int immz = opsz ? 2 : 4;
        if (op == 0x0F)
        {
            n.Two = true; op = b[p++];
            if (op >= 0x80 && op <= 0x8F) { int r = BitConverter.ToInt32(b, p); p += 4; n.Jump = true; n.Cond = true; n.Target = (uint)(va + (p - s) + r); }
            else if (op == 0x06 || op == 0x08 || op == 0x09 || op == 0x0B || op == 0x31 || op == 0x77 || op == 0xA2 || (op >= 0x30 && op <= 0x37)
                || op == 0xA0 || op == 0xA1 || op == 0xA8 || op == 0xA9 || op == 0xAA || (op >= 0xC8 && op <= 0xCF)) { }
            else if (op == 0x70 || op == 0x71 || op == 0x72 || op == 0x73 || op == 0xA4 || op == 0xAC || op == 0xBA || op == 0xC2 || op == 0xC4 || op == 0xC5 || op == 0xC6) { modrm = true; imm = 1; }
            else if (op <= 0x03 || op == 0x0D || (op >= 0x10 && op <= 0x2F) || (op >= 0x40 && op <= 0x7F) || (op >= 0x90 && op <= 0x9F)
                || op == 0xA3 || op == 0xA5 || op == 0xAB || op == 0xAD || op == 0xAE || op == 0xAF || (op >= 0xB0 && op <= 0xC7 && op != 0xBA) || op >= 0xD0) modrm = true;
            else return null;
        }
        else if (op < 0x40)
        {
            int lo = op & 7;
            if (op == 0x0F) return null;
            if (lo <= 3) modrm = true; else if (lo == 4) imm = 1; else if (lo == 5) imm = immz;
        }
        else if (op < 0x60) { }
        else switch (op)
        {
            case 0x60: case 0x61: case 0x6C: case 0x6D: case 0x6E: case 0x6F: break;
            case 0x62: case 0x63: modrm = true; break;
            case 0x68: imm = immz; break;
            case 0x69: modrm = true; imm = immz; break;
            case 0x6A: imm = 1; break;
            case 0x6B: modrm = true; imm = 1; break;
            case 0x80: case 0x82: case 0x83: case 0xC0: case 0xC1: case 0xC6: modrm = true; imm = 1; break;
            case 0x81: case 0xC7: modrm = true; imm = immz; break;
            case 0x9A: case 0xEA: p += opsz ? 4 : 6; break;
            case 0xA0: case 0xA1: case 0xA2: case 0xA3:
                n.Fields.Add(new Field { Off = p - s, Kind = 'm', Value = BitConverter.ToUInt32(b, p), Size = 4 }); p += 4; break;
            case 0xA8: imm = 1; break;
            case 0xA9: imm = immz; break;
            case 0xC2: p += 2; n.Stop = true; break;
            case 0xC3: case 0xCB: case 0xCC: case 0xCF: n.Stop = true; break;
            case 0xCA: p += 2; n.Stop = true; break;
            case 0xC4: case 0xC5: modrm = true; break;
            case 0xC8: p += 3; break;
            case 0xCD: case 0xD4: case 0xD5: case 0xE4: case 0xE5: case 0xE6: case 0xE7: imm = 1; break;
            case 0xE8: case 0xE9:
                { int r = BitConverter.ToInt32(b, p); p += 4; n.Target = (uint)(va + (p - s) + r); if (op == 0xE8) n.Call = true; else { n.Jump = true; } break; }
            case 0xEB: case 0xE0: case 0xE1: case 0xE2: case 0xE3:
                { int r = (sbyte)b[p]; p += 1; n.Target = (uint)(va + (p - s) + r); n.Jump = true; n.Cond = op != 0xEB; break; }
            case 0xF6: case 0xF7: case 0xFE: case 0xFF: case 0x84: case 0x85: case 0x86: case 0x87: case 0x88: case 0x89: case 0x8A: case 0x8B:
            case 0x8C: case 0x8D: case 0x8E: case 0x8F: case 0xD0: case 0xD1: case 0xD2: case 0xD3:
                modrm = true; break;
            default:
                if (op >= 0x70 && op <= 0x7F) { int r = (sbyte)b[p]; p += 1; n.Target = (uint)(va + (p - s) + r); n.Jump = true; n.Cond = true; }
                else if (op >= 0x90 && op <= 0x9F) { }
                else if (op >= 0xA4 && op <= 0xAF) { }
                else if (op >= 0xB0 && op <= 0xB7) imm = 1;
                else if (op >= 0xB8 && op <= 0xBF) imm = immz;
                else if (op >= 0xD8 && op <= 0xDF) modrm = true;
                else if (op == 0xC9 || op == 0xCE || op == 0xD6 || op == 0xD7 || (op >= 0xEC && op <= 0xEF) || op == 0xF1 || op == 0xF4 || op == 0xF5 || (op >= 0xF8 && op <= 0xFD)) { }
                else return null;
                break;
        }
        n.Op = op;
        if (modrm)
        {
            if (p >= b.Length) return null;
            byte m = b[p++];
            n.Mod = m >> 6; n.Reg = (m >> 3) & 7; n.Rm = m & 7;
            if (n.Mod != 3)
            {
                int disp = n.Mod == 1 ? 1 : n.Mod == 2 ? 4 : 0;
                if (n.Rm == 4)
                {
                    byte sib = b[p++]; n.Sib = true; n.Scale = 1 << (sib >> 6); n.Index = (sib >> 3) & 7; n.Base = sib & 7;
                    if (n.Mod == 0 && n.Base == 5) disp = 4;
                }
                else if (n.Mod == 0 && n.Rm == 5) disp = 4;
                if (disp == 4) n.Fields.Add(new Field { Off = p - s, Kind = 'd', Value = BitConverter.ToUInt32(b, p), Size = 4 });
                p += disp;
            }
            if (!n.Two && (op == 0xF6 || op == 0xF7) && n.Reg <= 1) imm = op == 0xF6 ? 1 : immz;
            if (!n.Two && op == 0xFF)
            {
                if (n.Reg == 2 || n.Reg == 3) { n.IndirectCall = true; n.Call = true; }
                if (n.Reg == 4 || n.Reg == 5) { n.IndirectJump = true; n.Stop = true; }
            }
        }
        if (imm == 4) n.Fields.Add(new Field { Off = p - s, Kind = 'i', Value = BitConverter.ToUInt32(b, p), Size = 4 });
        if (imm == 2) n.Fields.Add(new Field { Off = p - s, Kind = 'i', Value = BitConverter.ToUInt16(b, p), Size = 2 });
        p += imm;
        n.Len = p - s;
        if (n.Jump && !n.Cond) n.Stop = true;
        return n;
    }
}
