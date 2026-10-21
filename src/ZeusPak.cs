// Lecture/reecriture minimale des .pak de Zeus pour agrandir la mappemonde.
// Portage partiel de zeus_mapper (MIT) : compression PKWare DCL et structure des cartes "MAPS".
// zeus_mapper - Copyright (c) 2026 zawapl (Jakub Zawadzki) - licence MIT, texte complet dans LICENSE-zeus_mapper.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZeusPak
{
    // ---------------------------------------------------------------- PKWare DCL
    public static class Pk
    {
        static readonly byte[] OffBits = { 2,4,4,5,5,5,5,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8 };
        static readonly byte[] OffCode = { 0x03,0x0D,0x05,0x19,0x09,0x11,0x01,0x3E,0x1E,0x2E,0x0E,0x36,0x16,0x26,0x06,0x3A,0x1A,0x2A,0x0A,0x32,0x12,0x22,
            0x42,0x02,0x7C,0x3C,0x5C,0x1C,0x6C,0x2C,0x4C,0x0C,0x74,0x34,0x54,0x14,0x64,0x24,0x44,0x04,0x78,0x38,0x58,0x18,
            0x68,0x28,0x48,0x08,0xF0,0x70,0xB0,0x30,0xD0,0x50,0x90,0x10,0xE0,0x60,0xA0,0x20,0xC0,0x40,0x80,0x00 };
        static readonly byte[] LenBits = { 3,2,3,3,4,4,4,5,5,5,5,6,6,6,7,7 };
        static readonly ushort[] LenBase = { 0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x0A,0x0E,0x16,0x26,0x46,0x86,0x106 };
        static readonly byte[] LenCode = { 0x05,0x03,0x01,0x06,0x0A,0x02,0x0C,0x14,0x04,0x18,0x08,0x30,0x10,0x20,0x40,0x00 };
        static readonly byte[] LenExtra = { 0,0,0,0,0,0,0,0,1,2,3,4,5,6,7,8 };
        const int EOF_INDEX = 773, CODEWORDS = 774;

        // ---- explode (portage direct de zeus_mapper)
        class BitReader
        {
            byte[] d; int pos; public ushort Cur; int avail;
            public BitReader(byte[] data, int start, byte first) { d = data; pos = start; Cur = first; avail = 0; }
            public bool Use(int n)
            {
                if (avail >= n) { avail -= n; Cur >>= n; return true; }
                Cur >>= avail;
                if (pos >= d.Length) return false;
                Cur |= (ushort)(d[pos++] << 8);
                Cur >>= (n - avail);
                avail += 8 - n;
                return true;
            }
        }

        static byte[] Jump(byte[] bits, byte[] codes)
        {
            byte[] j = new byte[256];
            for (int i = bits.Length - 1; i >= 0; i--)
            {
                int code = codes[i];
                do { j[code] = (byte)i; code += 1 << bits[i]; } while (code < 0x100);
            }
            return j;
        }

        public static byte[] Explode(byte[] c)
        {
            if (c.Length <= 4) throw new InvalidDataException("explode: trop court");
            if (c[0] != 0) throw new InvalidDataException("explode: mode ASCII non gere");
            int ws = c[1];
            if (ws < 4 || ws > 6) throw new InvalidDataException("explode: fenetre invalide");
            ushort mask = (ushort)(0xFFFF >> (16 - ws));
            byte[] lj = Jump(LenBits, LenCode), oj = Jump(OffBits, OffCode);
            BitReader r = new BitReader(c, 3, c[2]);
            List<byte> o = new List<byte>(65536);
            while (true)
            {
                int token;
                if ((r.Cur & 1) != 0)
                {
                    if (!r.Use(1)) throw new InvalidDataException("explode: flux tronque");
                    int idx = lj[r.Cur & 0xFF];
                    if (!r.Use(LenBits[idx])) throw new InvalidDataException("explode: flux tronque");
                    int eb = LenExtra[idx];
                    if (eb != 0)
                    {
                        int ev = r.Cur & ((1 << eb) - 1);
                        bool ok = r.Use(eb);
                        if (!ok && idx + ev != 270) throw new InvalidDataException("explode: flux tronque");
                        idx = LenBase[idx] + ev;
                    }
                    token = idx + 256;
                }
                else
                {
                    if (!r.Use(1)) throw new InvalidDataException("explode: flux tronque");
                    token = r.Cur & 0xFF;
                    if (!r.Use(8)) throw new InvalidDataException("explode: flux tronque");
                }
                if (token == EOF_INDEX) break;
                if (token < 256) { o.Add((byte)token); continue; }
                int len = token - 254;
                int oi = oj[r.Cur & 0xFF];
                if (!r.Use(OffBits[oi])) throw new InvalidDataException("explode: flux tronque");
                int off;
                if (len == 2) { off = (r.Cur & 3) | (oi << 2); if (!r.Use(2)) throw new InvalidDataException("explode: flux tronque"); }
                else { off = (r.Cur & mask) | (oi << ws); if (!r.Use(ws)) throw new InvalidDataException("explode: flux tronque"); }
                off += 1;
                if (off > o.Count) throw new InvalidDataException("explode: decalage hors donnees");
                for (int k = 0; k < len; k++) o.Add(o[o.Count - off]);
            }
            return o.ToArray();
        }

        // ---- implode : encodeur simple (greedy + chaines de hachage), flux binaire, dictionnaire 4 Ko
        class BitWriter
        {
            List<byte> b = new List<byte> { 0 }; int used;
            public void Write(int n, uint v)
            {
                if (n > 8) { Write(8, v); Write(n - 8, v >> 8); return; }
                int cur = used;
                b[b.Count - 1] |= (byte)((v << used) & 0xFF);
                used += n;
                if (used == 8) { b.Add(0); used = 0; }
                else if (used > 8) { b.Add((byte)(v >> (8 - cur))); used -= 8; }
            }
            public byte[] Finish() { if (used == 0 && b.Count > 1) b.RemoveAt(b.Count - 1); return b.ToArray(); }
        }

        public static byte[] Implode(byte[] data)
        {
            const int WS = 6, DICT = 4096, MAXLEN = 516, CHAIN = 256;
            byte[] cwBits = new byte[CODEWORDS];
            ushort[] cwVal = new ushort[CODEWORDS];
            for (int i = 0; i < 256; i++) { cwBits[i] = 9; cwVal[i] = (ushort)(i << 1); }
            int ci = 256;
            for (int c = 0; c < 16; c++)
                for (int i = 0; i < (1 << LenExtra[c]); i++)
                {
                    cwBits[ci] = (byte)(1 + LenBits[c] + LenExtra[c]);
                    cwVal[ci] = (ushort)(1 | (LenCode[c] << 1) | (i << (LenBits[c] + 1)));
                    ci++;
                }

            BitWriter w = new BitWriter();
            w.Write(8, 0);
            w.Write(8, WS);

            int n = data.Length;
            int[] head = new int[1 << 16];
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            int[] prev = new int[Math.Max(1, n)];
            int pos = 0;
            Func<int, int> hash = p => (data[p] << 8 | data[p + 1]) ^ (data[p + 2] << 4);
            Action<int> insert = p => { if (p + 2 < n) { int h = hash(p) & 0xFFFF; prev[p] = head[h]; head[h] = p; } };

            while (pos < n)
            {
                int bestLen = 0, bestDist = 0;
                if (pos + 2 < n)
                {
                    int h = hash(pos) & 0xFFFF, cand = head[h], steps = 0;
                    while (cand >= 0 && pos - cand <= DICT && steps++ < CHAIN)
                    {
                        int l = 0, max = Math.Min(MAXLEN, n - pos);
                        while (l < max && data[cand + l] == data[pos + l]) l++;
                        if (l > bestLen) { bestLen = l; bestDist = pos - cand; if (l == max) break; }
                        cand = prev[cand];
                    }
                }
                // Correspondance de 2 octets : seulement si proche (le format le limite a 256).
                if (bestLen < 2 && pos + 1 < n)
                    for (int d = 1; d <= Math.Min(256, pos); d++)
                        if (data[pos - d] == data[pos] && data[pos - d + 1] == data[pos + 1]) { bestLen = 2; bestDist = d; break; }
                if (bestLen == 2 && bestDist > 256) bestLen = 0;

                if (bestLen >= 2)
                {
                    int idx = bestLen + 254;
                    w.Write(cwBits[idx], cwVal[idx]);
                    int off = bestDist - 1;
                    if (bestLen == 2) { int oi = off >> 2; w.Write(OffBits[oi], OffCode[oi]); w.Write(2, (uint)(off & 3)); }
                    else { int oi = off >> WS; w.Write(OffBits[oi], OffCode[oi]); w.Write(WS, (uint)(off & 0x3F)); }
                    for (int k = 0; k < bestLen; k++) insert(pos + k);
                    pos += bestLen;
                }
                else
                {
                    w.Write(cwBits[data[pos]], cwVal[data[pos]]);
                    insert(pos);
                    pos++;
                }
            }
            w.Write(cwBits[EOF_INDEX], cwVal[EOF_INDEX]);
            return w.Finish();
        }
    }

    // ---------------------------------------------------------------- carte "MAPS"
    // Un bloc de la carte, decrit par le manifeste : tel que lu (Raw) et, s'il est compresse, son contenu.
    public class Block
    {
        public int Index, Start, End;      // position dans le fichier (Start = debut du prefixe de taille eventuel)
        public bool Compressed;            // prefixe i32 present
        public int ElemSize, Count;
        public byte[] Data;                // contenu decompresse (ou brut)
        public byte[] Raw;                 // octets d'origine, prefixe compris
        public bool Modified;
    }

    public class MapFile
    {
        public int Start, End;
        public byte[] Header;              // "MAPS" + versions + manifeste
        public List<Block> Blocks = new List<Block>();
        public uint Version1;

        public static MapFile Parse(byte[] f, int start)
        {
            if (Encoding.ASCII.GetString(f, start, 4) != "MAPS") throw new InvalidDataException("pas de MAPS a " + start);
            MapFile m = new MapFile();
            m.Start = start;
            m.Version1 = BitConverter.ToUInt32(f, start + 4);
            uint v2 = BitConverter.ToUInt32(f, start + 8);
            if (v2 != 33 || m.Version1 < 227 || m.Version1 > 321) throw new InvalidDataException("version de carte inattendue");
            int man = start + 12;
            int p = man + 300 * 20;
            m.Header = Sub(f, start, p - start);
            for (int i = 4; i < 300; i++)
            {
                int e = man + i * 20;
                uint comp = BitConverter.ToUInt32(f, e), size = BitConverter.ToUInt32(f, e + 8), count = BitConverter.ToUInt32(f, e + 12);
                if (size == 0) break;
                Block b = new Block();
                b.Index = i; b.Start = p; b.ElemSize = (int)size; b.Count = (int)count; b.Compressed = comp != 0;
                int rawLen = (int)(size * count);
                if (b.Compressed)
                {
                    int cs = BitConverter.ToInt32(f, p); p += 4;
                    if (cs < 0) { b.Data = Sub(f, p, rawLen); p += rawLen; }
                    else
                    {
                        b.Data = Pk.Explode(Sub(f, p, cs)); p += cs;
                        if (b.Data.Length < rawLen) throw new InvalidDataException("bloc " + i + " trop court");
                    }
                }
                else { b.Data = Sub(f, p, rawLen); p += rawLen; }
                b.End = p;
                b.Raw = Sub(f, b.Start, b.End - b.Start);
                m.Blocks.Add(b);
            }
            m.End = p;
            return m;
        }

        public Block Get(int index) { foreach (Block b in Blocks) if (b.Index == index) return b; return null; }

        public byte[] Build()
        {
            MemoryStream ms = new MemoryStream();
            ms.Write(Header, 0, Header.Length);
            foreach (Block b in Blocks)
            {
                if (!b.Modified) { ms.Write(b.Raw, 0, b.Raw.Length); continue; }
                if (!b.Compressed) { ms.Write(b.Data, 0, b.Data.Length); continue; }
                byte[] c = Pk.Implode(b.Data);
                // Controle : le flux produit doit se relire a l'identique.
                byte[] back = Pk.Explode(c);
                if (back.Length != b.Data.Length) throw new Exception("implode: relecture de taille differente");
                for (int i = 0; i < back.Length; i++) if (back[i] != b.Data[i]) throw new Exception("implode: relecture differente");
                if (c.Length < b.Data.Length) { ms.Write(BitConverter.GetBytes(c.Length), 0, 4); ms.Write(c, 0, c.Length); }
                else { ms.Write(BitConverter.GetBytes(int.MinValue), 0, 4); ms.Write(b.Data, 0, b.Data.Length); }
            }
            return ms.ToArray();
        }

        public static byte[] Sub(byte[] f, int start, int len) { byte[] r = new byte[len]; Array.Copy(f, start, r, 0, len); return r; }
    }

    // ---------------------------------------------------------------- .pak complet
    public class PakFile
    {
        public byte[] Bytes;
        public bool NewVer;
        public int DataLength, PaddedEnd;
        public MapFile Settings;           // copie de la carte dans les reglages
        public List<MapFile> Maps = new List<MapFile>();

        public static PakFile Load(string path)
        {
            PakFile k = new PakFile();
            k.Bytes = File.ReadAllBytes(path);
            byte[] f = k.Bytes;
            uint v1 = BitConverter.ToUInt32(f, 0), v2 = BitConverter.ToUInt32(f, 4);
            if (v1 != 8871) throw new InvalidDataException("version de reglages inattendue : " + v1);
            k.NewVer = v2 == 26;
            k.DataLength = k.NewVer ? 842931 : 841863;
            k.PaddedEnd = k.NewVer ? 800361 : 799297;

            // La copie de la carte suit des champs de taille fixe : premiere entete MAPS valide avant la fin reservee.
            for (int i = 8; i < k.PaddedEnd - 12; i++)
            {
                if (f[i] != 'M' || f[i + 1] != 'A' || f[i + 2] != 'P' || f[i + 3] != 'S') continue;
                uint mv1 = BitConverter.ToUInt32(f, i + 4), mv2 = BitConverter.ToUInt32(f, i + 8);
                if (mv2 != 33 || mv1 < 227 || mv1 > 321) continue;
                k.Settings = MapFile.Parse(f, i);
                break;
            }
            if (k.Settings == null) throw new InvalidDataException("copie de carte introuvable dans les reglages");
            if (k.Settings.End > k.PaddedEnd) throw new InvalidDataException("copie de carte au-dela de la zone reservee");

            int p = k.DataLength;
            while (p < f.Length)
            {
                MapFile m = MapFile.Parse(f, p);
                k.Maps.Add(m);
                p = m.End;
            }
            return k;
        }

        public byte[] Build()
        {
            byte[] f = Bytes;
            MemoryStream ms = new MemoryStream();
            ms.Write(f, 0, Settings.Start);
            byte[] sm = Settings.Build();
            ms.Write(sm, 0, sm.Length);
            int room = PaddedEnd - Settings.Start;
            if (sm.Length > room) throw new Exception("la copie de carte ne tient plus dans la zone reservee (" + sm.Length + " > " + room + ")");
            // Remplissage : contenu d'origine tronque ou complete par des zeros, comme zeus_mapper.
            int padLen = room - sm.Length;
            int origPad = PaddedEnd - Settings.End;
            for (int i = 0; i < padLen; i++) ms.WriteByte(i < origPad ? f[Settings.End + i] : (byte)0);
            ms.Write(f, PaddedEnd, DataLength - PaddedEnd);
            foreach (MapFile m in Maps) { byte[] b = m.Build(); ms.Write(b, 0, b.Length); }
            return ms.ToArray();
        }

        public IEnumerable<MapFile> AllMaps() { yield return Settings; foreach (MapFile m in Maps) yield return m; }
    }

    // ---------------------------------------------------------------- mise a l'echelle de la mappemonde
    public static class WorldMap
    {
        // Bloc 17 : 200 elements (40 ou 72 octets) ; variant @0, x @4, y @6 (u16).
        // Bloc 18 : 232 routes de 324 octets ; points (x, y, ?) de 6 octets a partir de @8, nombre @321.
        public static string Scale(MapFile m, double s, bool apply)
        {
            StringBuilder sb = new StringBuilder();
            Block e = m.Get(17), t = m.Get(18);
            int elems = 0, pts = 0;
            for (int i = 0; i < e.Count; i++)
            {
                int o = i * e.ElemSize;
                if (e.Data[o] == 0) continue;
                ushort x = BitConverter.ToUInt16(e.Data, o + 4), y = BitConverter.ToUInt16(e.Data, o + 6);
                sb.AppendFormat("    element {0,3} variant {1} : ({2},{3})", i, e.Data[o], x, y);
                if (apply)
                {
                    ushort nx = (ushort)Math.Round(x * s), ny = (ushort)Math.Round(y * s);
                    Array.Copy(BitConverter.GetBytes(nx), 0, e.Data, o + 4, 2);
                    Array.Copy(BitConverter.GetBytes(ny), 0, e.Data, o + 6, 2);
                    sb.AppendFormat(" -> ({0},{1})", nx, ny);
                }
                sb.AppendLine();
                elems++;
            }
            for (int r = 0; r < t.Count; r++)
            {
                int o = r * t.ElemSize;
                int cnt = t.Data[o + 321];
                if (cnt == 0) continue;
                if (cnt > 50) throw new InvalidDataException("route " + r + " : " + cnt + " points");
                sb.AppendFormat("    route {0,3} : {1} points, existe={2}, premier ({3},{4})\n", r, cnt, t.Data[o + 322],
                    BitConverter.ToUInt16(t.Data, o + 8), BitConverter.ToUInt16(t.Data, o + 10));
                if (apply)
                    for (int k = 0; k < cnt; k++)
                    {
                        int q = o + 8 + 6 * k;
                        ushort x = BitConverter.ToUInt16(t.Data, q), y = BitConverter.ToUInt16(t.Data, q + 2);
                        Array.Copy(BitConverter.GetBytes((ushort)Math.Round(x * s)), 0, t.Data, q, 2);
                        Array.Copy(BitConverter.GetBytes((ushort)Math.Round(y * s)), 0, t.Data, q + 2, 2);
                    }
                pts += cnt;
            }
            if (apply) { e.Modified = true; t.Modified = true; }
            sb.Insert(0, string.Format("  {0} elements visibles, {1} points de routes\n", elems, pts));
            return sb.ToString();
        }

        public static uint Background(MapFile m) { return BitConverter.ToUInt32(m.Get(26).Data, 0); }
    }
}
