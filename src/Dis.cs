// Affichage lisible (approximatif) d'une instruction decodee par X86 : assez pour relire le code a la main.
using System;
using System.Text;

public static class Dis
{
    static readonly string[] R32 = { "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi" };
    static readonly string[] R16 = { "ax", "cx", "dx", "bx", "sp", "bp", "si", "di" };
    static readonly string[] R8 = { "al", "cl", "dl", "bl", "ah", "ch", "dh", "bh" };
    static readonly string[] Alu = { "add", "or", "adc", "sbb", "and", "sub", "xor", "cmp" };
    static readonly string[] Shf = { "rol", "ror", "rcl", "rcr", "shl", "shr", "sal", "sar" };
    static readonly string[] Cc = { "o", "no", "b", "ae", "e", "ne", "be", "a", "s", "ns", "p", "np", "l", "ge", "le", "g" };
    public static Func<uint, string> Name = v => null;   // nom symbolique d'une adresse (tableaux)

    static string Hex(uint v) { string n = Name(v); return n ?? (v < 10 ? v.ToString() : "0x" + v.ToString("X")); }

    static string Mem(Insn n, byte[] b, int o, string size)
    {
        if (n.Mod == 3 || n.Mod < 0) return null;
        var sb = new StringBuilder(size + "[");
        bool any = false;
        if (n.Sib)
        {
            if (!(n.Mod == 0 && n.Base == 5)) { sb.Append(R32[n.Base]); any = true; }
            if (n.Index != 4) { if (any) sb.Append("+"); sb.Append(R32[n.Index]); if (n.Scale > 1) sb.Append("*" + n.Scale); any = true; }
        }
        else if (!(n.Mod == 0 && n.Rm == 5)) { sb.Append(R32[n.Rm]); any = true; }
        foreach (Field f in n.Fields) if (f.Kind == 'd') { if (any) sb.Append("+"); sb.Append(Hex(f.Value)); any = true; }
        if (n.Mod == 1)
        {
            // deplacement 8 bits : juste apres ModRM/SIB
            int p = o; while (b[p] == 0x66 || b[p] == 0xF2 || b[p] == 0xF3 || b[p] == 0x64 || b[p] == 0x2E || b[p] == 0x3E || b[p] == 0x26) p++;
            p += n.Two ? 3 : 2; if (n.Sib) p++;
            int d = (sbyte)b[p]; sb.Append(d < 0 ? "-0x" + (-d).ToString("X") : "+0x" + d.ToString("X"));
        }
        return sb.Append("]").ToString();
    }

