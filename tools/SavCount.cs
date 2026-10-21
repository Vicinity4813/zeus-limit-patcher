// SavCount : compte les walkers (entree 28, en service si octet 14 != 0) et les batiments (entree 35,
// en service si octet 0 != 0) de la premiere partie de chaque sauvegarde.
// Usage : SavCount.exe <fichier.sav | dossier> ...
using System;
using System.IO;
using ZeusPak;

public static class SavCount
{
    static byte[] Block(byte[] f, int want, out int size, out int count)
    {
        int p = 8 + 300 * 20; size = count = 0;
        for (int i = 3; i < 300; i++)
        {
            int e = 8 + i * 20;
            uint comp = BitConverter.ToUInt32(f, e), sz = BitConverter.ToUInt32(f, e + 8), n = BitConverter.ToUInt32(f, e + 12);
            if (sz == 0) return null;
            int raw = (int)(sz * n); byte[] data = null;
            if (comp != 0)
            {
                int cs = BitConverter.ToInt32(f, p); p += 4;
                if (cs < 0) { if (i == want) data = MapFile.Sub(f, p, raw); p += raw; }
                else { if (i == want) data = Pk.Explode(MapFile.Sub(f, p, cs)); p += cs; }
            }
            else { if (i == want) data = MapFile.Sub(f, p, raw); p += raw; }
            if (i == want) { size = (int)sz; count = (int)n; return data; }
        }
        return null;
    }

    public static int Main(string[] a)
    {
        foreach (string arg in a)
        {
            string[] files = Directory.Exists(arg) ? Directory.GetFiles(arg, "*.sav", SearchOption.AllDirectories) : new[] { arg };
            foreach (string path in files)
            {
                try
                {
                    byte[] f = File.ReadAllBytes(path);
                    int ws, wn, bs, bn;
                    byte[] w = Block(f, 28, out ws, out wn), b = Block(f, 35, out bs, out bn);
                    if (w == null || b == null || ws != 388 || bs != 280) { Console.WriteLine("{0} : format inattendu", path); continue; }
                    int nw = 0, maxw = 0, nb = 0, maxb = 0;
                    for (int i = 0; i < wn; i++) if (w[i * ws + 14] != 0) { nw++; maxw = i; }
                    for (int i = 0; i < bn; i++) if (b[i * bs] != 0) { nb++; maxb = i; }
                    Console.WriteLine("{0,5} walkers (max n°{1,4}/{2})  {3,5} batiments (max n°{4,4}/{5})  {6}", nw, maxw, wn, nb, maxb, bn, path);
                }
                catch (Exception ex) { Console.WriteLine("{0} : {1}", path, ex.Message); }
            }
        }
        return 0;
    }
}
