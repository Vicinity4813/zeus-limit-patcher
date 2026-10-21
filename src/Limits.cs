// Patch des limites de donnees de Zeus.exe (anglais 2.1.4.0) : les tableaux sont deplaces dans une
// nouvelle section non initialisee ".zlim", plus grande, et chaque reference du code est corrigee.
// Le patch est reversible : l'annulation recalcule les valeurs d'origine (section retiree, references
// et constantes remises), ce qui redonne l'exe d'origine octet pour octet.
using System;
using System.Collections.Generic;
using System.Text;

// Un tableau deplace. Rows > 1 : matrice de Rows lignes de Old (puis New) elements ; Old == New : bloc fixe
// deplace seulement pour garder la contiguite. OneBased : le code y accede aussi par "debut - element"
// (indice a partir de 1), y compris pour chaque ligne d'une matrice.
public class Moved
{
    public string Name; public uint Base; public int Size, Old, New, Rows = 1, Group; public bool OneBased; public uint NewBase;
    public int RowOld { get { return Size * Old; } }
    public int RowNew { get { return Size * New; } }
    public uint End { get { return Base + (uint)(Rows * RowOld); } }
    public uint NewEnd { get { return NewBase + (uint)(Rows * RowNew); } }
    public bool Fixed { get { return Old == New; } }

    // adresse d'origine (dans [Base, End)) -> nouvelle adresse
    public uint Map(uint v) { return Conv(v - Base, RowOld, RowNew) + NewBase; }
    public uint Unmap(uint v) { return Conv(v - NewBase, RowNew, RowOld) + Base; }
    uint Conv(uint off, int from, int to)
    {
        // dernier element (depart d'une boucle a rebours, ex. 0x4D9F66 walkers[1999]+10) -> dernier element du nouveau tableau
        if (Rows == 1 && !Fixed && off >= (uint)(from - Size)) return off - (uint)from + (uint)to;
        if (Rows == 1) return off;
        if (OneBased && (off + (uint)Size) % (uint)from == 0) return (off + (uint)Size) / (uint)from * (uint)to - (uint)Size;
        return off / (uint)from * (uint)to + off % (uint)from;
    }
    // pas de boucle compatible avec ce tableau (0 = inconnu)
    public bool StrideOk(int s) { s = Math.Abs(s); return s == 0 || Fixed || s == Size || (Rows > 1 && s == RowOld); }
}

public struct Konst { public uint Va; public uint Old, New; public Konst(uint va, uint o, uint n) { Va = va; Old = o; New = n; } }

public static class Limits
{
    public const string SectionName = ".zlim";
    // Exe zeus-qol : sa section .wpat (la derniere) prend la seule place d'en-tete libre. Elle est alors agrandie
    // d'autant : memes adresses que .zlim (juste apres elle), page de code ecrite en fin de fichier apres des zeros
    // (la partie non initialisee de .wpat devient des zeros dans le fichier, meme contenu au demarrage).
    // La page se termine par WpatMagic et les valeurs d'origine de .wpat (taille virtuelle, brute, attributs).
    public const string WpatName = ".wpat";
    const string WpatMagic = "ZeusLimits.wpat";
    public static bool NoStack;   // essais seulement : ne pas agrandir les tableaux sur la pile

    // ---- description du patch (exe anglais 2.1.4.0)
    // Les tableaux sont listes dans l'ordre de la nouvelle section ; deux tableaux contigus a l'origine le
    // restent (la file des walkers libres finit au debut des walkers).
    static List<Moved> Arrays(bool walkers, bool buildings)
    {
        var l = new List<Moved>();
        if (walkers)
        {
            l.Add(new Moved { Group = 1, Name = "file des walkers libres", Base = 0xDBB84C, Size = 4, Old = 1999, New = 3999 });
            l.Add(new Moved { Group = 1, Name = "walkers", Base = 0xDBD788, Size = 388, Old = 2000, New = 4000 });
            // reservoir des chemins (bloc 30 : 500 o par chemin, champ +58 du walker) et proprietaire de chaque chemin (bloc 29)
            l.Add(new Moved { Group = 1, Name = "chemins", Base = 0xE7AEC8, Size = 500, Old = 1000, New = 2000 });
            l.Add(new Moved { Group = 1, Name = "proprietaires des chemins", Base = 0xEF4FE8, Size = 2, Old = 1000, New = 2000 });
        }
        if (buildings)
        {
            // bloc contigu : batiments puis tableaux par batiment qui les suivent en memoire
            l.Add(new Moved { Group = 2, Name = "batiments", Base = 0x92E658, Size = 280, Old = 4000, New = 8000 });
            l.Add(new Moved { Group = 2, Name = "K1 (27 dwords)", Base = 0xA3FD58, Size = 1, Old = 108, New = 108 });
            l.Add(new Moved { Group = 2, Name = "matrice 1", Base = 0xA3FDC4, Size = 4, Old = 4000, New = 8000, Rows = 27, OneBased = true });
            l.Add(new Moved { Group = 2, Name = "matrice 2", Base = 0xAA9544, Size = 4, Old = 4000, New = 8000, Rows = 27, OneBased = true });
            l.Add(new Moved { Group = 2, Name = "K2 (28 dwords)", Base = 0xB12CC4, Size = 1, Old = 112, New = 112 });
            l.Add(new Moved { Group = 2, Name = "A1", Base = 0xB12D34, Size = 4, Old = 4000, New = 8000, OneBased = true });
            l.Add(new Moved { Group = 2, Name = "A2", Base = 0xB16BB4, Size = 4, Old = 4000, New = 8000, OneBased = true });
            l.Add(new Moved { Group = 2, Name = "A3", Base = 0xB1AA34, Size = 4, Old = 4000, New = 8000, OneBased = true });
            l.Add(new Moved { Group = 3, Name = "liste de batiments (e73)", Base = 0x91F5B8, Size = 2, Old = 4000, New = 8000 });
        }
        return l;
    }

