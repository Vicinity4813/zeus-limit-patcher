// Test hors jeu de la routine d'attribution du plus petit numero de walker libre (Limits.LowestFree) :
// la routine est generee vers une fausse file en memoire executable, puis executee pour de vrai (processus
// 32 bits) sur des files tirees au hasard ; le resultat est compare au modele attendu.
// Usage : LowestTest.exe [nombre d'essais]
using System;
using System.Runtime.InteropServices;

public static class LowestTest
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr size, uint type, uint protect);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Run();

    const int Code = 0, Harness = 0x700, Ret = 0x7F0, Head = 0x800, Count = 0x808, OutEbp = 0x810, OutEsi = 0x814, Queue = 0x1000;

    public static int Main(string[] a)
    {
        if (IntPtr.Size != 4) { Console.WriteLine("processus 32 bits requis"); return 2; }
        int tries = a.Length > 0 ? int.Parse(a[0]) : 20000;
        IntPtr mem = VirtualAlloc(IntPtr.Zero, (UIntPtr)0x10000, 0x3000, 0x40);
        uint A = (uint)mem.ToInt32();
        var rnd = new Random(12345);
        int bad = 0;
        foreach (int cap in new[] { 3999, 1999, 7 })
        {
            byte[] code = Limits.LowestFree(A + Code, A + Head, A + Count, A + Queue, (uint)cap, A + Ret);
            Marshal.Copy(code, 0, mem, code.Length);
            Marshal.WriteByte(mem, Ret, 0xC3);
            // push ebp ; push ebx ; push esi ; xor ebx,ebx ; mov esi,0x12345678 ; call code ; mov [OutEbp],ebp ; mov [OutEsi],esi ; pop esi ; pop ebx ; pop ebp ; ret
            var h = new System.Collections.Generic.List<byte> { 0x55, 0x53, 0x56, 0x33, 0xDB, 0xBE, 0x78, 0x56, 0x34, 0x12, 0xE8 };
            h.AddRange(BitConverter.GetBytes((A + Code) - (A + Harness + (uint)h.Count + 4)));
            h.Add(0x89); h.Add(0x2D); h.AddRange(BitConverter.GetBytes(A + OutEbp));
            h.Add(0x89); h.Add(0x35); h.AddRange(BitConverter.GetBytes(A + OutEsi));
            h.AddRange(new byte[] { 0x5E, 0x5B, 0x5D, 0xC3 });
            Marshal.Copy(h.ToArray(), 0, mem + Harness, h.Count);
            var run = (Run)Marshal.GetDelegateForFunctionPointer(mem + Harness, typeof(Run));

            for (int t = 0; t < tries; t++)
            {
                int n = t % 10 == 0 ? cap : t % 10 == 1 ? 1 : rnd.Next(1, cap + 1);
                int head = t % 10 == 2 ? cap - 1 : rnd.Next(cap);
                int[] q = new int[cap];
                for (int i = 0; i < cap; i++) q[i] = rnd.Next(1, 4001);          // hors de la fenetre : sans effet
                // numeros distincts dans la fenetre (melange de 1..cap+1), parfois un 0 (refuse par le jeu)
                int[] pool = new int[cap + 1]; for (int i = 0; i < pool.Length; i++) pool[i] = i + 1;
                for (int i = pool.Length - 1; i > 0; i--) { int j = rnd.Next(i + 1); int x = pool[i]; pool[i] = pool[j]; pool[j] = x; }
                for (int k = 0; k < n; k++) q[(head + k) % cap] = pool[k];
                if (t % 7 == 3) q[(head + rnd.Next(n)) % cap] = 0;
                // modele : plus petit numero (0 ignore sauf s'il est seul), premier en cas d'egalite
                int best = head;
                for (int k = 1; k < n; k++) { int p = (head + k) % cap; if (unchecked((uint)(q[p] - 1) < (uint)(q[best] - 1))) best = p; }
                int[] exp = (int[])q.Clone(); int min = q[best];
                exp[best] = q[head]; exp[head] = min;
                int expHead = (head + 1) % cap;

                Marshal.Copy(q, 0, mem + Queue, cap);
                Marshal.WriteInt32(mem, Head, head); Marshal.WriteInt32(mem, Count, n);
                run();
                int[] got = new int[cap]; Marshal.Copy(mem + Queue, got, 0, cap);
                string err = null;
                if (Marshal.ReadInt32(mem, OutEbp) != min) err = "numero " + Marshal.ReadInt32(mem, OutEbp) + " au lieu de " + min;
                else if (Marshal.ReadInt32(mem, Head) != expHead) err = "tete " + Marshal.ReadInt32(mem, Head);
                else if (Marshal.ReadInt32(mem, Count) != n) err = "nombre modifie";
                else if (Marshal.ReadInt32(mem, OutEsi) != 0x12345678) err = "esi non preserve";
                else for (int i = 0; i < cap && err == null; i++) if (got[i] != exp[i]) err = "file differente en " + i;
                if (err != null) { bad++; if (bad < 10) Console.WriteLine("ECHEC cap={0} essai {1} (n={2}, tete={3}) : {4}", cap, t, n, head, err); }
            }
            Console.WriteLine("cap {0} : {1} essais, routine de {2} octets", cap, tries, code.Length);
        }
        Console.WriteLine(bad == 0 ? "OK : routine conforme au modele" : "ECHECS : " + bad);
        return bad == 0 ? 0 : 1;
    }
}
