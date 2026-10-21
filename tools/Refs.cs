// Refs : parcourt le code de Zeus.exe (descente recursive) et liste les references aux tableaux
// des walkers et des batiments, ainsi que les constantes liees a leurs limites.
// Usage : Refs.exe <Zeus.exe> <rapport.txt>
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class Refs
{
    static Code C; static byte[] F; static List<Sec> Secs; static Sec Text;
    static int ToOff(uint va) { return C.ToOff(va); }
    static bool InText(uint va) { return C.InText(va); }

    // Plages etudiees : nom, debut, taille d'element, nombre
    public struct Arr { public string Name; public uint Base; public int Size, Count; public uint End { get { return Base + (uint)(Size * Count); } } }

    public static int Main(string[] a)
    {
        C = Code.Load(File.ReadAllBytes(a[0]));
        F = C.F; Secs = C.Secs; Text = C.Text;
        var insns = C.Insns; var spec = C.Spec; var owner = C.Owner; var funcs = C.Funcs; var jumpTables = C.JumpTables; int bad = C.Bad;
        // ---- plages
        var arrs = new List<Arr>
        {
            new Arr { Name = "walkers", Base = 0xDBD788, Size = 388, Count = 2000 },
            new Arr { Name = "batiments", Base = 0x92E658, Size = 280, Count = 4000 },
            new Arr { Name = "e134", Base = 0x7796D4, Size = 16, Count = 2000 },
            new Arr { Name = "e73", Base = 0x91F5B8, Size = 2, Count = 4000 },
            new Arr { Name = "e29", Base = 0xEF4FE8, Size = 2, Count = 1000 },
        };
        Dis.Name = v =>
        {
            foreach (Arr r in arrs)
                if (v >= r.Base && v <= r.End)
                {
                    uint d = v - r.Base;
                    if (v == r.End) return r.Name + ".FIN";
                    return d < (uint)r.Size ? r.Name + "+" + d : r.Name + "[" + d / (uint)r.Size + "]+" + d % (uint)r.Size;
                }
            return null;
        };
        // Mode lecture : Refs.exe exe dump va [nb]  -> instructions autour de va
        if (a[1] == "dump")
        {
            var ks = new List<uint>(insns.Keys);
            int nb = a.Length > 3 ? int.Parse(a[3]) : 12;
            uint at = Convert.ToUInt32(a[2], 16);
            int idx = ks.BinarySearch(at); if (idx < 0) idx = ~idx;
            for (int k = Math.Max(0, idx - nb); k < Math.Min(ks.Count, idx + nb); k++)
            {
                Insn n = insns[ks[k]];
                if (k > 0) { Insn pv = insns[ks[k - 1]]; if (pv.Va + pv.Len != n.Va) Console.WriteLine("        ..."); }
                Console.WriteLine("{0}{1:X} {2}", n.Va == at ? ">" : " ", n.Va, Dis.Text(n, F, ToOff(n.Va)));
            }
            return 0;
        }
        // Mode piles : fonctions dont le cadre de pile depasse 7000 octets (tableaux locaux possibles)
        if (a[1] == "frames")
        {
            var ks2 = new List<uint>(insns.Keys);
            for (int k = 0; k + 1 < ks2.Count; k++)
            {
                Insn m = insns[ks2[k]]; uint size = 0;
                if (!m.Two && m.Op == 0x81 && m.Reg == 5 && m.Mod == 3 && m.Rm == 4) foreach (Field f in m.Fields) if (f.Kind == 'i') size = f.Value;
                if (!m.Two && m.Op == 0xB8 && insns[ks2[k + 1]].Call && !insns[ks2[k + 1]].IndirectCall) foreach (Field f in m.Fields) if (f.Kind == 'i' && f.Value >= 7000 && f.Value < 0x100000) size = f.Value;
                if (size >= 7000 && size < 0x100000) Console.WriteLine("{0:X} cadre {1}", m.Va, size);
            }
            return 0;
        }
        // Mode pourquoi : Refs.exe exe pourquoi va -> transactions abandonnees qui couvraient va
        if (a[1] == "pourquoi")
        {
            uint at = Convert.ToUInt32(a[2], 16); int nb = 0;
            foreach (string s in C.AbortLog)
            {
                string[] p = s.Split(' ');
                uint lo = Convert.ToUInt32(p[0], 16), hi = Convert.ToUInt32(p[1], 16);
                if (at >= lo && at <= hi + 16 && nb++ < 10) Console.WriteLine(s);
            }
            Console.WriteLine("abandons {0}, couvrant {1:X} : {2}", C.Aborted, at, nb);
            return 0;
        }
        // Mode facteurs : multiplications faites par lea/shl (sans constante) donnant un facteur donne (ex. 4000 16000)
        if (a[1] == "facteurs")
        {
            var want = new HashSet<long>(); for (int i = 2; i < a.Length; i++) want.Add(long.Parse(a[i]));
            var fct = new long[8]; for (int r = 0; r < 8; r++) fct[r] = 1;
            uint prevEnd = 0;
            foreach (Insn m in insns.Values)
            {
                if (m.Va != prevEnd) for (int r = 0; r < 8; r++) fct[r] = 1;   // nouvelle suite d'instructions
                prevEnd = m.Va + (uint)m.Len;
                int o = ToOff(m.Va);
                long before = -1; int dst = -1;
                if (!m.Two && m.Op == 0x8D && m.Mod == 0 && m.Sib && m.Index != 4 && m.Base == m.Index && m.Fields.Count == 0)
                { dst = m.Reg; fct[dst] = fct[m.Index] * (m.Scale + 1); }                                   // lea r, [a + a*s]
                else if (!m.Two && m.Op == 0x8D && m.Mod == 0 && m.Sib && m.Index != 4 && m.Base == 5 && m.Fields.Count > 0 && m.Fields[0].Value == 0)
                { dst = m.Reg; fct[dst] = fct[m.Index] * m.Scale; }                                          // lea r, [a*s + 0]
                else if (!m.Two && (m.Op == 0xC1) && m.Mod == 3 && m.Reg == 4)
                { dst = m.Rm; before = fct[dst]; fct[dst] <<= F[o + m.Len - 1]; }                            // shl r, n
                else if (!m.Two && m.Op == 0x8B && m.Mod == 3) { fct[m.Reg] = fct[m.Rm]; }
                else if (!m.Two && (m.Op == 0x03 || m.Op == 0x01 || m.Op == 0x2B || m.Op == 0x29) && m.Mod == 3)
                { int r = (m.Op == 0x03 || m.Op == 0x2B) ? m.Reg : m.Rm; fct[r] = 1; }
                else if (m.Reg >= 0 && m.Mod == 3 && !m.Two && (m.Op == 0x8B || m.Op == 0x8A)) fct[m.Reg] = 1;
                else if (!m.Two && m.Op >= 0xB8 && m.Op <= 0xBF) fct[m.Op - 0xB8] = 1;
                else if (!m.Two && m.Op == 0x8B) fct[m.Reg] = 1;
                else if (m.Call) for (int r = 0; r < 8; r++) fct[r] = 1;
                if (dst >= 0 && want.Contains(fct[dst])) Console.WriteLine("{0:X} {1} -> facteur {2}{3}", m.Va, Dis.Text(m, F, o), fct[dst], spec.Contains(m.Va) ? " [trou]" : "");
            }
            return 0;
        }
        // Mode reste : Refs.exe exe reste lo hi -> toutes les references (champs 32 bits) encore dans [lo, hi)
        if (a[1] == "reste")
        {
            uint lo = Convert.ToUInt32(a[2], 16), hi = Convert.ToUInt32(a[3], 16); int nb = 0;
            foreach (Insn m in insns.Values)
                foreach (Field f in m.Fields)
                    if (f.Size == 4 && f.Value >= lo && f.Value < hi) { nb++; Console.WriteLine("{0:X}{1} {2}", m.Va, spec.Contains(m.Va) ? " [trou]" : "", Dis.Text(m, F, ToOff(m.Va))); }
            Console.WriteLine("total {0}", nb);
            return 0;
        }
        // Mode pile : Refs.exe exe pile va -> objets du cadre (rel par rapport a esp a l'entree)
        if (a[1] == "pile")
        {
            StackFrame sf = StackFrame.Analyze(C, Convert.ToUInt32(a[2], 16));
            Console.WriteLine("cadre {0}, instructions {1}, acces esp {2}", sf.Frame, sf.Depth.Count, sf.Refs.Count);
            foreach (string pb in sf.Problems) Console.WriteLine("  PROBLEME " + pb);
            var g = new SortedDictionary<int, List<StackRef>>();
            foreach (StackRef r in sf.Refs) { if (!g.ContainsKey(r.Rel)) g[r.Rel] = new List<StackRef>(); g[r.Rel].Add(r); }
            foreach (var kv in g)
            {
                int ix = 0, d1 = 0; foreach (StackRef r in kv.Value) { if (r.Indexed) ix++; if (r.DispSize == 1) d1++; }
                Console.WriteLine("  rel {0,7} : {1,3} acces{2}{3}  ex. {4:X}", kv.Key, kv.Value.Count, ix > 0 ? " (" + ix + " indexes)" : "", d1 > 0 ? " [disp8 x" + d1 + "]" : "", kv.Value[0].Va);
            }
            return 0;
        }
        // Mode const : Refs.exe exe const v1 v2 ... (decimal) -> instructions qui ont ces immediats
        if (a[1] == "const")
        {
            for (int i = 2; i < a.Length; i++)
            {
                uint v = uint.Parse(a[i]);
                foreach (Insn m in insns.Values)
                    foreach (Field f in m.Fields)
                        if (f.Kind == 'i' && f.Value == v) Console.WriteLine("{0} : {1:X} {2}", v, m.Va, Dis.Text(m, F, ToOff(m.Va)));
            }
            return 0;
        }
        // Mode ronds : immediats (pas des deplacements) dans une plage, aux valeurs suspectes (multiples de 0x100)
        if (a[1] == "ronds")
        {
            uint lo = Convert.ToUInt32(a[2], 16), hi = Convert.ToUInt32(a[3], 16);
            foreach (Insn m in insns.Values)
                foreach (Field f in m.Fields)
                    if (f.Kind == 'i' && f.Size == 4 && f.Value >= lo && f.Value < hi && (f.Value & 0xFF) == 0)
                        Console.WriteLine("{0:X} {1}", m.Va, Dis.Text(m, F, ToOff(m.Va)));
            return 0;
        }
        // Mode carte : Refs.exe exe map lo hi -> adresses referencees entre lo et hi (indexees seulement)
        if (a[1] == "map")
        {
            uint lo = Convert.ToUInt32(a[2], 16), hi = Convert.ToUInt32(a[3], 16);
            var mp = new SortedDictionary<uint, string>();
            var nb = new Dictionary<uint, int>();
            foreach (Insn m in insns.Values)
                foreach (Field f in m.Fields)
                {
                    if (f.Size != 4 || f.Value < lo || f.Value >= hi) continue;
                    nb[f.Value] = nb.ContainsKey(f.Value) ? nb[f.Value] + 1 : 1;
                    if (m.Sib && m.Index != 4 && f.Kind == 'd') { string s = "x" + m.Scale + " " + m.Va.ToString("X"); if (!mp.ContainsKey(f.Value) || mp[f.Value] == "") mp[f.Value] = s; }
                    else if (!mp.ContainsKey(f.Value)) mp[f.Value] = "";
                }
            uint prev = 0;
            foreach (var kv in mp)
            {
                if (kv.Value == "" && nb[kv.Key] < 3) continue;
                Console.WriteLine("{0:X} (+{1}) n={2} {3}", kv.Key, prev == 0 ? 0 : kv.Key - prev, nb[kv.Key], kv.Value);
                prev = kv.Key;
            }
            return 0;
        }
        var sb = new StringBuilder();
        long codeBytes = owner.Count;
        sb.AppendFormat("instructions {0}, octets de code {1} / .text {2}, echecs {3}, fonctions {4}, tables de sauts {5}\n", insns.Count, codeBytes, Text.VSize, bad, funcs.Count, jumpTables.Count);

        // dwords de .text dans chaque plage : couverts par un champ decode ou non
        var fieldAt = new Dictionary<int, Tuple<Insn, Field>>();
        foreach (Insn n in insns.Values) { int o = ToOff(n.Va); foreach (Field f in n.Fields) fieldAt[o + f.Off] = Tuple.Create(n, f); }
        foreach (Arr r in arrs)
        {
            int inField = 0, inCode = 0, outside = 0, exactEnd = 0, inside = 0; var stray = new List<string>(); var insideAt = new List<string>();
            var hist = new SortedDictionary<int, int>();
            for (int o = (int)Text.Raw; o + 4 <= Text.Raw + Text.RSize; o++)
            {
                uint v = BitConverter.ToUInt32(F, o);
                if (v < r.Base || v > r.End) continue;
                if (fieldAt.ContainsKey(o))
                {
                    if (v == r.End) exactEnd++; else { inField++; int fo = (int)((v - r.Base) % (uint)r.Size); hist[fo] = hist.ContainsKey(fo) ? hist[fo] + 1 : 1; }
                }
                else if (owner.ContainsKey(o)) { inCode++; if (owner.ContainsKey(o + 3) && owner[o] == owner[o + 3] && owner[o] != 0) { inside++; if (insideAt.Count < 20) insideAt.Add(string.Format("0x{0:X}", owner[o])); } }
                else { if (v == r.End) continue; outside++; if (stray.Count < 30) stray.Add(string.Format("0x{0:X}", o - Text.Raw + Text.Va)); }
            }
            sb.AppendFormat("\n[{0}] 0x{1:X}..0x{2:X} : champs {3}, fin exacte {4}, dans du code hors champ {5}, hors code {6}\n", r.Name, r.Base, r.End, inField, exactEnd, inCode, outside);
            if (stray.Count > 0) sb.AppendLine("  hors code : " + string.Join(" ", stray.ToArray()));
            sb.AppendLine("  dans une seule instruction sans etre un champ : " + inside + " " + string.Join(" ", insideAt.ToArray()));
            sb.Append("  champs visés (decalage:nb) : ");
            foreach (var kv in hist) sb.AppendFormat("{0}:{1} ", kv.Key, kv.Value);
            sb.AppendLine();
        }

        // ---- constantes
        uint[] consts = { 2000, 1999, 2001, 4000, 3999, 4001, 8000, 7996, 8004, 16000, 15996, 16004, 12000, 6000, 32000, 194000, 280000, 1999 * 388, 3999 * 280, 7996 / 4 };
        foreach (uint c in consts)
        {
            var at = new List<string>();
            foreach (Insn n in insns.Values) foreach (Field f in n.Fields) if (f.Kind == 'i' && f.Value == c) at.Add(string.Format("0x{0:X}", n.Va));
            sb.AppendFormat("\nconstante {0} : {1} fois\n  {2}\n", c, at.Count, string.Join(" ", at.ToArray()));
        }
        // contexte de chaque constante de limite : instruction + tableaux touches a +-80 octets
        sb.AppendLine("\n==== contexte des constantes de limite");
        var keys = new List<uint>(insns.Keys);
        uint[] lim = { 2000, 1999, 2001, 4000, 3999, 4001 };
        for (int k = 0; k < keys.Count; k++)
        {
            Insn n = insns[keys[k]]; bool hit = false; uint cv = 0;
            foreach (Field f in n.Fields) if (f.Kind == 'i' && Array.IndexOf(lim, f.Value) >= 0) { hit = true; cv = f.Value; }
            if (!hit) continue;
            var near = new SortedSet<string>();
            for (int j = Math.Max(0, k - 25); j < Math.Min(keys.Count, k + 25); j++)
            {
                Insn m = insns[keys[j]]; if (Math.Abs((long)m.Va - n.Va) > 80) continue;
                foreach (Field f in m.Fields) foreach (Arr r in arrs) if (f.Size == 4 && f.Value >= r.Base && f.Value <= r.End) near.Add(r.Name);
            }
            int o = ToOff(n.Va); var hx = new StringBuilder();
            for (int q = 0; q < n.Len; q++) hx.AppendFormat("{0:x2} ", F[o + q]);
            sb.AppendFormat("0x{0:X} {1,4} {2}{3} | {4}\n", n.Va, cv, spec.Contains(n.Va) ? "(trou) " : "", hx.ToString().Trim(), string.Join(",", near));
        }
        // tableaux parcourus par les boucles bornees par une constante de limite (saut arriere juste apres)
        sb.AppendLine("\n==== tableaux dans les boucles bornees");
        var cand = new SortedDictionary<uint, List<string>>();
        for (int k = 0; k + 1 < keys.Count; k++)
        {
            Insn n = insns[keys[k]]; uint cv = 0;
            foreach (Field f in n.Fields) if (f.Kind == 'i' && Array.IndexOf(lim, f.Value) >= 0) cv = f.Value;
            if (cv == 0) continue;
            Insn j = insns[keys[k + 1]]; if (!j.Jump || j.Target >= n.Va) { if (k + 2 < keys.Count) j = insns[keys[k + 2]]; if (!j.Jump || j.Target >= n.Va) continue; }
            for (int q = keys.BinarySearch(j.Target); q >= 0 && q <= k; q++)
            {
                Insn m = insns[keys[q]];
                foreach (Field f in m.Fields)
                {
                    if (f.Kind == 'i' || f.Value < 0x5E8000 || f.Value > 0x160E000) continue;
                    bool known = false; foreach (Arr r in arrs) if (f.Value >= r.Base && f.Value <= r.End) known = true;
                    if (known) continue;
                    if (!cand.ContainsKey(f.Value)) cand[f.Value] = new List<string>();
                    cand[f.Value].Add(string.Format("{0:X}/{1}{2}", m.Va, cv, m.Sib ? "*" + m.Scale : ""));
                }
            }
        }
        foreach (var kv in cand) sb.AppendFormat("0x{0:X} : {1}\n", kv.Key, string.Join(" ", kv.Value.ToArray()));
        // references juste apres la fin (fin + champ) : bornes de boucles par pointeur de champ, ou debut du voisin
        sb.AppendLine("\n==== references dans [fin, fin + taille d'element]");
        foreach (Arr r in arrs)
            foreach (Insn m in insns.Values)
                foreach (Field f in m.Fields)
                    if (f.Size == 4 && f.Value >= r.End && f.Value <= r.End + (uint)r.Size)
                        sb.AppendFormat("{0} 0x{1:X}{2} fin+{3} : {4}\n", r.Name, m.Va, spec.Contains(m.Va) ? " (trou)" : "", f.Value - r.End, Dis.Text(m, F, ToOff(m.Va)));
        // cmp registre, imm au tout debut d'un tableau : peut etre la fin de boucle du tableau precedent
        sb.AppendLine("\n==== cmp reg, imm dans [debut, debut + 400]");
        foreach (Arr r in arrs)
            foreach (Insn m in insns.Values)
            {
                bool cmpImm = !m.Two && ((m.Op == 0x81 && m.Reg == 7 && m.Mod == 3) || m.Op == 0x3D);
                if (!cmpImm) continue;
                foreach (Field f in m.Fields)
                    if (f.Kind == 'i' && f.Size == 4 && f.Value >= r.Base && f.Value < r.Base + 400)
                        sb.AppendFormat("{0} 0x{1:X} debut+{2} : {3}\n", r.Name, m.Va, f.Value - r.Base, Dis.Text(m, F, ToOff(m.Va)));
            }
        // tableaux indexes dont la place jusqu'a l'adresse referencee suivante = 2000 ou 4000 elements
        sb.AppendLine("\n==== tableaux indexes de 2000 / 4000 elements (d'apres la place disponible)");
        var all = new SortedSet<uint>();
        var idxBase = new Dictionary<uint, SortedSet<int>>();
        var where = new Dictionary<uint, List<string>>();
        foreach (Insn m in insns.Values)
            foreach (Field f in m.Fields)
            {
                if (f.Size != 4 || f.Value < 0x5E8000 || f.Value >= 0x160E000) continue;
                all.Add(f.Value);
                if (f.Kind == 'd' && m.Sib && m.Index != 4)
                {
                    if (!idxBase.ContainsKey(f.Value)) { idxBase[f.Value] = new SortedSet<int>(); where[f.Value] = new List<string>(); }
                    idxBase[f.Value].Add(m.Scale); if (where[f.Value].Count < 4) where[f.Value].Add(m.Va.ToString("X"));
                }
            }
        foreach (Sec s in Secs) for (uint o = 0; o + 4 <= Math.Min(s.RSize, s.VSize); o += 4) { uint v = BitConverter.ToUInt32(F, (int)(s.Raw + o)); if (v >= 0x5E8000 && v < 0x160E000) all.Add(v); }
        var sorted = new List<uint>(all);
        foreach (var kv in idxBase)
        {
            int i0 = sorted.BinarySearch(kv.Key); if (i0 < 0 || i0 + 1 >= sorted.Count) continue;
            uint gap = sorted[i0 + 1] - kv.Key;
            foreach (int sc in kv.Value)
            {
                uint nEl = gap / (uint)sc;
                if ((nEl >= 1999 && nEl <= 2001) || (nEl >= 3999 && nEl <= 4001))
                    sb.AppendFormat("0x{0:X} x{1} : place {2} = {3} elements ; ex. {4}\n", kv.Key, sc, gap, nEl, string.Join(" ", where[kv.Key].ToArray()));
            }
        }
        // dwords dans d'autres sections (tables de donnees) pointant dans les plages
        foreach (Sec s in Secs)
        {
            if (s == Text) continue;
            foreach (Arr r in arrs)
            {
                var at = new List<string>();
                for (uint o = 0; o + 4 <= Math.Min(s.RSize, s.VSize); o++)
                { uint v = BitConverter.ToUInt32(F, (int)(s.Raw + o)); if (v >= r.Base && v <= r.End) at.Add(string.Format("0x{0:X}", s.Va + o)); }
                if (at.Count > 0) sb.AppendFormat("\n{0} -> {1} : {2}\n", s.Name, r.Name, string.Join(" ", at.ToArray()));
            }
        }
        File.WriteAllText(a[1], sb.ToString());
        Console.Write(sb.ToString().Split('\n')[0]);
        return 0;
    }
}