    // Indices "ligne x 4000" des matrices calcules sans constante : lea x5, lea x5, lea x5 puis shl 5 (= x4000).
    // Les lignes passant a 8000 elements, le shl 5 devient shl 6.
    static uint[] ShlSites(bool walkers, bool buildings)
    {
        return buildings ? new uint[] { 0x40E52E, 0x40E718, 0x40E837, 0x40F0AF } : new uint[0];
    }

    static void PatchShl(byte[] f, Code c, uint va, byte from, byte to)
    {
        Insn n;
        if (!c.Insns.TryGetValue(va, out n) || n.Two || n.Op != 0xC1 || n.Mod != 3 || n.Reg != 4)
            throw new Exception(string.Format("shl attendu en 0x{0:X}", va));
        int o = c.ToOff(va) + n.Len - 1;
        if (f[o] != from) throw new Exception(string.Format("shl {0} attendu en 0x{1:X} (trouve {2})", from, va, f[o]));
        f[o] = to;
    }

    static List<Konst> Constants(bool walkers, bool buildings)
    {
        var l = new List<Konst>();
        if (walkers)
        {
            foreach (uint va in new uint[] { 0x459DE5, 0x459EF4, 0x45A5C9, 0x4AC278, 0x4CC3B1, 0x4CC633, 0x4CC6BE, 0x4CCA12, 0x4D06EF, 0x4D8A12,
                0x4D9F2B, 0x4DED01, 0x4DF31E, 0x4EFE2A, 0x4F7D45, 0x4F838A, 0x4F8392, 0x52F26E, 0x533A10, 0x56B58A, 0x56B60A })
                l.Add(new Konst(va, 2000, 4000));
            foreach (uint va in new uint[] { 0x4CC618, 0x4D9F10, 0x4D9F61, 0x4D9FA3, 0x4D9FEE, 0x4DA9CA })
                l.Add(new Konst(va, 1999, 3999));
            l.Add(new Konst(0x4D9ED1, 194000, 388000));     // rep stosd : remise a zero des walkers (en dwords)
            l.Add(new Konst(0x4CC6E7, 1000, 2000));         // recherche d'un chemin libre
            l.Add(new Konst(0x4D89E1, 125000, 250000));     // rep stosd : remise a zero des chemins
            l.Add(new Konst(0x4D89EF, 500, 1000));          // rep stosd : remise a zero des proprietaires
        }
        if (walkers || buildings)
        {
            // tampon de (de)compression des sauvegardes [0xF9C104] : taille minimale et taille maximale d'un bloc
            // (1 200 000 octets ; walkers agrandis = 1 552 000, batiments = 2 240 000). Sinon le bloc n'est pas ecrit.
            foreach (uint va in new uint[] { 0x5C3F51, 0x5C3F59, 0x5C4924, 0x5C492B, 0x5C5F8F, 0x5C6031, 0x5C613B })
                l.Add(new Konst(va, 1200000, 2400000));
        }
        if (buildings)
        {
            foreach (uint va in new uint[] { 0x40E91D, 0x40F451, 0x486F1A, 0x486FFB, 0x487043, 0x489C15, 0x489C8F, 0x48B7D1, 0x48B816,
                0x4CDC83, 0x4CDD98, 0x4FE32B, 0x51D76E, 0x5468C2, 0x53EC83, 0x5422C4,
                0x48A1D5, 0x48A1E3 })                                                  // (rep stosd des 2 tableaux sur la pile de 0x48A140)
                l.Add(new Konst(va, 4000, 8000));
            foreach (uint va in new uint[] { 0x4A7D3E, 0x4A8132, 0x4A817E, 0x4A89FE, 0x5423A5, 0x542476 })
                l.Add(new Konst(va, 3999, 7999));
            foreach (uint va in new uint[] { 0x40E917, 0x40F44C, 0x4870F1 })   // pas d'une ligne de matrice ; budget d'iterations
                l.Add(new Konst(va, 16000, 32000));
            foreach (uint va in new uint[] { 0x40E2D8, 0x40E2E4, 0x40EB09, 0x40EB15 })   // rep stosd : remise a zero des matrices
                l.Add(new Konst(va, 108000, 216000));
            l.Add(new Konst(0x493E51, 280000, 560000));     // rep stosd : remise a zero des batiments
        }
        return l;
    }

    // Tableaux locaux sur la pile, par fonction : (rel du debut, taille d'origine en octets, debut - 4 utilise)
    public class StackArr { public int Rel, Old, New; public bool OneBased; }
    static Dictionary<uint, StackArr[]> StackArrays(bool walkers, bool buildings)
    {
        var d = new Dictionary<uint, StackArr[]>();
        if (buildings)
        {
            d[0x48A140] = new[] { new StackArr { Rel = -32000, Old = 16000, New = 32000 }, new StackArr { Rel = -16000, Old = 16000, New = 32000, OneBased = true } };
            d[0x53E740] = new[] { new StackArr { Rel = -32000, Old = 32000, New = 64000 } };     // 4000 x 8 o (tri par distance)
            d[0x542900] = new[] { new StackArr { Rel = -32000, Old = 32000, New = 64000 } };     // idem
            d[0x4C59A0] = new[] { new StackArr { Rel = -16000, Old = 16000, New = 32000 } };     // liste de batiments
            d[0x58AAD0] = new[] { new StackArr { Rel = -64000, Old = 64000, New = 128000 } };    // 4000 x 16 o (destinations des walkers)
        }
        return d;
    }

