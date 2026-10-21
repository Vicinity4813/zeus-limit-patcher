// Analyse du code de Zeus.exe : sections PE et descente recursive (puis decodage des trous).
// Partagee par l'outil de rapport (Refs) et le patcheur.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class Sec { public string Name; public uint Va, VSize, Raw, RSize; public int Header; }

public class Code
{
    public byte[] F; public uint ImageBase, Entry; public int Pe, NumSec, OptSize;
    public List<Sec> Secs = new List<Sec>(); public Sec Text;
    public SortedDictionary<uint, Insn> Insns = new SortedDictionary<uint, Insn>();
    public HashSet<uint> Spec = new HashSet<uint>();          // instructions decodees depuis un trou (moins sures)
    public HashSet<uint> Funcs = new HashSet<uint>();
    public Dictionary<int, uint> Owner = new Dictionary<int, uint>();   // offset -> instruction qui le couvre
    public List<string> JumpTables = new List<string>();
    public int Bad;

    public int ToOff(uint va) { foreach (Sec s in Secs) if (va >= s.Va && va < s.Va + Math.Min(s.VSize, s.RSize)) return (int)(va - s.Va + s.Raw); return -1; }
    public bool InText(uint va) { return va >= Text.Va && va < Text.Va + Text.VSize; }

    public static Code Load(byte[] f)
    {
        Code c = new Code(); c.F = f;
        c.Pe = BitConverter.ToInt32(f, 0x3C);
        c.NumSec = BitConverter.ToUInt16(f, c.Pe + 6); c.OptSize = BitConverter.ToUInt16(f, c.Pe + 20);
        c.ImageBase = BitConverter.ToUInt32(f, c.Pe + 24 + 28);
        c.Entry = BitConverter.ToUInt32(f, c.Pe + 24 + 16) + c.ImageBase;
        for (int i = 0; i < c.NumSec; i++)
        {
            int s = c.Pe + 24 + c.OptSize + i * 40;
            Sec x = new Sec { Header = s, Name = Encoding.ASCII.GetString(f, s, 8).TrimEnd('\0'), VSize = BitConverter.ToUInt32(f, s + 8), Va = BitConverter.ToUInt32(f, s + 12) + c.ImageBase, RSize = BitConverter.ToUInt32(f, s + 16), Raw = BitConverter.ToUInt32(f, s + 20) };
            c.Secs.Add(x); if (c.Text == null) c.Text = x;
        }
        c.Analyze();
        return c;
    }

    // Departs : point d'entree et appels directs = surs (decodes d'office) ; pointeurs trouves dans les donnees,
    // immediats et trous = peu surs, decodes "en transaction" : au premier opcode invalide ou chevauchement,
    // tout ce qui vient de ce depart est abandonne (evite que du decodage bidon bloque le vrai code).
    // Reparation : une transaction abandonnee pour chevauchement avec du decodage peu sur est rejouee ; si elle
    // est propre et plus grande que ce qu'elle chevauche, ces transactions-la sont evincees.
    class Tx { public List<uint> Vas = new List<uint>(); public List<int> Offs = new List<int>(); }
    class Result
    {
        public bool Ok = true; public string Reason; public uint FailVa;
        public Dictionary<uint, Insn> Ins = new Dictionary<uint, Insn>(); public Dictionary<int, uint> Own = new Dictionary<int, uint>();
        public List<uint> Calls = new List<uint>(), Ptrs = new List<uint>(); public List<string> Tables = new List<string>();
        public HashSet<int> Conflicts = new HashSet<int>();
    }
    List<Tx> Txs = new List<Tx> { null };                       // 0 = decodage sur
    Dictionary<int, int> OwnTx = new Dictionary<int, int>();    // offset -> transaction
    HashSet<int> triedGap = new HashSet<int>();
    List<uint> failed = new List<uint>();
    public int Aborted, Repaired;
    public List<string> AbortLog = new List<string>();          // etendue, racine et raison de chaque abandon

