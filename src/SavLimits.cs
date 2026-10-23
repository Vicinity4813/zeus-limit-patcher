// Conversion des sauvegardes .sav entre l'exe normal (walkers 2000, chemins 1000, batiments 4000) et l'exe
// patche par Limits.cs (4000 / 2000 / 8000).
// Une sauvegarde = une ou plusieurs parties a la suite (aventure : la partie suivante sert a l'episode suivant).
// Partie = 8 octets de versions + manifeste de 300 entrees de 20 octets {compresse, adresse, taille, nombre, ptr},
// puis un bloc par entree 3.. (jusqu'a la premiere entree de taille 0). Bloc compresse : prefixe i32 = taille
// compressee (PKWare DCL), ou negatif = donnees brutes.
// Le manifeste reflete la table de sauvegarde de l'exe : l'adresse du tableau y est ecrite telle quelle.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZeusPak;

public class SavPart
{
    public int Base, End;                  // position dans le fichier
    public byte[] Header;                  // versions + manifeste (8 + 6000 octets)
    public List<Block> Blocks = new List<Block>();

    public const int ManCount = 300, HeadLen = 8 + ManCount * 20;

    public static SavPart Parse(byte[] f, int start)
    {
        if (start + HeadLen > f.Length) throw new InvalidDataException("manifeste tronque");
        SavPart s = new SavPart();
        s.Base = start;
        s.Header = MapFile.Sub(f, start, HeadLen);
        int man = start + 8, p = start + HeadLen;
        for (int i = 3; i < ManCount; i++)
        {
            int e = man + i * 20;
            uint comp = BitConverter.ToUInt32(f, e), size = BitConverter.ToUInt32(f, e + 8), count = BitConverter.ToUInt32(f, e + 12);
            if (size == 0) break;
            Block b = new Block();
            b.Index = i; b.Start = p; b.ElemSize = (int)size; b.Count = (int)count; b.Compressed = comp != 0;
            long rl = (long)size * count;
            if (rl > f.Length * 64L + (1 << 24)) throw new InvalidDataException("bloc " + i + " : taille incoherente");
            int rawLen = (int)rl;
            if (b.Compressed)
            {
                if (p + 4 > f.Length) throw new InvalidDataException("bloc " + i + " tronque");
                int cs = BitConverter.ToInt32(f, p); p += 4;
                if (cs < 0) { if (rawLen > f.Length - p) throw new InvalidDataException("bloc " + i + " tronque"); b.Data = MapFile.Sub(f, p, rawLen); p += rawLen; }
                else
                {
                    if (cs > f.Length - p) throw new InvalidDataException("bloc " + i + " : taille compressee incoherente");
                    try { b.Data = Pk.Explode(MapFile.Sub(f, p, cs)); }
                    catch (Exception ex) { throw new InvalidDataException("bloc " + i + " illisible : " + ex.Message); }
                    p += cs;
                    if (b.Data.Length < rawLen) throw new InvalidDataException("bloc " + i + " trop court apres decompression");
                }
            }
            else { if (rawLen > f.Length - p) throw new InvalidDataException("bloc " + i + " tronque"); b.Data = MapFile.Sub(f, p, rawLen); p += rawLen; }
            b.End = p;
            b.Raw = MapFile.Sub(f, b.Start, b.End - b.Start);
            s.Blocks.Add(b);
        }
        s.End = p;
        return s;
    }

    public Block Get(int index) { foreach (Block b in Blocks) if (b.Index == index) return b; return null; }

    public uint Man(int i, int field) { return BitConverter.ToUInt32(Header, 8 + i * 20 + field * 4); }
    public void SetMan(int i, int field, uint v) { Array.Copy(BitConverter.GetBytes(v), 0, Header, 8 + i * 20 + field * 4, 4); }

    public void Write(MemoryStream ms)
    {
        ms.Write(Header, 0, Header.Length);
        foreach (Block b in Blocks)
        {
            if (!b.Modified) { ms.Write(b.Raw, 0, b.Raw.Length); continue; }
            if (!b.Compressed) { ms.Write(b.Data, 0, b.Data.Length); continue; }
            byte[] c = Pk.Implode(b.Data);
            byte[] back = Pk.Explode(c);
            if (back.Length < b.Data.Length) throw new Exception("implode : relecture de taille differente");
            for (int i = 0; i < b.Data.Length; i++) if (back[i] != b.Data[i]) throw new Exception("implode : relecture differente");
            if (c.Length < b.Data.Length) { ms.Write(BitConverter.GetBytes(c.Length), 0, 4); ms.Write(c, 0, c.Length); }
            else { ms.Write(BitConverter.GetBytes(int.MinValue), 0, 4); ms.Write(b.Data, 0, b.Data.Length); }
        }
    }
}

public class SavLimitsFile
{
    public byte[] Bytes;
    public List<SavPart> Parts = new List<SavPart>();