    // Decalage "s" d'un acces : croissance des tableaux situes au-dessus de l'objet (le sien compris s'il est dedans).
    // rel est exprime dans la disposition "start" (ancienne si patch, nouvelle si annulation).
    static int StackShift(StackArr[] arrs, int rel, bool indexed, bool patched)
    {
        int s = 0;
        for (int pass = 0; pass < 2; pass++)       // d'abord "debut - 4" (indice a partir de 1), puis les plages
        foreach (StackArr a in arrs)
        {
            int start = patched ? NewStart(arrs, a) : a.Rel, size = patched ? a.New : a.Old;
            bool inside = pass == 0 ? a.OneBased && indexed && rel == start - 4 : rel >= start && rel < start + size;
            if (inside) { s = 0; foreach (StackArr b in arrs) if (b.Rel >= a.Rel) s += b.New - b.Old; return s; }
        }
        foreach (StackArr a in arrs) if ((patched ? NewStart(arrs, a) : a.Rel) > rel) s += a.New - a.Old;
        return s;
    }
    static int NewStart(StackArr[] arrs, StackArr a) { int s = 0; foreach (StackArr b in arrs) if (b.Rel >= a.Rel) s += b.New - b.Old; return a.Rel - s; }

    // Agrandit (ou remet) les tableaux sur la pile d'une fonction. undo : f est l'exe patche.
    static void PatchStack(byte[] f, Code c, uint fn, StackArr[] arrs, bool undo, StringBuilder log)
    {
        StackFrame sf = StackFrame.Analyze(c, fn);
        if (sf.Problems.Count > 0) throw new Exception(string.Format("pile de 0x{0:X} non suivie : {1}", fn, sf.Problems[0]));
        int G = 0; foreach (StackArr a in arrs) G += a.New - a.Old;
        int sign = undo ? -1 : 1, changed = 0;
        foreach (StackRef r in sf.Refs)
        {
            int s = StackShift(arrs, r.Rel, r.Indexed, undo);
            int delta = sign * (G - s);
            if (delta == 0) continue;
            if (r.DispSize != 4) throw new Exception(string.Format("0x{0:X} : deplacement 8 bits a decaler ({1})", r.Va, c.Text_(r.Va)));
            Put(f, c.ToOff(r.Va) + r.DispOff, (uint)(r.Disp + delta)); changed++;
        }
        int frameOld = sf.Frame, ops = 0;
        foreach (uint va in sf.FrameOps)
        {
            Insn n = c.Insns[va];
            if (!n.Two && n.Op == 0xE8)                      // __chkstk : mov eax, taille juste avant
            {
                int o = c.ToOff(va - 5) + 1; Put(f, o, (uint)(sf.Frame + sign * G)); ops++;
            }
            else
            {
                int o = c.ToOff(va) + n.Len - 4;
                if (n.Op != 0x81) throw new Exception(string.Format("0x{0:X} : operation de cadre inattendue", va));
                uint imm = BitConverter.ToUInt32(f, o);
                if (imm != (uint)sf.Frame) throw new Exception(string.Format("0x{0:X} : add/sub esp {1} au lieu de {2}", va, imm, sf.Frame));
                Put(f, o, (uint)(sf.Frame + sign * G)); ops++;
            }
        }
        if (ops < 2) throw new Exception(string.Format("0x{0:X} : allocation ou liberation du cadre introuvable", fn));
        log.AppendFormat("pile 0x{0:X} : cadre {1} -> {2}, {3} acces decales\n", fn, frameOld, frameOld + sign * G, changed);
    }

    // Table de sauvegarde (.data 0x63C2D0, 139 entrees de 20 octets) : entree -> tableau deplace
    static readonly uint SaveTable = 0x63C2D0;
    static Dictionary<int, string> SaveEntries(bool walkers, bool buildings)
    {
        var d = new Dictionary<int, string>();
        if (walkers) { d[28] = "walkers"; d[29] = "proprietaires des chemins"; d[30] = "chemins"; }
        if (buildings) { d[35] = "batiments"; d[73] = "liste de batiments (e73)"; }
        return d;
    }

    // ---- outils
    static bool IsCmpImm(Insn n, out int reg)
    {
        reg = -1;
        if (n.Two) return false;
        if (n.Op == 0x81 && n.Reg == 7 && n.Mod == 3) { reg = n.Rm; return true; }
        if (n.Op == 0x3D) { reg = 0; return true; }
        return false;
    }

    // Pas d'une boucle : add/sub du registre compare, dans les instructions qui precedent.
    static int Stride(Code c, List<uint> keys, int k, int reg)
    {
        // corps de la boucle : du saut arriere qui suit le cmp jusqu'au cmp ; sinon les 10 instructions precedentes
        int from = Math.Max(0, k - 10);
        for (int j = k + 1; j <= Math.Min(keys.Count - 1, k + 2); j++)
        {
            Insn jn = c.Insns[keys[j]];
            if (jn.Jump && jn.Cond && jn.Target < keys[k]) { int t = keys.BinarySearch(jn.Target); if (t >= 0 && k - t < 400) from = t; break; }
        }
        int s = StrideIn(c, keys, k - 1, from, reg);
        return s != 0 ? s : StrideIn(c, keys, Math.Min(keys.Count - 1, k + 60), k + 1, reg);   // boucle testee en tete
    }

    static int StrideIn(Code c, List<uint> keys, int hi, int lo, int reg)
    {
        for (int j = hi; j >= lo; j--)
        {
            Insn m = c.Insns[keys[j]];
            if (!m.Two && reg == 0 && (m.Op == 0x05 || m.Op == 0x2D)) return (int)m.Fields[0].Value;   // add/sub eax, imm32 (forme courte)
            if (m.Two || m.Mod != 3 || m.Rm != reg) continue;
            if ((m.Op == 0x81 || m.Op == 0x83) && (m.Reg == 0 || m.Reg == 5))
            {
                int o = c.ToOff(m.Va);
                return m.Op == 0x81 ? (int)BitConverter.ToUInt32(c.F, o + m.Len - 4) : (sbyte)c.F[o + m.Len - 1];
            }
        }
        return 0;
    }

    static uint Align(uint v, uint a) { return (v + a - 1) / a * a; }