    void Analyze()
    {
        var sure = new Stack<uint>(); sure.Push(Entry);
        var weak = new WeakQueue();
        foreach (Sec s in Secs)
        {
            if (s == Text) continue;
            for (uint o = 0; o + 4 <= Math.Min(s.RSize, s.VSize); o += 4)
            { uint v = BitConverter.ToUInt32(F, (int)(s.Raw + o)); if (InText(v)) weak.Enqueue(v); }
        }
        Passes(sure, weak, 0);
        for (int round = 0; round < 6; round++)
        {
            bool changed = false;
            foreach (uint root in failed.ToArray())
            {
                Result r = Explore(root, true, true);
                if (!r.Ok || r.Conflicts.Count == 0) continue;
                int sz = 0; foreach (int t in r.Conflicts) sz += Txs[t].Vas.Count;
                if (r.Ins.Count <= sz) continue;
                foreach (int t in r.Conflicts) Evict(t);
                Commit(r, true, true, sure, weak); Repaired++; changed = true;
            }
            if (!changed) break;
            failed.Clear(); triedGap.Clear();
            Passes(sure, weak, 1);
        }
    }

    void Passes(Stack<uint> sure, WeakQueue weak, int first)
    {
        for (int pass = first; pass < 8; pass++)
        {
            if (pass > 0)
            {
                // trous : debut apres un remplissage CC/90/00
                int seeds = 0;
                int end = (int)(Text.Raw + Math.Min(Text.VSize, Text.RSize));
                for (int o = (int)Text.Raw; o < end; o++)
                {
                    if (Owner.ContainsKey(o)) continue;
                    int g = o; while (g < end && !Owner.ContainsKey(g) && (F[g] == 0xCC || F[g] == 0x90 || F[g] == 0)) g++;
                    if (g < end && !Owner.ContainsKey(g) && !triedGap.Contains(g)) { triedGap.Add(g); weak.Enqueue((uint)(g - Text.Raw + Text.Va)); seeds++; }
                    while (o < end && !Owner.ContainsKey(o)) o++;
                }
                if (seeds == 0) break;
            }
            while (sure.Count > 0 || weak.Count > 0)
            {
                while (sure.Count > 0) Commit(Explore(sure.Pop(), false, false), false, pass > 0, sure, weak);
                if (weak.Count > 0)
                {
                    uint root = weak.Dequeue();
                    Result r = Explore(root, true, false);
                    if (r.Ok) Commit(r, true, pass > 0, sure, weak);
                    else
                    {
                        Aborted++;
                        uint lo = root, hi = root; foreach (uint k in r.Ins.Keys) { if (k < lo) lo = k; if (k > hi) hi = k; }
                        AbortLog.Add(string.Format("{0:X} {1:X} racine {2:X} : {3} en {4:X}", lo, hi, root, r.Reason, r.FailVa));
                        if (r.Reason != "opcode inconnu") failed.Add(root);
                    }
                }
            }
        }
    }

    // En reparation, les octets d'une transaction peu sure comptent comme libres (et sont notes en conflit).
    bool Taken(int off, bool repair, Result r)
    {
        if (!Owner.ContainsKey(off)) return false;
        int t; if (repair && OwnTx.TryGetValue(off, out t) && t > 0) { r.Conflicts.Add(t); return false; }
        return true;
    }