    // Parties : la premiere a 0 ; les suivantes = memes 8 octets de versions plus loin, manifeste lisible.
    public static SavLimitsFile Load(byte[] f)
    {
        SavLimitsFile s = new SavLimitsFile();
        s.Bytes = f;
        SavPart p0 = SavPart.Parse(f, 0);
        s.Parts.Add(p0);
        int from = p0.End;
        for (int i = from; i + SavPart.HeadLen <= f.Length; i++)
        {
            if (f[i] != f[0] || !Same(f, 0, i, 8)) continue;
            SavPart x;
            try { x = SavPart.Parse(f, i); } catch (Exception) { continue; }
            if (x.Blocks.Count != p0.Blocks.Count) continue;
            s.Parts.Add(x);
            i = x.End - 1;
        }
        return s;
    }

    static bool Same(byte[] f, int a, int b, int n) { for (int k = 0; k < n; k++) if (f[a + k] != f[b + k]) return false; return true; }

    public byte[] Build()
    {
        MemoryStream ms = new MemoryStream();
        int pos = 0;
        foreach (SavPart p in Parts)
        {
            ms.Write(Bytes, pos, p.Base - pos);   // ce qui precede / separe les parties, tel quel
            p.Write(ms);
            pos = p.End;
        }
        ms.Write(Bytes, pos, Bytes.Length - pos);
        return ms.ToArray();
    }
}

// Agrandir (exe normal -> exe patche) / reduire (exe patche -> exe normal), entree par entree.
public static class SavLimits
{
    // Entree du manifeste, taille d'element, nombre normal ; adresses des tableaux dans l'exe anglais 2.1.4.0
    // d'origine et dans l'exe patche (relevees dans une sauvegarde de l'exe patche). Une autre adresse (exe
    // francais...) est laissee telle quelle : au chargement, le jeu ne compare que la taille et le nombre.
    // Optional : entree doublee seulement depuis la version 1.1 (compagnies) ; une .sav de l'exe 1.0 la garde a
    // son nombre normal et reste « agrandie ». Agrandir / reduire la traite comme les autres.
    struct Ent { public int Index, Size, Count; public uint OldAddr, NewAddr; public string Name; public bool Optional; }
    static readonly Ent[] Ents = {
        new Ent { Index = 28, Size = 388, Count = 2000,   OldAddr = 0xDBD788, NewAddr = 0x1615E8C, Name = "walkers" },
        new Ent { Index = 29, Size = 2,   Count = 1000,   OldAddr = 0xEF4FE8, NewAddr = 0x1884F4C, Name = "proprietaires des chemins" },
        new Ent { Index = 30, Size = 1,   Count = 500000, OldAddr = 0xE7AEC8, NewAddr = 0x1790D0C, Name = "chemins" },
        new Ent { Index = 35, Size = 280, Count = 4000,   OldAddr = 0x92E658, NewAddr = 0x1886010, Name = "batiments" },
        new Ent { Index = 73, Size = 2,   Count = 4000,   OldAddr = 0x91F5B8, NewAddr = 0x1C67010, Name = "liste des batiments" },
        new Ent { Index = 31, Size = 156, Count = 100,    OldAddr = 0xDB7B40, NewAddr = 0x1C6B010, Name = "compagnies", Optional = true },
    };
    const int WalkerAlive = 14, WalkerPath = 58, BuildingAlive = 0;

    // Compagnie occupee : un octet non nul dans ses 156 octets (les cases libres sont entierement a zero).
    public static bool CompanyUsed(Block c, int i)
    {
        for (int k = 0; k < 156; k++) if (c.Data[i * 156 + k] != 0) return true;
        return false;
    }
    const uint PrevShift = 0x1000;   // patch du 2026-10-19 (sans page de code en tete de .zlim) : adresses 4 Ko plus bas

    public enum State { Normal, Agrandi, Mixte }

    public static State GetState(SavLimitsFile s)
    {
        int old = 0, big = 0;
        foreach (SavPart p in s.Parts)
            foreach (Ent e in Ents)
            {
                Block b = p.Get(e.Index);
                if (b == null || b.ElemSize != e.Size) throw new InvalidDataException("entree " + e.Index + " inattendue (pas une sauvegarde de Zeus 2.1.4.0 ?)");
                if (b.Count != e.Count && b.Count != e.Count * 2) throw new InvalidDataException("entree " + e.Index + " : nombre " + b.Count + " inconnu");
                if (e.Optional) continue;
                if (b.Count == e.Count) old++;
                else if (b.Count == e.Count * 2) big++;
                else throw new InvalidDataException("entree " + e.Index + " : nombre " + b.Count + " inconnu");
            }
        return big == 0 ? State.Normal : old == 0 ? State.Agrandi : State.Mixte;
    }