    public static string Text(Insn n, byte[] b, int o)
    {
        bool w16 = false; int p = o; string rep = "";
        while (b[p] == 0x66 || b[p] == 0xF2 || b[p] == 0xF3 || b[p] == 0x64 || b[p] == 0x2E || b[p] == 0x3E || b[p] == 0x26 || b[p] == 0xF0)
        { if (b[p] == 0x66) w16 = true; if (b[p] == 0xF3) rep = "rep "; p++; }
        string[] R = w16 ? R16 : R32;
        string imm = null; foreach (Field f in n.Fields) if (f.Kind == 'i') imm = Hex(f.Value);
        if (imm == null && n.Fields.Count == 0)
        {
            // immediat 8 bits eventuel : dernier octet
            imm = null;
        }
        string last8 = "0x" + b[o + n.Len - 1].ToString("X");
        string sx8 = ((sbyte)b[o + n.Len - 1]).ToString();
        string rm32 = n.Mod == 3 ? R[n.Rm] : Mem(n, b, o, "");
        string rm8 = n.Mod == 3 ? R8[n.Rm] : Mem(n, b, o, "byte ");
        string rmw = n.Mod == 3 ? R[n.Rm] : Mem(n, b, o, w16 ? "word " : "dword ");
        byte op = n.Op;
        if (n.Two)
        {
            if (op >= 0x80 && op <= 0x8F) return "j" + Cc[op & 15] + " 0x" + n.Target.ToString("X");
            if (op >= 0x90 && op <= 0x9F) return "set" + Cc[op & 15] + " " + rm8;
            if (op >= 0x40 && op <= 0x4F) return "cmov" + Cc[op & 15] + " " + R[n.Reg] + ", " + rm32;
            if (op == 0xB6 || op == 0xBE) return (op == 0xB6 ? "movzx " : "movsx ") + R[n.Reg] + ", " + rm8;
            if (op == 0xB7 || op == 0xBF) return (op == 0xB7 ? "movzx " : "movsx ") + R32[n.Reg] + ", " + (n.Mod == 3 ? R16[n.Rm] : Mem(n, b, o, "word "));
            if (op == 0xAF) return "imul " + R[n.Reg] + ", " + rm32;
            return "0F" + op.ToString("X2") + " " + (rm32 ?? "");
        }
        if (op < 0x40 && (op & 7) <= 5 && op != 0x0F)
        {
            string a = Alu[op >> 3]; int k = op & 7;
            if (k == 0) return a + " " + rm8 + ", " + R8[n.Reg];
            if (k == 1) return a + " " + rmw + ", " + R[n.Reg];
            if (k == 2) return a + " " + R8[n.Reg] + ", " + rm8;
            if (k == 3) return a + " " + R[n.Reg] + ", " + rm32;
            if (k == 4) return a + " al, " + last8;
            return a + " " + R[0] + ", " + imm;
        }
        if (op >= 0x40 && op <= 0x47) return "inc " + R[op & 7];
        if (op >= 0x48 && op <= 0x4F) return "dec " + R[op & 7];
        if (op >= 0x50 && op <= 0x57) return "push " + R32[op & 7];
        if (op >= 0x58 && op <= 0x5F) return "pop " + R32[op & 7];
        if (op >= 0x70 && op <= 0x7F) return "j" + Cc[op & 15] + " 0x" + n.Target.ToString("X");
        if (op >= 0xB0 && op <= 0xB7) return "mov " + R8[op & 7] + ", " + last8;
        if (op >= 0xB8 && op <= 0xBF) return "mov " + R[op & 7] + ", " + imm;
        if (op >= 0x91 && op <= 0x97) return "xchg eax, " + R32[op & 7];
        switch (op)
        {
            case 0x68: return "push " + imm;
            case 0x6A: return "push " + sx8;
            case 0x69: return "imul " + R[n.Reg] + ", " + rm32 + ", " + imm;
            case 0x6B: return "imul " + R[n.Reg] + ", " + rm32 + ", " + sx8;
            case 0x80: return Alu[n.Reg] + " " + rm8 + ", " + last8;
            case 0x81: return Alu[n.Reg] + " " + rmw + ", " + imm;
            case 0x83: return Alu[n.Reg] + " " + rmw + ", " + sx8;
            case 0x84: return "test " + rm8 + ", " + R8[n.Reg];
            case 0x85: return "test " + rmw + ", " + R[n.Reg];
            case 0x86: case 0x87: return "xchg " + rm32 + ", " + R[n.Reg];
            case 0x88: return "mov " + rm8 + ", " + R8[n.Reg];
            case 0x89: return "mov " + rmw + ", " + R[n.Reg];
            case 0x8A: return "mov " + R8[n.Reg] + ", " + rm8;
            case 0x8B: return "mov " + R[n.Reg] + ", " + rm32;
            case 0x8D: return "lea " + R[n.Reg] + ", " + rm32;
            case 0x8F: return "pop " + rmw;
            case 0x90: return "nop";
            case 0x99: return "cdq";
            case 0xA0: return "mov al, [" + Hex(n.Fields[0].Value) + "]";
            case 0xA1: return "mov " + R[0] + ", [" + Hex(n.Fields[0].Value) + "]";
            case 0xA2: return "mov [" + Hex(n.Fields[0].Value) + "], al";
            case 0xA3: return "mov [" + Hex(n.Fields[0].Value) + "], " + R[0];
            case 0xA4: case 0xA5: return rep + "movs";
            case 0xAA: case 0xAB: return rep + "stos" + (op == 0xAA ? "b" : "d");
            case 0xA8: return "test al, " + last8;
            case 0xA9: return "test " + R[0] + ", " + imm;
            case 0xC0: case 0xC1: return Shf[n.Reg] + " " + (op == 0xC0 ? rm8 : rmw) + ", " + last8;
            case 0xD0: case 0xD1: return Shf[n.Reg] + " " + (op == 0xD0 ? rm8 : rmw) + ", 1";
            case 0xD2: case 0xD3: return Shf[n.Reg] + " " + (op == 0xD2 ? rm8 : rmw) + ", cl";
            case 0xC2: case 0xC3: return "ret";
            case 0xC6: return "mov " + rm8 + ", " + last8;
            case 0xC7: return "mov " + rmw + ", " + imm;
            case 0xE8: return "call 0x" + n.Target.ToString("X");
            case 0xE9: case 0xEB: return "jmp 0x" + n.Target.ToString("X");
            case 0xF6: case 0xF7:
                { string[] u = { "test", "test", "not", "neg", "mul", "imul", "div", "idiv" }; string t = op == 0xF6 ? rm8 : rmw;
                  return u[n.Reg] + " " + t + (n.Reg <= 1 ? ", " + (op == 0xF6 ? last8 : imm) : ""); }
            case 0xFE: return (n.Reg == 0 ? "inc " : "dec ") + rm8;
            case 0xFF: { string[] u = { "inc", "dec", "call", "callf", "jmp", "jmpf", "push", "?" }; return u[n.Reg] + " " + rmw; }
        }
        return "op" + op.ToString("X2") + " " + (rm32 ?? "");
    }
}
