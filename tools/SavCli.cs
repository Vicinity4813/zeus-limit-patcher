// Essai du convertisseur de sauvegardes :
//   SavCli.exe info <fichier.sav | dossier> ...      parties, entrees des limites, relecture aller-retour
//   SavCli.exe diff <a.sav> <b.sav>                  entrees du manifeste qui different (1re partie)
using System;
using System.IO;
using ZeusPak;

public static class SavCli
{
    static readonly int[] Entries = { 28, 29, 30, 35, 73 };

    static string[] Files(string arg)
    {
        return Directory.Exists(arg) ? Directory.GetFiles(arg, "*.sav", SearchOption.AllDirectories) : new[] { arg };
    }

    public static int Main(string[] a)
    {
        if (a[0] == "agrandir" || a[0] == "reduire")
        {
            // En place avec .bak (garde le .bak existant = le plus ancien original), ou vers un autre fichier.
            string inp = a[1], outp = a.Length > 2 ? a[2] : a[1];
            var log = new System.Text.StringBuilder();
            try
            {
                byte[] r = SavLimits.Convert(File.ReadAllBytes(inp), a[0] == "agrandir", log);
                Console.Write(log.ToString());
                if (r == null) { Console.WriteLine("Rien a faire : deja au bon format."); return 0; }
                if (outp == inp && !File.Exists(inp + ".bak")) File.Copy(inp, inp + ".bak");
                File.WriteAllBytes(outp, r);
                Console.WriteLine("Ecrit : " + outp);
                return 0;
            }
            catch (Exception ex) { Console.Write(log.ToString()); Console.WriteLine("ERREUR : " + ex.Message); return 1; }
        }
        if (a[0] == "test") return Test(a);
        if (a[0] == "diff")
        {
            SavPart x = SavPart.Parse(File.ReadAllBytes(a[1]), 0), y = SavPart.Parse(File.ReadAllBytes(a[2]), 0);
            for (int i = 0; i < SavPart.ManCount; i++)
                for (int k = 0; k < 5; k++)
                    if (x.Man(i, k) != y.Man(i, k))
                    {
                        Console.WriteLine("entree {0,3} : {1,10:X} {2,6} {3,7} | {4,10:X} {5,6} {6,7}", i,
                            x.Man(i, 1), x.Man(i, 2), x.Man(i, 3), y.Man(i, 1), y.Man(i, 2), y.Man(i, 3));
                        break;
                    }
            return 0;
        }
        if (a[0] == "cases")
        {
            foreach (SavPart p in SavLimitsFile.Load(File.ReadAllBytes(a[1])).Parts)
            {
                foreach (int e in Entries)
                {
                    Block b = p.Get(e); int half = b.Data.Length / 2, nz = 0, first = -1;
                    for (int i = half; i < b.Data.Length; i++) if (b.Data[i] != 0) { nz++; if (first < 0) first = i; }
                    Console.WriteLine("entree {0} : {1} octets, {2} non nuls dans la 2e moitie (1er a {3})", e, b.Data.Length, nz, first);
                }
                Block w = p.Get(28), r = p.Get(29), bt = p.Get(35); int nw = w.Data.Length / 388;
                int vivants = 0, vivantsHaut = 0, cheminMax = 0, ownersHaut = 0, batHaut = 0, ownerMax = 0;
                for (int i = 0; i < nw; i++)
                {
                    if (w.Data[i * 388 + 14] == 0) continue;
                    vivants++; if (i >= nw / 2) vivantsHaut++;
                    cheminMax = Math.Max(cheminMax, BitConverter.ToInt16(w.Data, i * 388 + 58));
                }
                for (int i = 0; i < r.Data.Length / 2; i++) { int o = BitConverter.ToInt16(r.Data, i * 2); if (o != 0) { ownerMax = Math.Max(ownerMax, o); if (i >= r.Data.Length / 4) ownersHaut++; } }
                for (int i = bt.Data.Length / 280 / 2; i < bt.Data.Length / 280; i++) if (bt.Data[i * 280] != 0) batHaut++;
                Console.WriteLine("walkers vivants {0} (haut {1}), chemin max {2}, proprietaires haut {3} (n° max {4}), batiments haut {5}",
                    vivants, vivantsHaut, cheminMax, ownersHaut, ownerMax, batHaut);
            }
            return 0;
        }
        for (int n = 1; n < a.Length; n++)
            foreach (string path in Files(a[n]))
            {
                try
                {
                    byte[] f = File.ReadAllBytes(path);
                    SavLimitsFile s = SavLimitsFile.Load(f);
                    byte[] back = s.Build();
                    bool same = back.Length == f.Length;
                    for (int i = 0; same && i < f.Length; i++) same = back[i] == f[i];
                    var sb = new System.Text.StringBuilder();
                    foreach (SavPart p in s.Parts)
                    {
                        sb.AppendFormat(" [{0}-{1} b{2}", p.Base, p.End, p.Blocks.Count);
                        foreach (int e in Entries) sb.AppendFormat(" {0}:{1:X}/{2}x{3}", e, p.Man(e, 1), p.Man(e, 2), p.Man(e, 3));
                        sb.Append("]");
                    }
                    Console.WriteLine("{0} len={1} parties={2} relecture={3}{4}", path, f.Length, s.Parts.Count, same ? "OK" : "DIFF", sb);
                }
                catch (Exception ex) { Console.WriteLine("{0} : ERREUR {1}", path, ex.Message); }
            }
        return 0;
    }

