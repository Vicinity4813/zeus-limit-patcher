// Zeus Save Check : affiche, pour chaque sauvegarde .sav (et chaque partie d'une sauvegarde d'aventure), les
// walkers, batiments et chemins en service et leur limite, et si la sauvegarde vient d'un exe normal
// (2000 / 4000 / 1000) ou patche par Zeus Limit Patcher (4000 / 8000 / 2000), et les compagnies (100, 200 depuis la 1.1).
// En service : walker = octet 14 non nul (entree 28) ; batiment = octet 0 non nul (entree 35) ; chemin =
// proprietaire non nul (entree 29, n° du walker qui suit le chemin ; recoupe avec le champ +58 des walkers) ;
// compagnie = case non vide de l'entree 31.
// Interface en francais si Windows est en francais, en anglais sinon (--lang fr|en pour forcer).
// En ligne de commande : "Zeus Save Check.exe" <fichier.sav | dossier> ... (fichiers lâchés sur l'icône : la fenêtre)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ZeusPak;

[assembly: System.Reflection.AssemblyTitle("Zeus Save Check")]
[assembly: System.Reflection.AssemblyDescription("Shows the walkers, buildings and paths in use in a Zeus saved game")]
[assembly: System.Reflection.AssemblyCompany("Outils Zeus (projet amateur)")]
[assembly: System.Reflection.AssemblyProduct("Zeus Limit Patcher")]
[assembly: System.Reflection.AssemblyCopyright("© 2026 Vicinity4813 - MIT License")]
[assembly: System.Resources.NeutralResourcesLanguage("en")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0")]

// Langue de l'interface. Le choix est memorise dans %LOCALAPPDATA%\Zeus Save Check\langue.txt ;
// par defaut, la langue de Windows (francais si Windows est en francais, anglais sinon).
static class L
{
    public static bool Fr;

    static string SettingsFile
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus Save Check", "langue.txt"); }
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

// Resultat pour une partie (ou une erreur de lecture du fichier : Error non nul, Part = 0).
class PartInfo
{
    public string Path, Error;
    public int Part, Parts;
    public bool? Patched;                  // null : nombres d'elements inconnus ou melanges
    public int Walkers, WalkerMax, Buildings, BuildingMax, Paths, PathMax, Companies, CompanyMax;
}

static class Check
{
    public static List<string> Expand(IEnumerable<string> args)
    {
        var r = new List<string>();
        foreach (string a in args)
        {
            if (Directory.Exists(a)) { string[] f = Directory.GetFiles(a, "*.sav", SearchOption.AllDirectories); Array.Sort(f, StringComparer.OrdinalIgnoreCase); r.AddRange(f); }
            else r.Add(a);
        }
        return r;
    }

    public static List<PartInfo> Read(string path)
    {
        var r = new List<PartInfo>();
        try
        {
            SavLimitsFile s = SavLimitsFile.Load(File.ReadAllBytes(path));
            for (int pi = 0; pi < s.Parts.Count; pi++)
            {
                SavPart p = s.Parts[pi];
                Block w = p.Get(28), owners = p.Get(29), bat = p.Get(35), comp = p.Get(31);
                if (w == null || owners == null || bat == null || comp == null || w.ElemSize != 388 || owners.ElemSize != 2 || bat.ElemSize != 280 || comp.ElemSize != 156)
                    throw new InvalidDataException(L.T("format inattendu (pas une sauvegarde de Zeus 2.1.4.0 ?)", "unexpected format (not a Zeus 2.1.4.0 saved game?)"));
                PartInfo x = new PartInfo { Path = path, Part = pi + 1, Parts = s.Parts.Count };
                int[] a = SavLimits.Alive(p);
                x.Walkers = a[0]; x.Buildings = a[1];
                for (int i = 0; i < owners.Count; i++) if (BitConverter.ToUInt16(owners.Data, i * 2) != 0) x.Paths++;
                for (int i = 0; i < comp.Count; i++) if (SavLimits.CompanyUsed(comp, i)) x.Companies++;
                x.WalkerMax = w.Count; x.BuildingMax = bat.Count; x.PathMax = owners.Count; x.CompanyMax = comp.Count;
                if (w.Count == 2000 && bat.Count == 4000 && owners.Count == 1000) x.Patched = false;
                else if (w.Count == 4000 && bat.Count == 8000 && owners.Count == 2000) x.Patched = true;
                r.Add(x);
            }
        }
        catch (Exception ex)
        {
            r.Clear();
            string m = ex is IOException || ex is UnauthorizedAccessException ? ex.Message
                     : L.T("illisible : ", "unreadable: ") + ex.Message;
            r.Add(new PartInfo { Path = path, Error = m });
        }
        return r;
    }

    public static string ExeText(PartInfo x)
    {
        if (x.Error != null) return L.T("erreur", "error");
        if (x.Patched == true) return x.CompanyMax == 200 ? L.T("exe patché 1.1", "patched exe 1.1") : L.T("exe patché 1.0", "patched exe 1.0");
        return x.Patched == false && x.CompanyMax == 100 ? L.T("exe normal", "normal exe") : L.T("inconnu", "unknown");
    }

    public static string PartText(PartInfo x) { return x.Parts > 1 ? x.Part + "/" + x.Parts : ""; }

    public static string Count(int n, int max) { return n + " / " + max; }
}

static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [STAThread]
    static int Main(string[] args)
    {
        L.Load();
        int li = Array.IndexOf(args, "--lang");
        if (li >= 0 && li + 1 < args.Length) { L.Fr = args[li + 1] == "fr"; var r = new List<string>(args); r.RemoveRange(li, 2); args = r.ToArray(); }
        // sans console (fichiers lâchés sur l'icône de l'exe) : la fenêtre, avec ces fichiers
        if (args.Length > 0 && AttachConsole(-1)) return Cli(args);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        MainForm f = new MainForm();
        if (args.Length > 0) f.Shown += delegate { f.Add(args); };
        Application.Run(f);
        return 0;
    }

    static int Cli(string[] a)
    {
        int err = 0;
        Console.WriteLine();
        foreach (string path in Check.Expand(a))
            foreach (PartInfo x in Check.Read(path))
            {
                string name = Path.GetFileName(path) + (x.Parts > 1 ? " [" + Check.PartText(x) + "]" : "");
                if (x.Error != null) { Console.WriteLine("{0} : {1}", name, x.Error); err++; continue; }
                Console.WriteLine("{0,-40} {1,-16} {2} {3,11}  {4} {5,11}  {6} {7,11}  {8} {9,9}", name, Check.ExeText(x),
                    L.T("walkers", "walkers"), Check.Count(x.Walkers, x.WalkerMax), L.T("bâtiments", "buildings"), Check.Count(x.Buildings, x.BuildingMax),
                    L.T("chemins", "paths"), Check.Count(x.Paths, x.PathMax), L.T("compagnies", "companies"), Check.Count(x.Companies, x.CompanyMax));
            }
        return err == 0 ? 0 : 1;
    }
}

class MainForm : Form
{
    static readonly Color Brown = Color.FromArgb(90, 50, 20);
    static readonly Color Orange = Color.FromArgb(170, 100, 0), Red = Color.FromArgb(180, 20, 20);
    Label title, intro, lLang;
    ComboBox cLang;
    Button bOpen, bClear;
    ListView list;
    readonly List<PartInfo> rows = new List<PartInfo>();
    int reading;        // lectures en cours

    public MainForm()
    {
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(940, 440);
        MinimumSize = new Size(680, 360);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 238, 226);
        AllowDrop = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        AnchorStyles lr = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        title = new Label { Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Brown, AutoSize = true, Location = new Point(16, 12) };
        lLang = new Label { Location = new Point(736, 17), Size = new Size(88, 22), TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        cLang = new ComboBox { Location = new Point(826, 14), Size = new Size(98, 24), DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        cLang.Items.AddRange(new object[] { "Français", "English" });
        cLang.SelectedIndex = L.Fr ? 0 : 1;
        cLang.SelectedIndexChanged += delegate { L.Fr = cLang.SelectedIndex == 0; L.Save(); ApplyLanguage(); };
        intro = new Label { Location = new Point(16, 52), Size = new Size(908, 44), Anchor = lr };
        bOpen = new Button { Location = new Point(16, 100), Size = new Size(200, 30), Font = new Font(Font, FontStyle.Bold) };
        bClear = new Button { Location = new Point(224, 100), Size = new Size(110, 30) };
        list = new ListView { Location = new Point(16, 140), Size = new Size(908, 284), View = View.Details, FullRowSelect = true, GridLines = true,
            BackColor = Color.White, AllowDrop = true, UseCompatibleStateImageBehavior = false, ShowItemToolTips = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        for (int i = 0; i < 7; i++) list.Columns.Add("");
        list.Columns[0].Width = 250; list.Columns[1].Width = 50; list.Columns[2].Width = 115;
        list.Columns[3].Width = 115; list.Columns[4].Width = 115; list.Columns[5].Width = 115; list.Columns[6].Width = 105;
        for (int i = 3; i < 7; i++) list.Columns[i].TextAlign = HorizontalAlignment.Right;

        bOpen.Click += delegate
        {
            using (OpenFileDialog d = new OpenFileDialog { Filter = L.T("Sauvegardes Zeus", "Zeus saved games") + " (*.sav)|*.sav|*.*|*.*", Multiselect = true })
                if (d.ShowDialog(this) == DialogResult.OK) Add(d.FileNames);
        };
        bClear.Click += delegate { rows.Clear(); Fill(); };
        DragEventHandler enter = (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
        DragEventHandler drop = (s, e) => { string[] f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) Add(f); };
        DragEnter += enter; DragDrop += drop;
        list.DragEnter += enter; list.DragDrop += drop;

        Controls.AddRange(new Control[] { title, lLang, cLang, intro, bOpen, bClear, list });
        ApplyLanguage();
    }

    void ApplyLanguage()
    {
        Text = title.Text = "Zeus Save Check";
        lLang.Text = L.T("Langue :", "Language:");
        intro.Text = L.T("Glissez ici une ou plusieurs sauvegardes .sav (ou un dossier) : walkers, bâtiments, chemins et compagnies en service sur la limite. Exe normal : 2000 / 4000 / 1000 / 100 ; exe patché par Zeus Limit Patcher : 4000 / 8000 / 2000, compagnies 200 depuis la 1.1.",
                         "Drop one or more .sav saved games (or a folder) here: walkers, buildings, paths and companies in use out of the limit. Normal exe: 2000 / 4000 / 1000 / 100; exe patched by Zeus Limit Patcher: 4000 / 8000 / 2000, companies 200 since 1.1.");
        bOpen.Text = L.T("Choisir des sauvegardes…", "Choose saved games…");
        bClear.Text = L.T("Vider la liste", "Clear the list");
        list.Columns[0].Text = L.T("Sauvegarde", "Saved game");
        list.Columns[1].Text = L.T("Partie", "Part");
        list.Columns[2].Text = L.T("Écrite par", "Written by");
        list.Columns[3].Text = "Walkers";
        list.Columns[4].Text = L.T("Bâtiments", "Buildings");
        list.Columns[5].Text = L.T("Chemins", "Paths");
        list.Columns[6].Text = L.T("Compagnies", "Companies");
        Fill();
    }

    // Lecture en arriere-plan (decompression : environ un dixieme de seconde par fichier) ; une sauvegarde
    // deja dans la liste est relue et remplacee.
    public void Add(string[] paths)
    {
        reading++; UseWaitCursor = true;
        Thread t = new Thread(() =>
        {
            var got = new List<PartInfo>();
            foreach (string p in Check.Expand(paths)) got.AddRange(Check.Read(Path.GetFullPath(p)));
            BeginInvoke((Action)(() =>
            {
                var again = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PartInfo x in got) again.Add(x.Path);
                rows.RemoveAll(x => again.Contains(x.Path));
                rows.AddRange(got);
                if (--reading == 0) UseWaitCursor = false;
                Fill();
            }));
        });
        t.IsBackground = true;
        t.Start();
    }

    void Fill()
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (PartInfo x in rows)
        {
            ListViewItem it = new ListViewItem(Path.GetFileName(x.Path)) { UseItemStyleForSubItems = false, ToolTipText = x.Path };
            it.SubItems.Add(Check.PartText(x));
            if (x.Error != null)
            {
                it.SubItems.Add(x.Error).ForeColor = Red;
                it.ToolTipText = x.Path + Environment.NewLine + x.Error;
                list.Items.Add(it);
                continue;
            }
            it.SubItems.Add(Check.ExeText(x)).ForeColor = x.Patched == true ? Color.FromArgb(20, 120, 40) : Color.Black;
            Cell(it, x.Walkers, x.WalkerMax); Cell(it, x.Buildings, x.BuildingMax); Cell(it, x.Paths, x.PathMax); Cell(it, x.Companies, x.CompanyMax);
            list.Items.Add(it);
        }
        list.EndUpdate();
    }

    // En orange a partir de 90 % de la limite.
    static void Cell(ListViewItem it, int n, int max)
    {
        it.SubItems.Add(Check.Count(n, max)).ForeColor = n * 10 >= max * 9 ? Orange : Color.Black;
    }
}