    Result Explore(uint root, bool tx, bool repair)
    {
        var r = new Result();
        var todo = new Stack<uint>(); todo.Push(root);
        while (todo.Count > 0)
        {
            uint va = todo.Pop();
            while (true)
            {
                if (!InText(va) || r.Ins.ContainsKey(va)) break;
                int off = ToOff(va);
                if (off < 0) break;
                if (Insns.ContainsKey(va) && (!repair || Taken(off, repair, r))) break;     // deja decode
                string fail = null; Insn n = null;
                if (r.Own.ContainsKey(off) || Taken(off, repair, r)) fail = "milieu d'instruction";
                else
                {
                    n = X86.Decode(F, off, va);
                    if (n == null) fail = "opcode inconnu";
                    else for (int k = 0; k < n.Len; k++) if (r.Own.ContainsKey(off + k) || Taken(off + k, repair, r)) { fail = "chevauchement"; break; }
                }
                if (fail != null)
                {
                    if (tx) { r.Ok = false; r.Reason = fail; r.FailVa = va; return r; }
                    Bad++; break;
                }
                for (int k = 0; k < n.Len; k++) r.Own[off + k] = va;
                r.Ins[va] = n;
                if (n.Call && !n.IndirectCall) r.Calls.Add(n.Target);
                if (n.Jump) todo.Push(n.Target);
                foreach (Field f in n.Fields) if (f.Size == 4 && InText(f.Value) && f.Kind == 'i') r.Ptrs.Add(f.Value);
                // table de sauts : jmp [reg*4 + table]
                if (n.IndirectJump && n.Sib && n.Scale == 4 && n.Mod == 0 && n.Base == 5 && n.Fields.Count > 0)
                {
                    uint t = n.Fields[0].Value; int c = 0;
                    while (c < 1024)
                    {
                        int to = ToOff(t + (uint)(4 * c)); if (to < 0) break;
                        uint v = BitConverter.ToUInt32(F, to); if (!InText(v)) break;
                        if (c > 0 && (Insns.ContainsKey(t + (uint)(4 * c)) || r.Ins.ContainsKey(t + (uint)(4 * c)))) break;
                        todo.Push(v); for (int k = 0; k < 4; k++) r.Own[to + k] = 0; c++;
                    }
                    r.Tables.Add(string.Format("0x{0:X} table 0x{1:X} x{2}", va, t, c));
                }
                if (n.Stop) break;
                va += (uint)n.Len;
            }
        }
        return r;
    }

    void Commit(Result r, bool tx, bool spec, Stack<uint> sure, WeakQueue weak)
    {
        int id = 0;
        if (tx) { Txs.Add(new Tx()); id = Txs.Count - 1; }
        foreach (var kv in r.Ins) { Insns[kv.Key] = kv.Value; if (spec) Spec.Add(kv.Key); if (id > 0) Txs[id].Vas.Add(kv.Key); }
        foreach (var kv in r.Own) { Owner[kv.Key] = kv.Value; OwnTx[kv.Key] = id; if (id > 0) Txs[id].Offs.Add(kv.Key); }
        JumpTables.AddRange(r.Tables);
        foreach (uint t in r.Calls) { Funcs.Add(t); if (tx) weak.Enqueue(t); else sure.Push(t); }
        foreach (uint p in r.Ptrs) weak.Enqueue(p);
    }

    void Evict(int id)
    {
        Tx t = Txs[id];
        foreach (uint va in t.Vas) { Insns.Remove(va); Spec.Remove(va); }
        foreach (int o in t.Offs) { int cur; if (OwnTx.TryGetValue(o, out cur) && cur == id) { Owner.Remove(o); OwnTx.Remove(o); } }
        t.Vas.Clear(); t.Offs.Clear();
    }

    public string Text_(uint va) { Insn n; return Insns.TryGetValue(va, out n) ? Dis.Text(n, F, ToOff(va)) : "?"; }
}

// Departs peu surs : ceux alignes sur 16 octets (debuts de fonction MSVC) passent avant les autres.
public class WeakQueue
{
    Queue<uint> a = new Queue<uint>(), b = new Queue<uint>();
    public int Count { get { return a.Count + b.Count; } }
    public void Enqueue(uint v) { if ((v & 15) == 0) a.Enqueue(v); else b.Enqueue(v); }
    public uint Dequeue() { return a.Count > 0 ? a.Dequeue() : b.Dequeue(); }
}
