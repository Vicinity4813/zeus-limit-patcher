// Zeus Limit Patcher : double les limites de donnees d'un Zeus.exe anglais 2.1.4.0 (walkers 2000 -> 4000,
// chemins 1000 -> 2000, batiments 4000 -> 8000). L'exe d'origine est copie dans le dossier "Zeus.exe original"
// a cote du patcheur (pas de .bak a cote du jeu, qui pourrait entrer en conflit avec d'autres outils).
// Les anciennes sauvegardes s'ouvrent avec l'exe patche ; celles qu'il ecrit ne s'ouvrent plus avec l'exe d'origine.
// Interface en francais si Windows est en francais, en anglais sinon (--lang fr|en pour forcer).
// En ligne de commande : "Zeus Limit Patcher.exe" --info <Zeus.exe> | --patch <Zeus.exe>
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Zeus Limit Patcher")]
[assembly: System.Reflection.AssemblyDescription("Doubles the data limits of Zeus.exe 2.1.4.0 (walkers, paths, buildings)")]
[assembly: System.Reflection.AssemblyCompany("Outils Zeus (projet amateur)")]
[assembly: System.Reflection.AssemblyProduct("Zeus Limit Patcher")]
[assembly: System.Reflection.AssemblyCopyright("© 2026 Vicinity4813 - MIT License")]
[assembly: System.Resources.NeutralResourcesLanguage("en")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0")]

// Langue de l'interface. Le choix est memorise dans %LOCALAPPDATA%\Zeus Limit Patcher\langue.txt ;
// par defaut, la langue de Windows (francais si Windows est en francais, anglais sinon).
static class L
{
    public static bool Fr;

    static string SettingsFile
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus Limit Patcher", "langue.txt"); }
    }

    public static void Load()
    {
        Fr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
        try { if (File.Exists(SettingsFile)) Fr = File.ReadAllText(SettingsFile).Trim() == "fr"; }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            File.WriteAllText(SettingsFile, Fr ? "fr" : "en");
        }
        catch { }
    }

    public static string T(string fr, string en) { return Fr ? fr : en; }
}

enum Kind { Info, Ok, Warn, Error }

// Operations sur l'exe, sans interface : utilisees par le bouton et par la ligne de commande.
static class Ops
{
    public static Action<string, Kind> Log = (m, k) => { };

    public static readonly string BackupDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Application.ExecutablePath)), "Zeus.exe original");

    public static string SaveWarning
    {
        get
        {
            return L.T("Les sauvegardes faites avec l'exe patché ne pourront plus être ouvertes par l'exe d'origine (le jeu plante). Les sauvegardes existantes s'ouvrent normalement avec l'exe patché.",
                       "Saved games made with the patched exe can no longer be opened by the original exe (the game crashes). Existing saved games open normally with the patched exe.");
        }
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    public enum ExeState { Missing, Unknown, Normal, Patched }

    public static ExeState ExeInfo(string path, out string text)
    {
        text = "";
        if (!File.Exists(path)) return ExeState.Missing;
        byte[] f = File.ReadAllBytes(path);
        try
        {
            uint va; int hdr;
            bool[] g = Limits.Detect(f, out va, out hdr);
            if (g != null)
            {
                text = g[0] && g[1] ? L.T("Limites déjà doublées (walkers, chemins, bâtiments)", "Limits already doubled (walkers, paths, buildings)")
                                    : L.T("Limites déjà en partie doublées (essai)", "Limits partly doubled (test build)");
                return ExeState.Patched;
            }
            Limits.Patch(f, true, true, new StringBuilder());   // en memoire : l'exe est-il patchable ?
            text = L.T("Exe anglais 2.1.4.0 — limites normales", "English exe 2.1.4.0 — normal limits");
            return ExeState.Normal;
        }
        catch (Exception ex)
        {
            text = L.T("Exe non reconnu (Zeus anglais 2.1.4.0 attendu)", "Exe not recognised (English Zeus 2.1.4.0 expected)")
                 + (ex.Message.StartsWith("exe non reconnu") ? "" : " : " + ex.Message);
            return ExeState.Unknown;
        }
    }

    // Copie de l'original dans BackupDir : Zeus.exe, ou Zeus (2).exe... si un autre exe y est deja ;
    // une copie identique existante est reutilisee. Relue apres ecriture.
    static string Backup(string path, byte[] orig)
    {
        Directory.CreateDirectory(BackupDir);
        string name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int n = 1; ; n++)
        {
            string dst = Path.Combine(BackupDir, n == 1 ? name + ext : name + " (" + n + ")" + ext);
            if (File.Exists(dst))
            {
                if (Same(File.ReadAllBytes(dst), orig)) return dst;
                continue;
            }
            File.WriteAllBytes(dst, orig);
            if (!Same(File.ReadAllBytes(dst), orig)) throw new IOException(L.T("copie de l'original illisible : ", "unreadable copy of the original: ") + dst);
            File.WriteAllText(dst + ".txt", L.T("Copie de : ", "Copy of: ") + path + Environment.NewLine + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + Environment.NewLine);
            return dst;
        }
    }

    public static bool PatchExe(string path)
    {
        byte[] orig = File.ReadAllBytes(path);
        byte[] f = Limits.Patch(orig, true, true, new StringBuilder());
        // controle : l'annulation du nouvel exe doit redonner l'original octet pour octet
        if (!Same(Limits.Unpatch(f, new StringBuilder()), orig))
        {
            Log(L.T("Contrôle échoué : le patch ne serait pas réversible. Rien n'a été écrit.",
                    "Check failed: the patch would not be reversible. Nothing was written."), Kind.Error);
            return false;
        }
        string bak = Backup(path, orig);
        File.WriteAllBytes(path, f);
        Log(L.T("Limites doublées : walkers 4000, chemins 2000, bâtiments 8000.", "Limits doubled: walkers 4000, paths 2000, buildings 8000."), Kind.Ok);
        Log(L.T("Exe d'origine gardé ici : ", "Original exe kept here: ") + bak, Kind.Info);
        Log(L.T("Pour revenir en arrière : remettre cette copie à la place de Zeus.exe.", "To go back: put this copy back in place of Zeus.exe."), Kind.Info);
        Log(SaveWarning, Kind.Warn);
        return true;
    }
}