    // ---- patch
    public static byte[] Patch(byte[] orig, bool walkers, bool buildings, StringBuilder log)
    {
        byte[] f = (byte[])orig.Clone();
        Code c = Code.Load(f);
        foreach (Sec s in c.Secs) if (s.Name == SectionName) throw new Exception("cet exe est deja patche (section " + SectionName + ")");
        if (PatchedWpat(c) != null) throw new Exception("cet exe est deja patche (section " + WpatName + " agrandie)");
        CheckSignature(c);

        var arrs = Arrays(walkers, buildings);
        // nouvelle section apres la derniere
        int pe = c.Pe; uint secAlign = BitConverter.ToUInt32(f, pe + 24 + 32), fileAlign = BitConverter.ToUInt32(f, pe + 24 + 36);
        Sec last = c.Secs[c.Secs.Count - 1];
        uint newVa = Align(last.Va + Math.Max(last.VSize, last.RSize), secAlign);
        if (BitConverter.ToUInt32(f, pe + 24 + 56) != newVa - c.ImageBase) throw new Exception("SizeOfImage inattendu : l'annulation ne serait pas exacte");
        // en-tete de section : place libre, sinon (exe zeus-qol) agrandissement de .wpat
        int hdr = pe + 24 + c.OptSize + c.NumSec * 40;
        uint sizeOfHeaders = BitConverter.ToUInt32(f, pe + 24 + 60);
        bool free = hdr + 40 <= sizeOfHeaders;
        for (int i = 0; free && i < 40; i++) if (f[hdr + i] != 0) free = false;
        bool wpat = !free && last.Name == WpatName;
        if (!free && !wpat)
            throw new Exception(hdr + 40 > sizeOfHeaders ? "pas de place pour un en-tete de section" : "la place de l'en-tete de section n'est pas libre");
        Layout(arrs, newVa, wpat || HasCode(arrs));   // 4 octets de marge : la file (1 dword par case) finit sur 16 octets
        uint vsize = SectionSize(arrs, newVa);
        // l'annulation reconnait les valeurs par leur place dans la section : aucune valeur d'origine ne doit y tomber
        foreach (Insn n in c.Insns.Values)
            foreach (Field fl in n.Fields)
                if (fl.Size == 4 && fl.Value >= newVa && fl.Value < newVa + vsize + 0x10000)
                    throw new Exception(string.Format("valeur 0x{0:X} deja dans la future section (0x{1:X}) : annulation non garantie", fl.Value, n.Va));

        uint gap = 0; uint[] wt = null;
        if (wpat)
        {
            // .wpat agrandie jusqu'a la fin de nos tableaux ; sa partie brute couvre maintenant notre page de code
            if (f.Length != last.Raw + last.RSize || last.RSize % fileAlign != 0 || (newVa - last.Va) % fileAlign != 0 || CodePage % fileAlign != 0)
                throw new Exception("section " + WpatName + " inattendue : l'annulation ne serait pas exacte");
            uint ch = BitConverter.ToUInt32(f, last.Header + 36);
            wt = new uint[] { last.VSize, last.RSize, ch };
            gap = newVa - last.Va - last.RSize;
            Put(f, last.Header + 8, newVa - last.Va + vsize); Put(f, last.Header + 16, newVa - last.Va + CodePage);
            Put(f, last.Header + 36, ch | 0xE0000060);   // code + donnees, execution, lecture/ecriture
        }
        else
        {
            byte[] name = Encoding.ASCII.GetBytes(SectionName);
            Array.Copy(name, 0, f, hdr, name.Length);
            Put(f, hdr + 8, vsize); Put(f, hdr + 12, newVa - c.ImageBase); Put(f, hdr + 16, 0); Put(f, hdr + 20, 0);
            Put(f, hdr + 36, 0xC0000080);              // donnees non initialisees, lecture/ecriture
            if (HasCode(arrs))
            {
                // premiere page initialisee : la routine d'attribution (ajoutee en fin de fichier)
                if (f.Length % fileAlign != 0 || CodePage % fileAlign != 0) throw new Exception("taille de fichier non alignee : l'annulation ne serait pas exacte");
                Put(f, hdr + 16, CodePage); Put(f, hdr + 20, (uint)f.Length);
                Put(f, hdr + 36, 0xE0000060);          // code + donnees, execution, lecture/ecriture
            }
            f[pe + 6] = (byte)(c.NumSec + 1); f[pe + 7] = (byte)((c.NumSec + 1) >> 8);
        }
        Put(f, pe + 24 + 56, Align(newVa + vsize - c.ImageBase, secAlign));   // SizeOfImage

        // references du code
        var keys = new List<uint>(c.Insns.Keys);
        int moved = 0, endLoops = 0, skipped = 0;
        var count = new Dictionary<string, int>();
        for (int k = 0; k < keys.Count; k++)
        {
            Insn n = c.Insns[keys[k]]; int o = c.ToOff(n.Va);
            foreach (Field fl in n.Fields)
            {
                if (fl.Size != 4) continue;
                uint v = fl.Value, nv = v; string why = null;
                int reg; bool cmp = IsCmpImm(n, out reg) && fl.Kind == 'i';
                int stride = cmp ? Stride(c, keys, k, reg) : 0;
                // 1) "debut - element" : indice a partir de 1
                foreach (Moved m in arrs)
                    if (m.OneBased && v == m.Base - (uint)m.Size) { nv = m.NewBase - (uint)m.Size; why = m.Name + " (debut - 1 element)"; break; }
                // 2) dans un tableau
                if (why == null)
                foreach (Moved m in arrs)
                {
                    if (v >= m.Base && v < m.End)
                    {
                        if (cmp && !m.StrideOk(stride))
                        {
                            // fin de boucle (pointeur de champ) d'un tableau deplace qui precede ?
                            Moved pm = arrs.Find(x => x.Group == m.Group && x.End <= v && v <= x.End + (uint)x.Size && x.StrideOk(stride) && !x.Fixed);
                            if (pm != null) { nv = v - pm.End + pm.NewEnd; why = pm.Name + " (fin de boucle)"; endLoops++; break; }
                            why = "fin de boucle d'un autre tableau (pas " + stride + ")"; break;
                        }
                        nv = m.Map(v); why = m.Name; break;
                    }
                }
                // 3) fin de boucle juste apres la fin d'un tableau (pointeur de champ)
                if (why == null && cmp)
                foreach (Moved m in arrs)
                {
                    if (arrs.Exists(x => x.Base == m.End)) continue;     // fin = debut du tableau suivant, deja traite
                    if (v >= m.End && v <= m.End + (uint)Math.Max(m.Size, 4))
                    {
                        if (stride != 0 && m.StrideOk(stride)) { nv = v - m.End + m.NewEnd; why = m.Name + " (fin de boucle)"; endLoops++; break; }
                        why = "AMBIGU fin+" + (v - m.End) + " de " + m.Name + " (pas " + stride + ")"; break;
                    }
                }
                if (why == null) continue;
                if (nv == v) { skipped++; log.AppendFormat("  laisse 0x{0:X} {1} : {2}{3}\n", n.Va, c.Text_(n.Va), why, c.Spec.Contains(n.Va) ? " [trou]" : ""); continue; }
                if (c.Spec.Contains(n.Va)) log.AppendFormat("  [trou] 0x{0:X} {1} -> {2}\n", n.Va, c.Text_(n.Va), why);
                Put(f, o + fl.Off, nv); moved++;
                count[why] = count.ContainsKey(why) ? count[why] + 1 : 1;
            }
        }
        log.AppendFormat("references deplacees : {0} (dont fins de boucle {1}), laissees : {2}\n", moved, endLoops, skipped);
        foreach (var kv in count) log.AppendFormat("  {0} : {1}\n", kv.Key, kv.Value);

        // constantes
        foreach (Konst kc in Constants(walkers, buildings))
        {
            Insn n; if (!c.Insns.TryGetValue(kc.Va, out n)) throw new Exception(string.Format("instruction attendue introuvable en 0x{0:X}", kc.Va));
            bool done = false;
            foreach (Field fl in n.Fields)
                if (fl.Kind == 'i' && fl.Value == kc.Old)
                {
                    int o = c.ToOff(n.Va) + fl.Off;
                    if (fl.Size == 4) Put(f, o, kc.New); else { f[o] = (byte)kc.New; f[o + 1] = (byte)(kc.New >> 8); }
                    done = true; break;
                }
            if (!done) throw new Exception(string.Format("constante {0} attendue en 0x{1:X} : {2}", kc.Old, kc.Va, c.Text_(kc.Va)));
        }

        foreach (uint va in ShlSites(walkers, buildings)) PatchShl(f, c, va, 5, 6);

        // tableaux sur la pile
        if (!NoStack) foreach (var kv in StackArrays(walkers, buildings)) PatchStack(f, c, kv.Key, kv.Value, false, log);

        // table de sauvegarde : adresse et nombre
        foreach (var kv in SaveEntries(walkers, buildings))
        {
            Moved m = arrs.Find(x => x.Name == kv.Value);
            int e = c.ToOff(SaveTable + (uint)kv.Key * 20);
            bool bytes = BitConverter.ToUInt32(f, e + 8) == 1 && m.Size > 1;   // bloc decrit en octets (ex. chemins : 1 x 500000)
            uint unit = bytes ? (uint)m.Size : 1;
            if (BitConverter.ToUInt32(f, e + 4) != m.Base || (!bytes && BitConverter.ToUInt32(f, e + 8) != (uint)m.Size) || BitConverter.ToUInt32(f, e + 12) != (uint)m.Old * unit)
                throw new Exception("entree " + kv.Key + " de la table de sauvegarde inattendue");
            Put(f, e + 4, m.NewBase); Put(f, e + 12, (uint)m.New * unit);
        }

        // pointeurs dans les donnees (hors table de sauvegarde) : signales seulement s'ils visent un debut de tableau
        foreach (Sec s in c.Secs)
        {
            if (s == c.Text) continue;
            for (uint q = 0; q + 4 <= Math.Min(s.RSize, s.VSize); q += 4)
            {
                uint v = BitConverter.ToUInt32(orig, (int)(s.Raw + q)), va = s.Va + q;
                if (va >= SaveTable && va < SaveTable + 139 * 20) continue;
                foreach (Moved m in arrs) if (v == m.Base) log.AppendFormat("  ATTENTION donnee 0x{0:X} = debut de {1} (non modifiee)\n", va, m.Name);
            }
        }
        foreach (Moved m in arrs) log.AppendFormat("{0} : 0x{1:X} x{2} -> 0x{3:X} x{4}\n", m.Name, m.Base, m.Old, m.NewBase, m.New);
        if (HasCode(arrs) || wpat) f = AddPage(f, c, newVa, arrs, gap, wt, log);
        if (wpat) log.AppendFormat("{0} agrandie : 0x{1:X} -> 0x{2:X} (fichier +{3} octets)\n", WpatName, wt[0], newVa - last.Va + vsize, gap + CodePage);
        return f;
    }

