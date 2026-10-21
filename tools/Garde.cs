// Garde : lance Zeus sous un debogueur minimal et rend inaccessibles (PAGE_NOACCESS) les anciens emplacements
// des tableaux deplaces. Tout acces a un ancien tableau = reference oubliee : journalise (adresse du code).
// Les acces aux voisins qui partagent une page sont executes pas a pas puis la protection est remise.
// Usage (32 bits) : Garde.exe <Zeus.exe> <journal.txt> [lo-hi ...]   (plages en hexa, par defaut celles du patch)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class Garde
{
    [StructLayout(LayoutKind.Sequential)] struct STARTUPINFO { public int cb; public IntPtr r, d, t; public int x, y, w, h, cx, cy, fa, fl; public short sw, cb2; public IntPtr r2, i, o, e; }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int pid, tid; }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inh, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern bool WaitForDebugEvent(byte[] ev, uint ms);
    [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
    [DllImport("kernel32.dll")] static extern bool VirtualProtectEx(IntPtr h, IntPtr a, UIntPtr size, uint prot, out uint old);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inh, int tid);
    [DllImport("kernel32.dll")] static extern bool GetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern bool SetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    const uint DEBUG_ONLY_THIS_PROCESS = 2, DBG_CONTINUE = 0x00010002, DBG_EXCEPTION_NOT_HANDLED = 0x80010001;
    const uint PAGE_NOACCESS = 1, PAGE_READWRITE = 4;

    static List<uint[]> ranges = new List<uint[]>(), watches = new List<uint[]>();
    static Dictionary<uint, int> watchHits = new Dictionary<uint, int>();
    static HashSet<uint> pages = new HashSet<uint>();
    static IntPtr proc;
    static StreamWriter log;
    static Dictionary<uint, int> hits = new Dictionary<uint, int>();      // eip -> nombre d'acces a un ancien tableau
    static Dictionary<int, List<uint>> pending = new Dictionary<int, List<uint>>();   // thread -> pages a reproteger
    static long neighbours;

    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] buf, int n, out int read);

    // Plantage dans le code du jeu : registres et adresses de retour presentes sur la pile
    static void Dump(int tid, byte[] ctx)
    {
        IntPtr th = OpenThread(0x1FFFFF, false, tid);
        BitConverter.GetBytes(0x10007).CopyTo(ctx, 0);   // CONTEXT_FULL
        GetThreadContext(th, ctx); CloseHandle(th);
        string[] names = { "edi", "esi", "ebx", "edx", "ecx", "eax", "ebp" };
        var sb = new StringBuilder("  registres :");
        for (int i = 0; i < names.Length; i++) sb.AppendFormat(" {0}=0x{1:X}", names[i], BitConverter.ToUInt32(ctx, 0x9C + 4 * i));
        uint esp = BitConverter.ToUInt32(ctx, 0xC4);
        sb.AppendFormat(" esp=0x{0:X}", esp);
        log.WriteLine(sb.ToString());
        byte[] st = new byte[1024]; int rd;
        if (ReadProcessMemory(proc, (IntPtr)esp, st, st.Length, out rd))
        {
            sb = new StringBuilder("  pile (adresses dans le code) :");
            for (int i = 0; i + 4 <= rd; i += 4) { uint v = BitConverter.ToUInt32(st, i); if (v >= 0x401000 && v < 0x5D8000) sb.AppendFormat(" +{0:X}:0x{1:X}", i, v); }
            log.WriteLine(sb.ToString());
            sb = new StringBuilder("  pile (32 premiers dwords) :");
            for (int i = 0; i < 128 && i + 4 <= rd; i += 4) sb.AppendFormat(" {0:X}", BitConverter.ToUInt32(st, i));
            log.WriteLine(sb.ToString());
        }
    }

    // Releve : walkers (octet 14) et batiments (octet 0) en service, aux adresses de la table de sauvegarde
    static int maxW, maxB;
    static void Count()
    {
        byte[] t = new byte[20 * 8]; int rd;
        if (!ReadProcessMemory(proc, (IntPtr)(0x63C2D0 + 28 * 20), t, 20, out rd)) return;
        uint wa = BitConverter.ToUInt32(t, 4); int wn = BitConverter.ToInt32(t, 12);
        if (!ReadProcessMemory(proc, (IntPtr)(0x63C2D0 + 35 * 20), t, 20, out rd)) return;
        uint ba = BitConverter.ToUInt32(t, 4); int bn = BitConverter.ToInt32(t, 12);
        byte[] w = new byte[388 * wn], b = new byte[280 * bn];
        if (!ReadProcessMemory(proc, (IntPtr)wa, w, w.Length, out rd) || !ReadProcessMemory(proc, (IntPtr)ba, b, b.Length, out rd)) return;
        int nw = 0, nb = 0, hw = 0, hb = 0;
        int hi2000 = 0;
        for (int i = 0; i < wn; i++) if (w[i * 388 + 14] != 0) { nw++; hw = i; if (i >= 2000) hi2000++; }
        for (int i = 0; i < bn; i++) if (b[i * 280] != 0) { nb++; hb = i; }
        if (nw == 0 && nb == 0) return;
        if (nw > maxW) maxW = nw; if (nb > maxB) maxB = nb;
        // file des numeros libres : adresse lue dans mov ebp, [eax*4+file] (0x4D9FE6), taille dans cmp eax, n (0x4D9FEE) ;
        // avec l'attribution du plus petit numero (detour jmp en 0x4D9FE1) : dans la routine (Limits.LowestFree, +17 et +29)
        string qs = ""; byte[] v = new byte[4];
        uint qaAt = 0x4D9FE9, qnAt = 0x4D9FEF;
        byte[] j = new byte[5];
        if (ReadProcessMemory(proc, (IntPtr)0x4D9FE1, j, 5, out rd) && j[0] == 0xE9)
        {
            uint z = 0x4D9FE6 + BitConverter.ToUInt32(j, 1);
            qaAt = z + 17; qnAt = z + 29;
        }
        if (ReadProcessMemory(proc, (IntPtr)qaAt, v, 4, out rd))
        {
            uint qa = BitConverter.ToUInt32(v, 0);
            ReadProcessMemory(proc, (IntPtr)qnAt, v, 4, out rd); int qn = BitConverter.ToInt32(v, 0);
            byte[] q3 = new byte[12]; ReadProcessMemory(proc, (IntPtr)0xDBB840, q3, 12, out rd);
            int tail = BitConverter.ToInt32(q3, 0), head = BitConverter.ToInt32(q3, 4), cnt = BitConverter.ToInt32(q3, 8);
            byte[] qd = new byte[4 * qn]; int stale = 0, bad = 0;
            if (ReadProcessMemory(proc, (IntPtr)qa, qd, qd.Length, out rd))
                for (int k = 0; k < cnt && k < qn; k++)
                {
                    int id = BitConverter.ToInt32(qd, 4 * ((tail + k) % qn));
                    if (id <= 0 || id >= wn) bad++; else if (w[id * 388 + 14] != 0) stale++;
                }
            qs = string.Format(" ; file : {0} libres (debut {1}, fin {2}, taille {3}), {4} deja occupes, {5} invalides", cnt, tail, head, qn, stale, bad);
        }
        // autres goulots possibles : reservoir 0xDACC50 (nombre en +0x9C50), e134 (nombre en 0x7796D0), chemins (bloc 29 : 1000 mots)
        int pool = -1, e134 = -1, paths = 0;
        if (ReadProcessMemory(proc, (IntPtr)0xDB68A0, v, 4, out rd)) pool = BitConverter.ToInt32(v, 0);
        if (ReadProcessMemory(proc, (IntPtr)0x7796D0, v, 4, out rd)) e134 = BitConverter.ToInt32(v, 0);
        if (!ReadProcessMemory(proc, (IntPtr)(0x63C2D0 + 29 * 20), t, 20, out rd)) return;
        uint pa = BitConverter.ToUInt32(t, 4); int pn = BitConverter.ToInt32(t, 12);
        byte[] pw = new byte[2 * pn];
        if (ReadProcessMemory(proc, (IntPtr)pa, pw, pw.Length, out rd)) for (int i = 0; i < pw.Length; i += 2) if (BitConverter.ToUInt16(pw, i) != 0) paths++;
        log.WriteLine("{0:HH:mm:ss} releve : {1} walkers (n° max {2}/{3}, {14} n° >= 2000), {4} batiments (n° max {5}/{6}) ; maximum vu {7} / {8} ; reservoir {9}/2000, e134 {10}/2000, chemins {11}/{12}{13}", DateTime.Now, nw, hw, wn, nb, hb, bn, maxW, maxB, pool, e134, paths, pn, qs, hi2000);
    }

    static bool InOld(uint a) { foreach (uint[] r in ranges) if (a >= r[0] && a < r[1]) return true; return false; }

    static void Protect(uint page, uint prot) { uint old; VirtualProtectEx(proc, (IntPtr)page, (UIntPtr)4096u, prot, out old); }

    public static int Main(string[] a)
    {
        if (IntPtr.Size != 4) { Console.WriteLine("compiler en 32 bits (/platform:x86)"); return 2; }
        string exe = Path.GetFullPath(a[0]);
        log = new StreamWriter(a[1], false, Encoding.UTF8) { AutoFlush = true };
        for (int i = 2; i < a.Length; i++)
        {
            bool w = a[i].StartsWith("watch:"); string[] p = (w ? a[i].Substring(6) : a[i]).Split((char)45);
            (w ? watches : ranges).Add(new[] { Convert.ToUInt32(p[0], 16), Convert.ToUInt32(p[1], 16) });
        }
        if (ranges.Count == 0)

        {
            ranges.Add(new uint[] { 0xDBB84C, 0xEF57B8 });   // file des walkers libres + walkers + chemins + proprietaires
            ranges.Add(new uint[] { 0x92E658, 0xB1E8B4 });   // batiments + tableaux par batiment
            ranges.Add(new uint[] { 0x91F5B8, 0x9214F8 });   // liste de batiments (e73)
        }
        // seulement les pages entierement dans une plage : une page partagee avec un voisin lu par ReadFile
        // ferait echouer la lecture sans exception (donnees chargees incompletes)
        foreach (uint[] r in ranges) for (uint p = (r[0] + 0xFFF) & ~0xFFFu; p + 4096 <= r[1]; p += 4096) pages.Add(p);
        foreach (uint[] r in watches) for (uint p = r[0] & ~0xFFFu; p < r[1]; p += 4096) pages.Add(p);

        var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(si);
        PROCESS_INFORMATION pi;
        if (!CreateProcess(exe, "\"" + exe + "\"", IntPtr.Zero, IntPtr.Zero, false, DEBUG_ONLY_THIS_PROCESS, IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out pi))
        { Console.WriteLine("CreateProcess : erreur " + Marshal.GetLastWin32Error()); return 1; }
        proc = pi.hProcess;
        log.WriteLine("{0:HH:mm:ss} lancement {1}, {2} pages gardees", DateTime.Now, exe, pages.Count);
        bool protectedOnce = false; double nextCount = 0;
        var sw = Stopwatch.StartNew();
        byte[] ev = new byte[96];
        byte[] ctx = new byte[716 + 16];
        while (true)
        {
            if (protectedOnce && sw.Elapsed.TotalSeconds >= nextCount) { Count(); nextCount = sw.Elapsed.TotalSeconds + 10; }
            if (!WaitForDebugEvent(ev, 1000)) continue;
            int code = BitConverter.ToInt32(ev, 0), pid = BitConverter.ToInt32(ev, 4), tid = BitConverter.ToInt32(ev, 8);
            uint status = DBG_CONTINUE;
            if (code == 1)   // EXCEPTION_DEBUG_EVENT
            {
                uint exc = BitConverter.ToUInt32(ev, 12), eip = BitConverter.ToUInt32(ev, 24);
                uint first = BitConverter.ToUInt32(ev, 12 + 80);   // dwFirstChance (apres EXCEPTION_RECORD de 80 octets)
                if (exc == 0x80000003 || exc == 0x4000001F)
                {
                    // point d'arret initial du chargeur : on protege maintenant (la section est chargee)
                    if (!protectedOnce) { foreach (uint p in pages) Protect(p, PAGE_NOACCESS); protectedOnce = true; log.WriteLine("pages protegees"); }
                }
                else if (exc == 0xC0000005)
                {
                    uint addr = BitConverter.ToUInt32(ev, 36);   // ExceptionInformation[1] (EXCEPTION_RECORD a +12, informations a +20)
                    uint page = addr & ~0xFFFu;
                    if (pages.Contains(page))
                    {
                        bool watched = false; foreach (uint[] r in watches) if (addr >= r[0] && addr < r[1]) watched = true;
                        if (watched && BitConverter.ToUInt32(ev, 32) == 1)
                        {
                            int n; watchHits.TryGetValue(eip, out n); watchHits[eip] = n + 1;
                            if (n < 3) { log.WriteLine("{0:HH:mm:ss} ECRITURE SURVEILLEE : code 0x{1:X} -> 0x{2:X}", DateTime.Now, eip, addr); Dump(tid, ctx); }
                        }
                        if (InOld(addr))
                        {
                            int n; hits.TryGetValue(eip, out n); hits[eip] = n + 1;
                            if (n == 0) log.WriteLine("{0:HH:mm:ss} OUBLI : code 0x{1:X} -> 0x{2:X} ({3})", DateTime.Now, eip, addr, BitConverter.ToUInt32(ev, 32) == 1 ? "ecriture" : "lecture");
                        }
                        else neighbours++;
                        Protect(page, PAGE_READWRITE);
                        if (!pending.ContainsKey(tid)) pending[tid] = new List<uint>();
                        pending[tid].Add(page);
                        IntPtr th = OpenThread(0x1FFFFF, false, tid);
                        BitConverter.GetBytes(0x10001).CopyTo(ctx, 0);   // CONTEXT_CONTROL
                        GetThreadContext(th, ctx);
                        uint fl = BitConverter.ToUInt32(ctx, 0xC0); BitConverter.GetBytes(fl | 0x100).CopyTo(ctx, 0xC0);   // TF
                        SetThreadContext(th, ctx); CloseHandle(th);
                    }
                    else
                    {
                        log.WriteLine("{0:HH:mm:ss} ACCES INVALIDE ({1}) : code 0x{2:X} -> 0x{3:X}", DateTime.Now, first != 0 ? "1re chance" : "PLANTAGE", eip, addr);
                        if (first != 0 && eip >= 0x401000 && eip < 0x5D8000) Dump(tid, ctx);
                        status = DBG_EXCEPTION_NOT_HANDLED;
                    }
                }
                else if (exc == 0x80000004 && pending.ContainsKey(tid))
                {
                    foreach (uint p in pending[tid]) Protect(p, PAGE_NOACCESS);
                    pending.Remove(tid);
                }
                else
                {
                    if (first == 0) log.WriteLine("{0:HH:mm:ss} EXCEPTION 0x{1:X} non geree en 0x{2:X}", DateTime.Now, exc, eip);
                    status = DBG_EXCEPTION_NOT_HANDLED;
                }
            }
            else if (code == 5)   // EXIT_PROCESS
            {
                log.WriteLine("{0:HH:mm:ss} fin du jeu (code {1}) apres {2:F0} s", DateTime.Now, BitConverter.ToUInt32(ev, 12), sw.Elapsed.TotalSeconds);
                ContinueDebugEvent(pid, tid, DBG_CONTINUE);
                break;
            }
            ContinueDebugEvent(pid, tid, status);
        }
        log.WriteLine("acces aux voisins (pas a pas) : {0}", neighbours);
        log.WriteLine("references oubliees : {0} instruction(s)", hits.Count);
        foreach (var kv in hits) log.WriteLine("  0x{0:X} : {1} acces", kv.Key, kv.Value);
        log.Close();
        return 0;
    }
}