static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [STAThread]
    static int Main(string[] args)
    {
        L.Load();
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
        if (a.Length != 2 || (a[0] != "--info" && a[0] != "--patch")) { Console.WriteLine("\"Zeus Limit Patcher.exe\" [--lang fr|en] --info <Zeus.exe> | --patch <Zeus.exe>"); return 2; }
        bool err = false;
        Ops.Log = (m, k) => { Console.WriteLine((k == Kind.Error ? "ERROR " : k == Kind.Warn ? "note " : "") + m); if (k == Kind.Error) err = true; };
        string p = Path.GetFullPath(a[1]);
        try
        {
            if (a[0] == "--info") { string t; Ops.ExeInfo(p, out t); Console.WriteLine(t); return 0; }
            return Ops.PatchExe(p) && !err ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine("ERROR " + ex.Message); return 1; }
    }
}

class MainForm : Form
{
    static readonly Color Brown = Color.FromArgb(90, 50, 20);
    Label title, intro, lExe, lInfo, lWarn, lLang;
    ComboBox cLang;
    TextBox tExe;
    Button bExe, bPatch;
    RichTextBox log;
    bool busy;          // patch en cours
    int reading;        // n° de la derniere analyse lancee (les resultats d'une analyse depassee sont ignores)
    string shown;       // chemin dont l'etat est affiche
    Ops.ExeState exeState;