    // Disposition de la section : identique au patch, recalculee a partir de son adresse.
    // Chaque groupe commence 16 octets apres une limite de 4 Ko (place pour les acces "debut - element").
    static void Layout(List<Moved> arrs, uint newVa) { Layout(arrs, newVa, HasCode(arrs)); }
    static void Layout(List<Moved> arrs, uint newVa, bool page)
    {
        uint p = newVa + (page ? CodePage : 0); int g = -1;   // page de code en tete (walkers, ou .wpat agrandie)
        foreach (Moved m in arrs)
        {
            if (m.Group != g) { p = Align(p, 4096) + 16; g = m.Group; }   // 4 Ko entre groupes : les fins de boucle (fin + champ) ne tombent pas dans le groupe suivant
            m.NewBase = p; p += (uint)(m.Rows * m.RowNew);
        }
    }

    static uint SectionSize(List<Moved> arrs, uint newVa)
    {
        uint end = 0; foreach (Moved m in arrs) end = Math.Max(end, m.NewEnd);
        return Align(end - newVa + 16, 16);
    }

    // Quels groupes sont dans la section : d'apres sa taille.
    public static bool[] Detect(byte[] f, out uint newVa, out int hdr) { bool wpat; return Detect(f, out newVa, out hdr, out wpat); }