    static readonly int[] Limited = { 28, 29, 30, 35, 73 };

    // Meme contenu : memes parties, memes manifestes, memes donnees ; blocs hors limites identiques a l'octet.
    static string Compare(byte[] fa, byte[] fb)
    {
        SavLimitsFile x = SavLimitsFile.Load(fa), y = SavLimitsFile.Load(fb);
        if (x.Parts.Count != y.Parts.Count) return "nombre de parties";
        for (int pi = 0; pi < x.Parts.Count; pi++)
        {
            SavPart p = x.Parts[pi], q = y.Parts[pi];
            for (int i = 0; i < p.Header.Length; i++) if (p.Header[i] != q.Header[i]) return "manifeste, partie " + pi + " octet " + i;
            if (p.Blocks.Count != q.Blocks.Count) return "nombre de blocs";
            for (int k = 0; k < p.Blocks.Count; k++)
            {
                Block b = p.Blocks[k], c = q.Blocks[k];
                int len = b.ElemSize * b.Count;
                if (Array.IndexOf(Limited, b.Index) < 0)
                {
                    if (b.Raw.Length != c.Raw.Length) return "bloc " + b.Index + " brut";
                    for (int i = 0; i < b.Raw.Length; i++) if (b.Raw[i] != c.Raw[i]) return "bloc " + b.Index + " brut";
                }
                for (int i = 0; i < len; i++) if (b.Data[i] != c.Data[i]) return "bloc " + b.Index + " donnees octet " + i;
            }
        }
        return null;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // test <dossier|fichier>... : sur chaque .sav (en memoire, rien n'est ecrit)
    //   normale : agrandir -> etat Agrandi, donnees d'origine + zeros ; reduire -> meme contenu que l'original ;
    //             re-agrandir -> meme fichier qu'au 1er agrandissement ; agrandir 2 fois -> rien a faire.
    //   agrandie : reduire (accepte ou refuse avec raison) ; si accepte : re-agrandir -> meme contenu, zeros mis a part.
    static int Test(string[] a)
    {
        int ok = 0, bad = 0, refused = 0, unreadable = 0;
        for (int n = 1; n < a.Length; n++)
            foreach (string path in Files(a[n]))
            {
                string err = null;
                byte[] f = File.ReadAllBytes(path);
                SavLimitsFile s;
                try { s = SavLimitsFile.Load(f); SavLimits.GetState(s); }
                catch (Exception ex) { Console.WriteLine("ILLISIBLE {0} : {1}", path, ex.Message); unreadable++; continue; }
                var log = new System.Text.StringBuilder();
                try
                {
                    if (SavLimits.GetState(s) != SavLimits.State.Agrandi)
                    {
                        byte[] big = SavLimits.Convert(f, true, log);
                        SavLimitsFile sb = SavLimitsFile.Load(big);
                        if (SavLimits.GetState(sb) != SavLimits.State.Agrandi) err = "agrandi : etat";
                        else if (SavLimits.Convert(big, true, log) != null) err = "double agrandissement non refuse";
                        else if (SavLimits.GetState(s) == SavLimits.State.Normal)
                        {
                            for (int pi = 0; err == null && pi < s.Parts.Count; pi++)
                                foreach (int e in Limited)
                                {
                                    Block o = s.Parts[pi].Get(e), g = sb.Parts[pi].Get(e);
                                    int lo = o.ElemSize * o.Count, hi = g.ElemSize * g.Count;
                                    for (int i = 0; i < hi && err == null; i++) if (g.Data[i] != (i < lo ? o.Data[i] : 0)) err = "agrandi : bloc " + e + " octet " + i;
                                }
                            if (err == null)
                            {
                                byte[] small = SavLimits.Convert(big, false, log);
                                err = Compare(f, small);
                                if (err == null && !Same(SavLimits.Convert(small, true, log), big)) err = "re-agrandi different";
                            }
                        }
                    }
                    else
                    {
                        byte[] small = null;
                        try { small = SavLimits.Convert(f, false, log); }
                        catch (InvalidDataException ex) { Console.WriteLine("REFUSE   {0} : {1}", Path.GetFileName(path), ex.Message); refused++; continue; }
                        byte[] big = SavLimits.Convert(small, true, log);
                        // les cases hautes non nulles mais vides (walkers morts) sont perdues : comparer sur la partie basse
                        SavLimitsFile x = SavLimitsFile.Load(big);
                        for (int pi = 0; err == null && pi < s.Parts.Count; pi++)
                            foreach (int e in Limited)
                            {
                                Block o = s.Parts[pi].Get(e), g = x.Parts[pi].Get(e);
                                int lo = o.ElemSize * o.Count / 2;
                                for (int i = 0; i < lo && err == null; i++) if (g.Data[i] != o.Data[i]) err = "bloc " + e + " octet " + i;
                            }
                    }
                }
                catch (Exception ex) { err = "exception : " + ex.Message; }
                if (err == null) ok++; else { bad++; Console.WriteLine("ECHEC    {0} : {1}", path, err); }
            }
        Console.WriteLine("OK {0}  echecs {1}  reductions refusees {2}  illisibles {3}", ok, bad, refused, unreadable);
        return bad == 0 ? 0 : 1;
    }
}
