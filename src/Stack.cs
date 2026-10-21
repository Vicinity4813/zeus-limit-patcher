// Suivi de la pile dans une fonction : profondeur (octets empiles depuis l'entree) a chaque instruction,
// et position "rel" de chaque acces [esp+X] par rapport a esp a l'entree (rel 0 = adresse de retour,
// rel >= 4 = arguments, rel < 0 = variables locales).
using System;
using System.Collections.Generic;

public class StackRef { public uint Va; public int Rel, Disp, DispSize, DispOff; public bool Indexed; public bool Lea; }

public class StackFrame
{
    public uint Entry; public int Frame;                // taille allouee par __chkstk / sub esp
    public Dictionary<uint, int> Depth = new Dictionary<uint, int>();
    public List<StackRef> Refs = new List<StackRef>();
    public List<uint> FrameOps = new List<uint>();      // instructions qui allouent / liberent le cadre
    public List<string> Problems = new List<string>();

    public const uint ChkStk = 0x5CBCD0;

    public static StackFrame Analyze(Code c, uint entry)
    {
        var sf = new StackFrame { Entry = entry };
        var todo = new Stack<KeyValuePair<uint, int>>();
        todo.Push(new KeyValuePair<uint, int>(entry, 0));
        while (todo.Count > 0)
        {
            var cur = todo.Pop(); uint va = cur.Key; int d = cur.Value; int args = 0;   // args : pushs depuis le dernier appel
            while (true)
            {
                Insn n;
                if (!c.Insns.TryGetValue(va, out n)) { sf.Problems.Add(string.Format("0x{0:X} non decode", va)); break; }
                int seen;
                if (sf.Depth.TryGetValue(va, out seen)) { if (seen != d) sf.Problems.Add(string.Format("0x{0:X} profondeur {1} / {2}", va, seen, d)); break; }
                sf.Depth[va] = d;
                int o = c.ToOff(va);
                // acces memoire base sur esp
                if (n.Mod >= 0 && n.Mod != 3 && n.Sib && n.Base == 4)
                {
                    int dsz = n.Mod == 1 ? 1 : n.Mod == 2 ? 4 : 0, disp = 0, doff = -1;
                    if (dsz == 4) { foreach (Field f in n.Fields) if (f.Kind == 'd') { disp = (int)f.Value; doff = f.Off; } }
                    else if (dsz == 1)
                    {
                        // octet de deplacement : juste avant l'immediat eventuel, apres ModRM+SIB
                        int p = o; while (c.F[p] == 0x66 || c.F[p] == 0xF2 || c.F[p] == 0xF3 || c.F[p] == 0x64 || c.F[p] == 0x2E || c.F[p] == 0x3E || c.F[p] == 0x26 || c.F[p] == 0xF0) p++;
                        p += n.Two ? 4 : 3; doff = p - o; disp = (sbyte)c.F[p];
                    }
                    sf.Refs.Add(new StackRef { Va = va, Rel = disp - d, Disp = disp, DispSize = dsz, DispOff = doff, Indexed = n.Index != 4, Lea = !n.Two && n.Op == 0x8D });
                }
                // effet sur esp
                int nd = d; bool stop = n.Stop;
                if (!n.Two)
                {
                    byte op = n.Op;
                    if ((op >= 0x50 && op <= 0x57) || op == 0x68 || op == 0x6A || (op == 0xFF && n.Reg == 6) || op == 0x9C || op == 0x06 || op == 0x0E || op == 0x16 || op == 0x1E) { nd += 4; args++; }
                    else if ((op >= 0x58 && op <= 0x5F) || op == 0x8F || op == 0x9D || op == 0x07 || op == 0x17 || op == 0x1F) { nd -= 4; if (args > 0) args--; }
                    else if (op == 0x60) nd += 32; else if (op == 0x61) nd -= 32;
                    else if ((op == 0x81 || op == 0x83) && n.Mod == 3 && n.Rm == 4 && (n.Reg == 0 || n.Reg == 5))
                    {
                        int imm = op == 0x81 ? (int)BitConverter.ToUInt32(c.F, o + n.Len - 4) : (sbyte)c.F[o + n.Len - 1];
                        nd += n.Reg == 5 ? imm : -imm;
                        if (Math.Abs(imm) >= 4096) sf.FrameOps.Add(va);
                    }
                    else if (op == 0xE8 && n.Target == ChkStk)
                    {
                        // mov eax, taille juste avant
                        int fr = 0;
                        fr = PrevMovEax(c, va);
                        if (fr <= 0) sf.Problems.Add(string.Format("0x{0:X} __chkstk sans taille", va));
                        nd += fr; sf.Frame = fr; sf.FrameOps.Add(va);
                    }
                    else if (op == 0xE8) { nd -= CalleePops(c, n.Target); args = 0; }
                    else if (op == 0xFF && n.Reg == 2)
                    {
                        // appel indirect (methode virtuelle : thiscall) : l'appele depile ses arguments, sauf add esp juste apres
                        Insn nx; bool cleanup = c.Insns.TryGetValue(va + (uint)n.Len, out nx) && !nx.Two && (nx.Op == 0x83 || nx.Op == 0x81) && nx.Mod == 3 && nx.Rm == 4 && nx.Reg == 0;
                        if (!cleanup) nd -= 4 * args;
                        sf.IndirectCalls.Add(va); args = 0;
                    }
                    else if (op == 0xC2 || op == 0xC3) { stop = true; }
                    else if ((op == 0x89 || op == 0x8B || op == 0x8D) && n.Mod == 3 && ((op == 0x89 && n.Rm == 4) || (op != 0x89 && n.Reg == 4))) sf.Problems.Add(string.Format("0x{0:X} esp modifie directement", va));
                }
                if (n.Jump) todo.Push(new KeyValuePair<uint, int>(n.Target, nd));
                if (n.IndirectJump)
                {
                    // table de sauts : cibles = dwords de la table
                    if (n.Sib && n.Scale == 4 && n.Fields.Count > 0)
                    {
                        uint t = n.Fields[0].Value;
                        for (int k = 0; k < 256; k++)
                        {
                            int to = c.ToOff(t + (uint)(4 * k)); if (to < 0) break;
                            uint v = BitConverter.ToUInt32(c.F, to); if (!c.InText(v) || !c.Insns.ContainsKey(v)) break;
                            todo.Push(new KeyValuePair<uint, int>(v, nd));
                        }
                    }
                    else sf.Problems.Add(string.Format("0x{0:X} saut indirect", va));
                }
                if (stop) break;
                d = nd; va += (uint)n.Len;
            }
        }
        return sf;
    }