    // wpat : patch fait en agrandissant .wpat (exe zeus-qol) ; hdr est alors l'en-tete de .wpat.
    public static bool[] Detect(byte[] f, out uint newVa, out int hdr, out bool wpat)
    {
        Code c = Code.Load(f);
        newVa = 0; hdr = -1; wpat = false; uint vsize = 0;
        foreach (Sec s in c.Secs) if (s.Name == SectionName) { newVa = s.Va; hdr = s.Header; vsize = s.VSize; }
        if (hdr < 0)
        {
            Sec w = PatchedWpat(c); if (w == null) return null;
            wpat = true; newVa = w.Va + w.RSize - CodePage; hdr = w.Header; vsize = w.VSize - (newVa - w.Va);
        }
        for (int w = 0; w < 2; w++) for (int b = 0; b < 2; b++)
        {
            var arrs = Arrays(w == 1, b == 1); if (arrs.Count == 0) continue;
            Layout(arrs, newVa, wpat || HasCode(arrs));
            if (SectionSize(arrs, newVa) == vsize) return new bool[] { w == 1, b == 1 };
        }
        throw new Exception("section " + (wpat ? WpatName : SectionName) + " de taille inattendue");
    }

    // Exe zeus-qol patche : .wpat en derniere section, terminee par notre page (signature en fin de page).
    static Sec PatchedWpat(Code c)
    {
        Sec w = c.Secs[c.Secs.Count - 1];
        if (w.Name != WpatName || w.RSize < CodePage || w.Raw + w.RSize > c.F.Length) return null;
        byte[] m = Encoding.ASCII.GetBytes(WpatMagic);
        int o = (int)(w.Raw + w.RSize - 32);
        for (int i = 0; i < 16; i++) if (c.F[o + i] != (i < m.Length ? m[i] : 0)) return null;
        return w;
    }

    public static byte[] Unpatch(byte[] patched, StringBuilder log)
    {
        byte[] f = (byte[])patched.Clone();
        uint newVa; int hdr; bool wpat;
        bool[] g = Detect(f, out newVa, out hdr, out wpat);
        if (g == null) throw new Exception("cet exe n'est pas patche");
        f = RemovePage(f, g, newVa, hdr, wpat, log);
        Code c = Code.Load(f);
        var arrs = Arrays(g[0], g[1]); Layout(arrs, newVa, wpat || HasCode(arrs));
        foreach (var kv in StackArrays(g[0], g[1])) PatchStack(f, c, kv.Key, kv.Value, true, log);
        foreach (uint va in ShlSites(g[0], g[1])) PatchShl(f, c, va, 6, 5);
        int restored = 0;
        foreach (Insn n in c.Insns.Values)
        {
            int o = c.ToOff(n.Va);
            foreach (Field fl in n.Fields)
            {
                if (fl.Size != 4) continue;
                uint v = fl.Value, ov = v; int reg; bool cmp = IsCmpImm(n, out reg) && fl.Kind == 'i';
                bool hit = false;
                foreach (Moved m in arrs)
                    if (m.OneBased && v == m.NewBase - (uint)m.Size) { ov = m.Base - (uint)m.Size; hit = true; break; }
                if (!hit) foreach (Moved m in arrs) if (v >= m.NewBase && v < m.NewEnd) { ov = m.Unmap(v); hit = true; break; }
                if (!hit && cmp)
                    foreach (Moved m in arrs)
                        if (!arrs.Exists(x => x.NewBase == m.NewEnd) && v >= m.NewEnd && v <= m.NewEnd + (uint)Math.Max(m.Size, 4)) { ov = v - m.NewEnd + m.End; break; }
                if (ov != v) { Put(f, o + fl.Off, ov); restored++; }
            }
        }
        foreach (Konst kc in Constants(g[0], g[1]))
        {
            Insn n = c.Insns[kc.Va]; bool done = false;
            foreach (Field fl in n.Fields)
                if (fl.Kind == 'i' && fl.Value == kc.New)
                {
                    int o = c.ToOff(n.Va) + fl.Off;
                    if (fl.Size == 4) Put(f, o, kc.Old); else { f[o] = (byte)kc.Old; f[o + 1] = (byte)(kc.Old >> 8); }
                    done = true; break;
                }
            if (!done) throw new Exception(string.Format("constante {0} attendue en 0x{1:X}", kc.New, kc.Va));
        }
        foreach (var kv in SaveEntries(g[0], g[1]))
        {
            Moved m = arrs.Find(x => x.Name == kv.Value);
            int e = c.ToOff(SaveTable + (uint)kv.Key * 20);
            uint unit = BitConverter.ToUInt32(f, e + 8) == 1 && m.Size > 1 ? (uint)m.Size : 1;
            Put(f, e + 4, m.Base); Put(f, e + 12, (uint)m.Old * unit);
        }
        // retrait de la section (.wpat : deja remise a sa taille d'origine par RemovePage)
        int pe = c.Pe; uint secAlign = BitConverter.ToUInt32(f, pe + 24 + 32);
        Sec last = c.Secs[c.Secs.Count - 1];
        if (!wpat)
        {
            for (int i = 0; i < 40; i++) f[hdr + i] = 0;
            f[pe + 6] = (byte)(c.NumSec - 1); f[pe + 7] = (byte)((c.NumSec - 1) >> 8);
            last = c.Secs[c.Secs.Count - 2];
        }
        Put(f, pe + 24 + 56, Align(last.Va + Math.Max(last.VSize, last.RSize) - c.ImageBase, secAlign));
        log.AppendFormat("references remises : {0}\n", restored);
        return f;
    }