    // Renvoie le fichier converti, ou null si rien a faire. Exception (message en clair) si la reduction est impossible.
    public static byte[] Convert(byte[] f, bool enlarge, StringBuilder log)
    {
        SavLimitsFile s = SavLimitsFile.Load(f);
        GetState(s);
        bool changed = false;
        for (int pi = 0; pi < s.Parts.Count; pi++)
        {
            SavPart p = s.Parts[pi];
            if (!enlarge) CheckReducible(p, pi, s.Parts.Count);
            foreach (Ent e in Ents)
            {
                Block b = p.Get(e.Index);
                int target = enlarge ? e.Count * 2 : e.Count;
                if (b.Count == target) continue;
                byte[] d = new byte[e.Size * target];
                Array.Copy(b.Data, d, Math.Min(d.Length, e.Size * b.Count));   // agrandir : complete par des zeros
                b.Data = d; b.Count = target; b.Modified = true;
                p.SetMan(e.Index, 3, (uint)target);
                uint addr = p.Man(e.Index, 1);
                if (enlarge && addr == e.OldAddr) p.SetMan(e.Index, 1, e.NewAddr);
                if (!enlarge && (addr == e.NewAddr || addr == e.NewAddr - PrevShift)) p.SetMan(e.Index, 1, e.OldAddr);
                changed = true;
            }
            log.AppendLine(Describe(p, pi, s.Parts.Count));
        }
        return changed ? s.Build() : null;
    }

    static string PartName(int pi, int n) { return n == 1 ? "partie" : "partie " + (pi + 1) + "/" + n; }

    // Ce qui empeche de revenir aux limites normales : walkers vivants n° >= 2000, chemins utilises n° >= 1000,
    // batiments n° >= 4000, cases occupees de la liste e73 au-dela de 4000 (tout a zero = reduction possible).
    public static int[] Blockers(SavPart p)
    {
        Block w = p.Get(28), owners = p.Get(29), bat = p.Get(35), list = p.Get(73), comp = p.Get(31);
        int hiW = 0, hiPath = 0, hiOwner = 0, hiB = 0, hiList = 0, hiC = 0;
        for (int i = 0; i < w.Count; i++)
        {
            if (w.Data[i * 388 + WalkerAlive] == 0) continue;
            if (i >= 2000) hiW++;
            else if (BitConverter.ToInt16(w.Data, i * 388 + WalkerPath) >= 1000) hiPath++;
        }
        for (int i = 1000; i < owners.Count; i++) if (BitConverter.ToInt16(owners.Data, i * 2) != 0) hiOwner++;
        for (int i = 4000; i < bat.Count; i++) if (bat.Data[i * 280 + BuildingAlive] != 0) hiB++;
        for (int i = 4000; i < list.Count; i++) if (BitConverter.ToInt16(list.Data, i * 2) != 0) hiList++;
        for (int i = 100; i < comp.Count; i++) if (CompanyUsed(comp, i)) hiC++;
        return new[] { hiW, hiPath + hiOwner, hiB, hiList, hiC };
    }

    static void CheckReducible(SavPart p, int pi, int n)
    {
        int[] k = Blockers(p);
        var why = new List<string>();
        if (k[0] > 0) why.Add(k[0] + " walkers vivants au-dela du n° 1999");
        if (k[1] > 0) why.Add(k[1] + " chemins utilises au-dela du n° 999");
        if (k[2] > 0) why.Add(k[2] + " batiments au-dela du n° 3999");
        if (k[3] > 0) why.Add(k[3] + " cases occupees dans la liste des batiments au-dela de 4000");
        if (k[4] > 0) why.Add(k[4] + " compagnies au-dela du n° 99");
        if (why.Count > 0) throw new InvalidDataException(PartName(pi, n) + " : impossible de revenir aux limites normales sans perte (" + string.Join(", ", why) + ")");
    }

    // Walkers vivants, batiments existants
    public static int[] Alive(SavPart p)
    {
        Block w = p.Get(28), bat = p.Get(35);
        int nw = 0, nb = 0;
        for (int i = 0; i < w.Count; i++) if (w.Data[i * 388 + WalkerAlive] != 0) nw++;
        for (int i = 0; i < bat.Count; i++) if (bat.Data[i * 280 + BuildingAlive] != 0) nb++;
        return new[] { nw, nb };
    }

    public static string Describe(SavPart p, int pi, int n)
    {
        int[] a = Alive(p);
        Block c = p.Get(31); int nc = 0;
        for (int i = 0; i < c.Count; i++) if (CompanyUsed(c, i)) nc++;
        return string.Format("{0} : {1} walkers / {2}, {3} batiments / {4}, chemins / {5}, {6} compagnies / {7}", PartName(pi, n), a[0], p.Get(28).Count, a[1], p.Get(35).Count, p.Get(29).Count, nc, c.Count);
    }
}