    public List<uint> IndirectCalls = new List<uint>();   // appels indirects : on suppose qu'ils ne depilent rien (a verifier)

    // Octets depiles par la fonction appelee (ret N) : premier ret rencontre en suivant son code.
    static Dictionary<uint, int> pops = new Dictionary<uint, int>();
    static int CalleePops(Code c, uint target)
    {
        int r;
        if (pops.TryGetValue(target, out r)) return r;
        r = 0; uint va = target;
        var seen = new HashSet<uint>(); var todo = new Stack<uint>(); todo.Push(target);
        while (todo.Count > 0 && seen.Count < 5000)
        {
            va = todo.Pop(); bool found = false;
            while (true)
            {
                Insn n; if (seen.Contains(va) || !c.Insns.TryGetValue(va, out n)) break;
                seen.Add(va);
                if (!n.Two && n.Op == 0xC2) { r = BitConverter.ToUInt16(c.F, c.ToOff(va) + n.Len - 2); found = true; break; }
                if (!n.Two && n.Op == 0xC3) { r = 0; found = true; break; }
                if (n.Jump) todo.Push(n.Target);
                if (n.Stop) break;
                va += (uint)n.Len;
            }
            if (found) break;
        }
        pops[target] = r;
        return r;
    }

    static int PrevMovEax(Code c, uint callVa)
    {
        for (uint back = 5; back <= 5; back++)
        {
            Insn m; if (c.Insns.TryGetValue(callVa - back, out m) && !m.Two && m.Op == 0xB8) return (int)m.Fields[0].Value;
        }
        return -1;
    }
}