    // ---- attribution du plus petit numero de walker libre
    // A l'origine, 0x4D9FE1 prend le numero en tete de la file des numeros libres (FIFO) : apres un chargement
    // (file reconstruite a rebours) puis en jeu, les numeros s'etalent sur toute la plage. Une .sav de l'exe
    // patche garde alors des walkers vivants au-dela de 1999 et ne peut pas revenir a l'exe normal.
    // Le detour cherche le plus petit numero de la file, l'echange avec la tete, puis reprend le code d'origine :
    // la file garde les memes numeros et le meme nombre, seul l'ordre de distribution change.
    const uint AllocVa = 0x4D9FE1, AllocBack = 0x4DA000;           // 31 octets remplaces (prise en tete)
    const uint QHead = 0xDBB840, QCount = 0xDBB848;                // indice de tete, nombre de numeros libres

    // Les 31 octets d'origine, tels qu'apres le deplacement de la file (q) et le changement de taille (cap).
    static byte[] AllocOrig(uint q, uint cap)
    {
        var a = new Asm(AllocVa);
        a.B(0xA1).D(QHead);                    // mov eax, [tete]
        a.B(0x8B, 0x2C, 0x85).D(q);            // mov ebp, [eax*4+file]
        a.B(0x40);                             // inc eax
        a.B(0x3D).D(cap);                      // cmp eax, cap
        a.B(0xA3).D(QHead);                    // mov [tete], eax
        a.B(0x7C, 0x06);                       // jl +6
        a.B(0x89, 0x1D).D(QHead);              // mov [tete], ebx   (ebx = 0)
        return a.Bytes();
    }

    // Routine placee en z ; head/count/q/cap/back parametres pour pouvoir l'executer hors du jeu (test).
    // Entree : nombre > 0 (verifie avant 0x4D9FE1), ebx = 0. Sortie : ebp = numero attribue, tete avancee.
    // Modifie eax, ecx, edx (comme l'original avant 0x4DA000, qui les recharge) ; esi est preserve.
    public static byte[] LowestFree(uint z, uint head, uint count, uint q, uint cap, uint back)
    {
        var a = new Asm(z);
        a.B(0x56);                             // push esi
        a.B(0xA1).D(head);                     // mov eax, [tete]
        a.B(0x8B, 0x0D).D(count);              // mov ecx, [nombre]
        a.B(0x8B, 0xD0);                       // mov edx, eax            meilleure place
        a.B(0x8B, 0x2C, 0x85).D(q);            // mov ebp, [eax*4+file]
        a.B(0x8D, 0x75, 0xFF);                 // lea esi, [ebp-1]        cle = numero - 1 (non signe : 0 -> le plus grand)
        a.Label("suivant");
        a.B(0x49);                             // dec ecx
        a.B(0x74).Rel8("fin");                 // jz fin
        a.B(0x40);                             // inc eax
        a.B(0x3D).D(cap);                      // cmp eax, cap             file circulaire
        a.B(0x7C, 0x02);                       // jl +2
        a.B(0x33, 0xC0);                       // xor eax, eax
        a.B(0x8B, 0x2C, 0x85).D(q);            // mov ebp, [eax*4+file]
        a.B(0x4D);                             // dec ebp
        a.B(0x3B, 0xEE);                       // cmp ebp, esi
        a.B(0x73).Rel8("suivant");             // jae suivant
        a.B(0x8B, 0xF5);                       // mov esi, ebp
        a.B(0x8B, 0xD0);                       // mov edx, eax
        a.B(0xEB).Rel8("suivant");             // jmp suivant
        a.Label("fin");
        a.B(0xA1).D(head);                     // mov eax, [tete]
        a.B(0x8B, 0x2C, 0x95).D(q);            // mov ebp, [edx*4+file]   le plus petit
        a.B(0x8B, 0x0C, 0x85).D(q);            // mov ecx, [eax*4+file]   celui de tete
        a.B(0x89, 0x0C, 0x95).D(q);            // mov [edx*4+file], ecx   echange
        a.B(0x89, 0x2C, 0x85).D(q);            // mov [eax*4+file], ebp
        a.B(0x5E);                             // pop esi
        a.B(0x40);                             // inc eax                 suite du code d'origine
        a.B(0x3D).D(cap);                      // cmp eax, cap
        a.B(0x7C, 0x02);                       // jl +2
        a.B(0x33, 0xC0);                       // xor eax, eax
        a.B(0xA3).D(head);                     // mov [tete], eax
        a.B(0xE9).Rel32(back);                 // jmp back
        return a.Bytes();
    }

    static byte[] AllocDetour(uint z)
    {
        var a = new Asm(AllocVa);
        a.B(0xE9).Rel32(z);
        while (a.Length < AllocBack - AllocVa) a.B(0x90);
        return a.Bytes();
    }

    // Place de la routine : premiere page de .zlim, seule partie initialisee de la section (donnees ajoutees en
    // fin de fichier). Pas de place sure ailleurs : la fin de .text sert deja au French Patch, au Resolution
    // Customiser et au code des exe widescreen, et l'en-tete de l'exe n'a pas de place pour une autre section.
    // Exe zeus-qol : la page est au meme endroit (juste apres l'ancienne fin de .wpat), au bout de .wpat agrandie.
    const uint CodePage = 0x1000;
    static bool HasCode(List<Moved> arrs) { return arrs.Count > 0 && arrs[0].Base == 0xDBB84C; }   // patch des walkers

    // Contenu de la page placee en z : la routine (patch des walkers), puis pour .wpat la signature et les
    // valeurs d'origine de la section (wt : taille virtuelle, taille brute, attributs) dans les 32 derniers octets.
    static byte[] Page(uint z, List<Moved> arrs, uint[] wt)
    {
        byte[] p = new byte[CodePage];
        if (HasCode(arrs))
        {
            byte[] code = LowestFree(z, QHead, QCount, arrs[0].NewBase, (uint)arrs[0].New, AllocBack);
            Array.Copy(code, p, code.Length);
        }
        if (wt != null)
        {
            byte[] m = Encoding.ASCII.GetBytes(WpatMagic);
            Array.Copy(m, 0, p, CodePage - 32, m.Length);
            for (int i = 0; i < 3; i++) Put(p, (int)CodePage - 16 + i * 4, wt[i]);
        }
        return p;
    }

