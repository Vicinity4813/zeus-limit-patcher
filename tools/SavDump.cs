// SavDump : lit tous les blocs d'une sauvegarde (premiere partie) et affiche un bloc.
// Usage : SavDump.exe <fichier.sav> <entree> [taille d'element a afficher en hexa par element]
using System;
using System.IO;
using ZeusPak;

public static class SavDump
{
    public static int Main(string[] a)
    {
        byte[] f = File.ReadAllBytes(a[0]);
        int want = int.Parse(a[1]), p = 8 + 300 * 20;
        for (int i = 3; i < 300; i++)
        {
            int e = 8 + i * 20;
            uint comp = BitConverter.ToUInt32(f, e), size = BitConverter.ToUInt32(f, e + 8), count = BitConverter.ToUInt32(f, e + 12);
            if (size == 0) break;
            int raw = (int)(size * count); byte[] data;
            if (comp != 0)
            {
                int cs = BitConverter.ToInt32(f, p); p += 4;
                if (cs < 0) { data = MapFile.Sub(f, p, raw); p += raw; }
                else { data = Pk.Explode(MapFile.Sub(f, p, cs)); p += cs; }
            }
            else { data = MapFile.Sub(f, p, raw); p += raw; }
            if (i != want) continue;
            Console.WriteLine("entree {0} : {1} x {2}, {3} octets", i, size, count, data.Length);
            if (a.Length > 2 && a[2] == "u16") { int nz = 0, mx = 0; for (int k = 0; k + 1 < data.Length; k += 2) { int v = BitConverter.ToUInt16(data, k); if (v != 0) { nz++; mx = k / 2; } } Console.WriteLine("mots non nuls : {0}, dernier n {1}", nz, mx); return 0; }
            if (a.Length > 3 && a[2] == "champ16") { int off = int.Parse(a[3]), nz = 0, mx = 0; for (int k = 0; k < count; k++) { int v = BitConverter.ToInt16(data, k * (int)size + off); if (v != 0) nz++; if (v > mx) mx = v; } Console.WriteLine("champ +{0} non nul : {1} elements, valeur max {2}", off, nz, mx); return 0; }
            for (int k = 0; k < count && k < 64; k++)
            {
                Console.Write("{0,3}:", k);
                for (int b = 0; b < size && b < 64; b += 4) Console.Write(" {0:X8}", BitConverter.ToUInt32(data, k * (int)size + b));
                Console.WriteLine();
            }
            return 0;
        }
        return 1;
    }
}
