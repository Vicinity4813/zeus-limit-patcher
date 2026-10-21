// Zeus Data Limits : double les limites de donnees d'un Zeus.exe anglais 2.1.4.0 (walkers 2000 -> 4000,
// chemins 1000 -> 2000, batiments 4000 -> 8000) et convertit les sauvegardes .sav dans les deux sens.
// Interface en francais si Windows est en francais, en anglais sinon (--lang fr|en pour forcer).
// En ligne de commande : "Zeus Data Limits.exe" --info <Zeus.exe|x.sav> | --patch <Zeus.exe> | --unpatch <Zeus.exe>
//                                               | --enlarge <x.sav> | --reduce <x.sav>
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ZeusPak;

[assembly: System.Reflection.AssemblyTitle("Zeus Data Limits")]
[assembly: System.Reflection.AssemblyDescription("Doubles the data limits of Zeus.exe 2.1.4.0 (walkers, paths, buildings) and converts saved games")]
[assembly: System.Reflection.AssemblyCompany("Outils Zeus (projet amateur)")]
[assembly: System.Reflection.AssemblyProduct("Zeus Data Limits")]
[assembly: System.Reflection.AssemblyCopyright("© 2026 Vicinity4813 - MIT License")]
[assembly: System.Resources.NeutralResourcesLanguage("en")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0")]

static class L
{
    public static bool Fr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
    public static string T(string fr, string en) { return Fr ? fr : en; }
}

enum Kind { Info, Ok, Warn, Error }

// Operations sur les fichiers, sans interface : utilisees par les boutons et par la ligne de commande.
// Chaque operation ecrit ses messages par Log et renvoie true si le fichier a ete ecrit (ou deja au bon etat).
static class Ops
{
    public static Action<string, Kind> Log = (m, k) => { };

    // .bak de l'original : un .bak existant (le plus ancien original) n'est jamais ecrase.
    static void Backup(string path)
    {
        string bak = path + ".bak";
        if (File.Exists(bak)) { Log(L.T("Copie .bak déjà présente, gardée telle quelle : ", ".bak copy already present, kept as is: ") + Path.GetFileName(bak), Kind.Info); return; }
        File.Copy(path, bak);
        Log(L.T("Copie de l'original : ", "Copy of the original: ") + Path.GetFileName(bak), Kind.Info);
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---- exe
    public enum ExeState { Missing, Unknown, Normal, Patched }

    public static ExeState ExeInfo(string path, out string text)
    {
        text = "";
        if (!File.Exists(path)) return ExeState.Missing;
        byte[] f = File.ReadAllBytes(path);
        string bak = File.Exists(path + ".bak") ? L.T("  (copie .bak présente)", "  (.bak copy present)") : "";
        try
        {
            uint va; int hdr;
            bool[] g = Limits.Detect(f, out va, out hdr);
            if (g != null)
            {
                text = (g[0] && g[1] ? L.T("Limites déjà doublées (walkers, chemins, bâtiments)", "Limits already doubled (walkers, paths, buildings)")
                                     : L.T("Limites déjà en partie doublées (essai)", "Limits partly doubled (test build)")) + bak;
                return ExeState.Patched;
            }
            Limits.Patch(f, true, true, new StringBuilder());   // en memoire : l'exe est-il patchable ?
            text = L.T("Exe anglais 2.1.4.0 — limites normales", "English exe 2.1.4.0 — normal limits") + bak;
            return ExeState.Normal;
        }
        catch (Exception ex)
        {
            text = L.T("Exe non reconnu (Zeus anglais 2.1.4.0 attendu)", "Exe not recognised (English Zeus 2.1.4.0 expected)")
                 + (ex.Message.StartsWith("exe non reconnu") ? "" : " : " + ex.Message);
            return ExeState.Unknown;
        }
    }

    public static bool PatchExe(string path)
    {
        byte[] orig = File.ReadAllBytes(path);
        byte[] f = Limits.Patch(orig, true, true, new StringBuilder());
        // controle : l'annulation du nouvel exe doit redonner l'original octet pour octet
        if (!Same(Limits.Unpatch(f, new StringBuilder()), orig))
        {
            Log(L.T("Contrôle échoué : l'annulation ne redonnerait pas l'exe d'origine. Rien n'a été écrit.",
                    "Check failed: undoing would not give back the original exe. Nothing was written."), Kind.Error);
            return false;
        }
        Backup(path);
        File.WriteAllBytes(path, f);
        Log(L.T("Limites doublées : walkers 4000, chemins 2000, bâtiments 8000. Contrôle de l'annulation : OK.",
                "Limits doubled: walkers 4000, paths 2000, buildings 8000. Undo check: OK."), Kind.Ok);
        Log(L.T("Les sauvegardes de l'exe normal doivent être agrandies (partie 2) avant d'être chargées.",
                "Saved games from the normal exe must be enlarged (part 2) before being loaded."), Kind.Warn);
        return true;
    }

    public static bool UnpatchExe(string path)
    {
        byte[] f = File.ReadAllBytes(path);
        byte[] orig = Limits.Unpatch(f, new StringBuilder());
        // controle : repatcher l'exe rendu doit redonner l'exe actuel
        if (!Same(Limits.Patch(orig, true, true, new StringBuilder()), f))
        {
            Log(L.T("Contrôle échoué : l'exe rendu ne correspond pas. Rien n'a été écrit.",
                    "Check failed: the restored exe does not match. Nothing was written."), Kind.Error);
            return false;
        }
        File.WriteAllBytes(path, orig);
        Log(L.T("Exe remis aux limites normales (identique à l'original). Contrôle : OK.",
                "Exe back to normal limits (identical to the original). Check: OK."), Kind.Ok);
        Log(L.T("Les sauvegardes faites avec les limites doublées doivent être réduites (partie 2) avant d'être chargées.",
                "Saved games made with doubled limits must be reduced (part 2) before being loaded."), Kind.Warn);
        return true;
    }

    // ---- sauvegardes
    public static SavLimits.State? SavInfo(string path, out string text)
    {
        text = "";
        if (!File.Exists(path)) return null;
        try
        {
            SavLimitsFile s = SavLimitsFile.Load(File.ReadAllBytes(path));
            SavLimits.State st = SavLimits.GetState(s);
            var sb = new StringBuilder(st == SavLimits.State.Normal ? L.T("Limites normales", "Normal limits")
                                     : st == SavLimits.State.Agrandi ? L.T("Limites doublées", "Doubled limits") : L.T("Format mixte", "Mixed format"));
            for (int pi = 0; pi < s.Parts.Count; pi++)
            {
                SavPart p = s.Parts[pi];
                int[] a = SavLimits.Alive(p);
                sb.Append(" — ");
                if (s.Parts.Count > 1) sb.Append(L.T("partie ", "part ") + (pi + 1) + " : ");
                sb.AppendFormat(L.T("{0} walkers / {1}, {2} bâtiments / {3}", "{0} walkers / {1}, {2} buildings / {3}"), a[0], p.Get(28).Count, a[1], p.Get(35).Count);
            }
            if (File.Exists(path + ".bak")) sb.Append(L.T("  (copie .bak présente)", "  (.bak copy present)"));
            text = sb.ToString();
            return st;
        }
        catch (Exception ex)
        {
            text = L.T("Sauvegarde non reconnue : ", "Saved game not recognised: ") + ex.Message;
            return null;
        }
    }

    // Raisons du refus de la reduction, en clair ; null si la reduction est possible.
    public static string WhyNotReducible(SavLimitsFile s)
    {
        var why = new StringBuilder();
        for (int pi = 0; pi < s.Parts.Count; pi++)
        {
            int[] k = SavLimits.Blockers(s.Parts[pi]);
            string part = s.Parts.Count > 1 ? L.T("Partie ", "Part ") + (pi + 1) + " : " : "";
            if (k[0] > 0) why.AppendLine(part + string.Format(L.T("{0} walkers portent un numéro au-delà de 1999.", "{0} walkers have a number above 1999."), k[0]));
            if (k[1] > 0) why.AppendLine(part + string.Format(L.T("{0} chemins portent un numéro au-delà de 999.", "{0} paths have a number above 999."), k[1]));
            if (k[2] > 0) why.AppendLine(part + string.Format(L.T("{0} bâtiments portent un numéro au-delà de 3999.", "{0} buildings have a number above 3999."), k[2]));
            if (k[3] > 0) why.AppendLine(part + string.Format(L.T("{0} cases de la liste des bâtiments sont au-delà de 4000.", "{0} entries of the building list are above 4000."), k[3]));
        }
        return why.Length == 0 ? null : why.ToString();
    }

    public static readonly string ReduceAdvice = L.T(
        "Continuez à jouer avec l'exe aux limites doublées jusqu'à passer sous 2000 walkers (et 4000 bâtiments) : le jeu libère alors les numéros hauts. Enregistrez, puis réessayez.",
        "Keep playing with the doubled-limit exe until the city is below 2000 walkers (and 4000 buildings): the game then frees the high numbers. Save, then try again.");

    // Une sauvegarde ; ecrite a la place de l'originale (avec .bak). Prevue pour etre appelee en boucle sur un dossier.
    public static bool ConvertSav(string path, bool enlarge)
    {
        byte[] f = File.ReadAllBytes(path);
        if (!enlarge)
        {
            string why = WhyNotReducible(SavLimitsFile.Load(f));
            if (why != null)
            {
                Log(Path.GetFileName(path) + L.T(" : impossible de revenir aux limites normales sans perte.", ": cannot go back to normal limits without loss."), Kind.Error);
                Log(why.TrimEnd(), Kind.Error);
                Log(ReduceAdvice, Kind.Warn);
                return false;
            }
        }
        byte[] r = SavLimits.Convert(f, enlarge, new StringBuilder());
        if (r == null)
        {
            Log(Path.GetFileName(path) + (enlarge ? L.T(" : déjà aux limites doublées, rien à faire.", ": already at doubled limits, nothing to do.")
                                                  : L.T(" : déjà aux limites normales, rien à faire.", ": already at normal limits, nothing to do.")), Kind.Ok);
            return true;
        }
        Backup(path);
        File.WriteAllBytes(path, r);
        Log(Path.GetFileName(path) + (enlarge ? L.T(" : agrandie, à charger avec l'exe aux limites doublées.", ": enlarged, load it with the doubled-limit exe.")
                                              : L.T(" : réduite, à charger avec l'exe normal.", ": reduced, load it with the normal exe.")), Kind.Ok);
        return true;
    }
}

static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [STAThread]
    static int Main(string[] args)
    {
        int li = Array.IndexOf(args, "--lang");
        if (li >= 0 && li + 1 < args.Length) { L.Fr = args[li + 1] == "fr"; var r = new System.Collections.Generic.List<string>(args); r.RemoveRange(li, 2); args = r.ToArray(); }
        if (args.Length > 0) { AttachConsole(-1); return Cli(args); }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    static int Cli(string[] a)
    {
        if (a.Length != 2) { Console.WriteLine("\"Zeus Data Limits.exe\" [--lang fr|en] --info <Zeus.exe|x.sav> | --patch <Zeus.exe> | --unpatch <Zeus.exe> | --enlarge <x.sav> | --reduce <x.sav>"); return 2; }
        bool err = false;
        Ops.Log = (m, k) => { Console.WriteLine((k == Kind.Error ? "ERROR " : k == Kind.Warn ? "note " : "") + m); if (k == Kind.Error) err = true; };
        string p = Path.GetFullPath(a[1]);
        try
        {
            bool ok;
            switch (a[0])
            {
                case "--info":
                    string t;
                    if (p.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) Ops.SavInfo(p, out t); else Ops.ExeInfo(p, out t);
                    Console.WriteLine(t); ok = true; break;
                case "--patch": ok = Ops.PatchExe(p); break;
                case "--unpatch": ok = Ops.UnpatchExe(p); break;
                case "--enlarge": ok = Ops.ConvertSav(p, true); break;
                case "--reduce": ok = Ops.ConvertSav(p, false); break;
                default: Console.WriteLine("unknown option " + a[0]); return 2;
            }
            return ok && !err ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine("ERROR " + ex.Message); return 1; }
    }
}

class MainForm : Form
{
    static readonly Color Brown = Color.FromArgb(90, 50, 20);
    Label title, intro, lExeInfo, lExeText, lSavInfo, lSavText;
    TextBox tExe, tSav;
    Button bExe, bPatch, bUnpatch, bSav, bEnlarge, bReduce;
    GroupBox gExe, gSav;
    RichTextBox log;
    bool busy;
    ToolTip tip = new ToolTip();
    Ops.ExeState exeState;
    SavLimits.State? savState;

    public MainForm()
    {
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 600);
        MinimumSize = new Size(680, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 238, 226);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        AnchorStyles lr = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        title = new Label { Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Brown, AutoSize = true, Location = new Point(16, 12) };
        intro = new Label { Location = new Point(16, 52), Size = new Size(728, 44), Anchor = lr };

        gExe = new GroupBox { Location = new Point(16, 100), Size = new Size(728, 150), Anchor = lr };
        lExeText = new Label { Location = new Point(12, 22), Size = new Size(704, 20), Anchor = lr };
        tExe = new TextBox { Location = new Point(12, 46), Width = 596, Anchor = lr };
        bExe = new Button { Location = new Point(616, 44), Size = new Size(100, 27), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        lExeInfo = new Label { Location = new Point(12, 76), Size = new Size(704, 22), ForeColor = Brown, Font = new Font(Font, FontStyle.Bold), Anchor = lr };
        bPatch = new Button { Location = new Point(12, 104), Size = new Size(260, 34), Font = new Font(Font, FontStyle.Bold) };
        bUnpatch = new Button { Location = new Point(284, 104), Size = new Size(260, 34) };
        gExe.Controls.AddRange(new Control[] { lExeText, tExe, bExe, lExeInfo, bPatch, bUnpatch });

        gSav = new GroupBox { Location = new Point(16, 260), Size = new Size(728, 170), Anchor = lr };
        lSavText = new Label { Location = new Point(12, 22), Size = new Size(704, 40), Anchor = lr };
        tSav = new TextBox { Location = new Point(12, 66), Width = 596, Anchor = lr };
        bSav = new Button { Location = new Point(616, 64), Size = new Size(100, 27), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        lSavInfo = new Label { Location = new Point(12, 96), Size = new Size(704, 22), ForeColor = Brown, Font = new Font(Font, FontStyle.Bold), Anchor = lr, AutoEllipsis = true };
        bEnlarge = new Button { Location = new Point(12, 124), Size = new Size(260, 34) };
        bReduce = new Button { Location = new Point(284, 124), Size = new Size(260, 34) };
        gSav.Controls.AddRange(new Control[] { lSavText, tSav, bSav, lSavInfo, bEnlarge, bReduce });

        log = new RichTextBox { Location = new Point(16, 442), Size = new Size(728, 142), ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9f),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

        bExe.Click += delegate
        {
            using (OpenFileDialog d = new OpenFileDialog { Filter = "Zeus.exe|*.exe" })
                if (d.ShowDialog(this) == DialogResult.OK) { tExe.Text = d.FileName; ShowInfo(); }
        };
        bSav.Click += delegate
        {
            using (OpenFileDialog d = new OpenFileDialog { Filter = L.T("Sauvegardes Zeus", "Zeus saved games") + " (*.sav)|*.sav" })
            {
                if (File.Exists(tSav.Text.Trim())) d.InitialDirectory = Path.GetDirectoryName(tSav.Text.Trim());
                else if (File.Exists(tExe.Text.Trim()) && Directory.Exists(Path.Combine(Path.GetDirectoryName(tExe.Text.Trim()), "Save")))
                    d.InitialDirectory = Path.Combine(Path.GetDirectoryName(tExe.Text.Trim()), "Save");
                if (d.ShowDialog(this) == DialogResult.OK) { tSav.Text = d.FileName; ShowInfo(); }
            }
        };
        tExe.Leave += delegate { ShowInfo(); };
        tSav.Leave += delegate { ShowInfo(); };
        bPatch.Click += delegate { Run(tExe, Ops.PatchExe); };
        bUnpatch.Click += delegate
        {
            if (MessageBox.Show(this, L.T("Remettre cet exe aux limites normales ?", "Put this exe back to normal limits?"), Text, MessageBoxButtons.OKCancel) == DialogResult.OK)
                Run(tExe, Ops.UnpatchExe);
        };
        bEnlarge.Click += delegate { Run(tSav, p => Ops.ConvertSav(p, true)); };
        bReduce.Click += delegate { Run(tSav, p => Ops.ConvertSav(p, false)); };

        Controls.AddRange(new Control[] { title, intro, gExe, gSav, log });
        ApplyLanguage();
        UpdateButtons();
    }

    void ApplyLanguage()
    {
        Text = title.Text = "Zeus Data Limits";
        intro.Text = L.T("Double les limites de données d'un Zeus.exe anglais 2.1.4.0 (GOG, Steam, widescreen, avec ou sans French Patch) : walkers 2000 → 4000, chemins 1000 → 2000, bâtiments 4000 → 8000.",
                         "Doubles the data limits of an English Zeus.exe 2.1.4.0 (GOG, Steam, widescreen, with or without the French Patch): walkers 2000 → 4000, paths 1000 → 2000, buildings 4000 → 8000.");
        gExe.Text = L.T("1. Le jeu (Zeus.exe)", "1. The game (Zeus.exe)");
        lExeText.Text = L.T("Une copie « .bak » de l'original est faite à côté. L'annulation redonne l'exe d'origine à l'octet près.",
                            "A \".bak\" copy of the original is made next to it. Undoing gives back the original exe byte for byte.");
        bExe.Text = bSav.Text = L.T("Parcourir…", "Browse…");
        bPatch.Text = L.T("Doubler les limites", "Double the limits");
        bUnpatch.Text = L.T("Annuler le patch", "Undo the patch");
        gSav.Text = L.T("2. Une sauvegarde (.sav)", "2. A saved game (.sav)");
        lSavText.Text = L.T("Une sauvegarde ne se charge qu'avec un exe aux mêmes limites : agrandir pour l'exe patché, réduire pour revenir à l'exe normal. Une copie « .bak » est faite à côté.",
                            "A saved game only loads with an exe of the same limits: enlarge it for the patched exe, reduce it to go back to the normal exe. A \".bak\" copy is made next to it.");
        bEnlarge.Text = L.T("Agrandir (exe patché)", "Enlarge (patched exe)");
        bReduce.Text = L.T("Réduire (exe normal)", "Reduce (normal exe)");
    }

    void UpdateButtons()
    {
        bool idle = !busy;
        bExe.Enabled = bSav.Enabled = tExe.Enabled = tSav.Enabled = idle;
        bPatch.Enabled = idle && exeState == Ops.ExeState.Normal;
        bUnpatch.Enabled = idle && exeState == Ops.ExeState.Patched;
        bEnlarge.Enabled = idle && savState != null && savState != SavLimits.State.Agrandi;
        bReduce.Enabled = idle && savState != null && savState != SavLimits.State.Normal;
    }

    // Lecture de l'etat des deux fichiers en arriere-plan (l'analyse de l'exe prend environ une seconde).
    void ShowInfo()
    {
        if (busy) return;
        string pe = tExe.Text.Trim(), ps = tSav.Text.Trim();
        busy = true; UpdateButtons();
        lExeInfo.Text = File.Exists(pe) ? L.T("Analyse…", "Reading…") : "";
        lSavInfo.Text = File.Exists(ps) ? L.T("Analyse…", "Reading…") : "";
        Thread t = new Thread(() =>
        {
            string te, ts;
            Ops.ExeState es; SavLimits.State? ss;
            try { es = Ops.ExeInfo(pe, out te); } catch (Exception ex) { es = Ops.ExeState.Unknown; te = ex.Message; }
            try { ss = Ops.SavInfo(ps, out ts); } catch (Exception ex) { ss = null; ts = ex.Message; }
            BeginInvoke((Action)(() =>
            {
                exeState = es; savState = ss; lExeInfo.Text = te; lSavInfo.Text = ts;
                tip.SetToolTip(lSavInfo, ts);
                busy = false; UpdateButtons();
            }));
        });
        t.IsBackground = true;
        t.Start();
    }

    void Run(TextBox box, Func<string, bool> work)
    {
        if (busy) return;
        string p = box.Text.Trim();
        if (!File.Exists(p)) return;
        busy = true; UpdateButtons();
        log.Clear();
        Ops.Log = (m, k) => BeginInvoke((Action)(() =>
        {
            log.SelectionStart = log.TextLength;
            log.SelectionColor = k == Kind.Error ? Color.FromArgb(180, 20, 20) : k == Kind.Warn ? Color.FromArgb(170, 100, 0) : k == Kind.Ok ? Color.FromArgb(20, 120, 40) : Color.Black;
            log.AppendText(m + Environment.NewLine);
            log.ScrollToCaret();
        }));
        Thread t = new Thread(() =>
        {
            bool ok = false;
            try { ok = work(p); }
            catch (IOException ex) { Ops.Log(L.T("Fichier inaccessible (le jeu est-il ouvert ?) : ", "File not accessible (is the game running?): ") + ex.Message, Kind.Error); }
            catch (UnauthorizedAccessException ex) { Ops.Log(L.T("Écriture refusée (dossier protégé ou fichier en lecture seule) : ", "Writing denied (protected folder or read-only file): ") + ex.Message, Kind.Error); }
            catch (Exception ex) { Ops.Log(L.T("Erreur : ", "Error: ") + ex.Message, Kind.Error); }
            BeginInvoke((Action)(() =>
            {
                busy = false;
                if (!ok && box == tSav)
                    MessageBox.Show(this, log.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowInfo();
            }));
        });
        t.IsBackground = true;
        t.Start();
    }
}