    // Ecrit le detour (walkers) et renvoie le fichier complete par gap zeros puis la page (placee en z).
    static byte[] AddPage(byte[] f, Code c, uint z, List<Moved> arrs, uint gap, uint[] wt, StringBuilder log)
    {
        byte[] page = Page(z, arrs, wt);
        if (HasCode(arrs))
        {
            int o = c.ToOff(AllocVa);
            byte[] orig = AllocOrig(arrs[0].NewBase, (uint)arrs[0].New);
            for (int i = 0; i < orig.Length; i++)
                if (f[o + i] != orig[i]) throw new Exception(string.Format("octets inattendus en 0x{0:X} (attribution des numeros de walkers)", AllocVa + i));
            byte[] det = AllocDetour(z);
            Array.Copy(det, 0, f, o, det.Length);
            log.AppendFormat("attribution du plus petit numero libre : detour 0x{0:X} -> 0x{1:X}\n", AllocVa, z);
        }
        byte[] g = new byte[f.Length + gap + CodePage];
        Array.Copy(f, g, f.Length);
        Array.Copy(page, 0, g, f.Length + gap, CodePage);
        return g;
    }

    // Remet les 31 octets (forme patchee : le reste de l'annulation s'en charge), retire la page et, pour .wpat,
    // les zeros ajoutes avant elle ; la section retrouve ses valeurs d'origine (.wpat) ou redevient non initialisee.
    static byte[] RemovePage(byte[] f, bool[] g, uint newVa, int hdr, bool wpat, StringBuilder log)
    {
        if (!wpat && !g[0]) return f;
        Code c = Code.Load(f);
        var arrs = Arrays(g[0], g[1]); Layout(arrs, newVa, wpat || HasCode(arrs));
        string sec = wpat ? WpatName : SectionName;
        uint raw = BitConverter.ToUInt32(f, hdr + 20), rsize = BitConverter.ToUInt32(f, hdr + 16);
        if (rsize < CodePage || (!wpat && rsize != CodePage) || raw + rsize != f.Length) throw new Exception("page de code de " + sec + " inattendue");
        int po = (int)(raw + rsize - CodePage);
        uint[] wt = null; uint keep = raw;
        if (wpat)
        {
            wt = new uint[3]; for (int i = 0; i < 3; i++) wt[i] = BitConverter.ToUInt32(f, po + (int)CodePage - 16 + i * 4);
            keep = raw + wt[1];
            if (keep > po) throw new Exception("taille d'origine de " + WpatName + " inattendue");
            for (int i = (int)keep; i < po; i++) if (f[i] != 0) throw new Exception("zeros attendus dans " + WpatName + " agrandie");
        }
        byte[] page = Page(newVa, arrs, wt);
        for (int i = 0; i < CodePage; i++) if (f[po + i] != page[i]) throw new Exception("routine inattendue dans " + sec);
        if (HasCode(arrs))
        {
            int o = c.ToOff(AllocVa);
            byte[] det = AllocDetour(newVa);
            for (int i = 0; i < det.Length; i++) if (f[o + i] != det[i]) throw new Exception(string.Format("detour inattendu en 0x{0:X}", AllocVa + i));
            byte[] orig = AllocOrig(arrs[0].NewBase, (uint)arrs[0].New);
            Array.Copy(orig, 0, f, o, orig.Length);
            log.AppendFormat("attribution d'origine remise (0x{0:X})\n", AllocVa);
        }
        byte[] r = new byte[keep];
        Array.Copy(f, r, r.Length);
        if (wpat) { Put(r, hdr + 8, wt[0]); Put(r, hdr + 16, wt[1]); Put(r, hdr + 36, wt[2]); log.AppendFormat("{0} remise a sa taille d'origine\n", WpatName); }
        else { Put(r, hdr + 16, 0); Put(r, hdr + 20, 0); Put(r, hdr + 36, 0xC0000080); }   // section de nouveau non initialisee
        return r;
    }

    // Petit assembleur : octets, dwords, sauts relatifs vers une adresse ou une etiquette.
    class Asm
    {
        readonly uint org; readonly List<byte> b = new List<byte>();
        readonly Dictionary<string, int> labels = new Dictionary<string, int>();
        readonly List<KeyValuePair<int, string>> fix8 = new List<KeyValuePair<int, string>>();
        public Asm(uint origin) { org = origin; }
        public int Length { get { return b.Count; } }
        public Asm B(params byte[] x) { b.AddRange(x); return this; }
        public Asm D(uint v) { b.AddRange(BitConverter.GetBytes(v)); return this; }
        public Asm Rel32(uint target) { return D(target - (org + (uint)b.Count + 4)); }
        public Asm Rel8(string label) { fix8.Add(new KeyValuePair<int, string>(b.Count, label)); b.Add(0); return this; }
        public void Label(string name) { labels[name] = b.Count; }
        public byte[] Bytes()
        {
            foreach (var kv in fix8)
            {
                int d = labels[kv.Value] - (kv.Key + 1);
                if (d < -128 || d > 127) throw new Exception("saut court trop long");
                b[kv.Key] = (byte)(sbyte)d;
            }
            return b.ToArray();
        }
    }

    static void CheckSignature(Code c)
    {
        // table de sauvegarde attendue : entree 28 = walkers, 35 = batiments
        int e = c.ToOff(SaveTable + 28 * 20);
        if (e < 0 || BitConverter.ToUInt32(c.F, e + 4) != 0xDBD788 || BitConverter.ToUInt32(c.F, e + 12) != 2000)
            throw new Exception("exe non reconnu (Zeus anglais 2.1.4.0 attendu)");
    }

    static void Put(byte[] f, int o, uint v) { f[o] = (byte)v; f[o + 1] = (byte)(v >> 8); f[o + 2] = (byte)(v >> 16); f[o + 3] = (byte)(v >> 24); }
}