    public MainForm()
    {
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 470);
        MinimumSize = new Size(680, 440);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 238, 226);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        AnchorStyles lr = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        title = new Label { Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Brown, AutoSize = true, Location = new Point(16, 12) };
        lLang = new Label { Location = new Point(556, 17), Size = new Size(88, 22), TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        cLang = new ComboBox { Location = new Point(646, 14), Size = new Size(98, 24), DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        cLang.Items.AddRange(new object[] { "Français", "English" });
        cLang.SelectedIndex = L.Fr ? 0 : 1;
        cLang.SelectedIndexChanged += delegate { L.Fr = cLang.SelectedIndex == 0; L.Save(); ApplyLanguage(); ShowInfo(true); };
        intro = new Label { Location = new Point(16, 52), Size = new Size(728, 44), Anchor = lr };
        lExe = new Label { Location = new Point(16, 100), AutoSize = true };
        tExe = new TextBox { Location = new Point(16, 120), Width = 620, Anchor = lr };
        bExe = new Button { Location = new Point(644, 118), Size = new Size(100, 27), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        lInfo = new Label { Location = new Point(16, 150), Size = new Size(728, 22), ForeColor = Brown, Font = new Font(Font, FontStyle.Bold), Anchor = lr };
        lWarn = new Label { Location = new Point(16, 178), Size = new Size(728, 44), ForeColor = Color.FromArgb(170, 100, 0), Anchor = lr };
        bPatch = new Button { Location = new Point(16, 228), Size = new Size(260, 36), Font = new Font(Font, FontStyle.Bold) };
        log = new RichTextBox { Location = new Point(16, 276), Size = new Size(728, 178), ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9f),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

        bExe.Click += delegate
        {
            using (OpenFileDialog d = new OpenFileDialog { Filter = "Zeus.exe|Zeus.exe|*.exe|*.exe" })
                if (d.ShowDialog(this) == DialogResult.OK) { tExe.Text = d.FileName; ShowInfo(false); }
        };
        tExe.Leave += delegate { ShowInfo(false); };
        bPatch.Click += delegate
        {
            if (MessageBox.Show(this, Ops.SaveWarning + Environment.NewLine + Environment.NewLine + L.T("Doubler les limites de cet exe ?", "Double the limits of this exe?"),
                                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                Run();
        };

        Controls.AddRange(new Control[] { title, lLang, cLang, intro, lExe, tExe, bExe, lInfo, lWarn, bPatch, log });
        ApplyLanguage();
        UpdateButtons();
    }

    void ApplyLanguage()
    {
        Text = title.Text = "Zeus Limit Patcher";
        lLang.Text = L.T("Langue :", "Language:");
        intro.Text = L.T("Double les limites de données d'un Zeus.exe anglais 2.1.4.0 (GOG, Steam, widescreen, avec ou sans French Patch) : walkers 2000 → 4000, chemins 1000 → 2000, bâtiments 4000 → 8000.",
                         "Doubles the data limits of an English Zeus.exe 2.1.4.0 (GOG, Steam, widescreen, with or without the French Patch): walkers 2000 → 4000, paths 1000 → 2000, buildings 4000 → 8000.");
        lExe.Text = L.T("Zeus.exe à patcher :", "Zeus.exe to patch:");
        bExe.Text = L.T("Parcourir…", "Browse…");
        lWarn.Text = Ops.SaveWarning + " " + L.T("L'exe d'origine est copié dans : ", "The original exe is copied to: ") + Ops.BackupDir;
        bPatch.Text = L.T("Doubler les limites", "Double the limits");
    }

    void UpdateButtons()
    {
        bExe.Enabled = tExe.Enabled = cLang.Enabled = !busy;
        bPatch.Enabled = !busy && exeState == Ops.ExeState.Normal;
    }

    // Lecture de l'etat de l'exe en arriere-plan (l'analyse prend environ une seconde). Les controles restent
    // actifs pendant l'analyse (sinon le clic sur Parcourir, qui fait quitter la zone de texte, est perdu) ;
    // seul le bouton de patch attend le resultat. force : relire meme si le chemin n'a pas change.
    void ShowInfo(bool force)
    {
        if (busy) return;
        string p = tExe.Text.Trim();
        if (!force && p == shown) return;
        shown = p;
        int id = ++reading;
        exeState = Ops.ExeState.Missing; UpdateButtons();
        lInfo.Text = File.Exists(p) ? L.T("Analyse…", "Reading…") : "";
        Thread t = new Thread(() =>
        {
            string te; Ops.ExeState es;
            try { es = Ops.ExeInfo(p, out te); } catch (Exception ex) { es = Ops.ExeState.Unknown; te = ex.Message; }
            BeginInvoke((Action)(() =>
            {
                if (id != reading) return;
                exeState = es; lInfo.Text = te; UpdateButtons();
            }));
        });
        t.IsBackground = true;
        t.Start();
    }

    void Run()
    {
        if (busy) return;
        string p = tExe.Text.Trim();
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
            try { Ops.PatchExe(p); }
            catch (IOException ex) { Ops.Log(L.T("Fichier inaccessible (le jeu est-il ouvert ?) : ", "File not accessible (is the game running?): ") + ex.Message, Kind.Error); }
            catch (UnauthorizedAccessException ex) { Ops.Log(L.T("Écriture refusée (dossier protégé ou fichier en lecture seule) : ", "Writing denied (protected folder or read-only file): ") + ex.Message, Kind.Error); }
            catch (Exception ex) { Ops.Log(L.T("Erreur : ", "Error: ") + ex.Message, Kind.Error); }
            BeginInvoke((Action)(() => { busy = false; ShowInfo(true); }));
        });
        t.IsBackground = true;
        t.Start();
    }
}
