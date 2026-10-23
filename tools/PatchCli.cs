// Essai du patch en ligne de commande :
//   PatchCli.exe <entree.exe> <sortie.exe> [walkers] [batiments] [compagnies]
//   PatchCli.exe annuler <entree.exe> <sortie.exe>
using System;
using System.IO;
using System.Text;

public static class PatchCli
{
    public static int Main(string[] a)
    {
        var log = new StringBuilder();
        try
        {
            if (a[0] == "annuler") File.WriteAllBytes(a[2], Limits.Unpatch(File.ReadAllBytes(a[1]), log));
            else
            {
                bool w = Array.IndexOf(a, "walkers") >= 0, b = Array.IndexOf(a, "batiments") >= 0, k = Array.IndexOf(a, "compagnies") >= 0;
                Limits.NoStack = Array.IndexOf(a, "sanspile") >= 0;
                File.WriteAllBytes(a[1], Limits.Patch(File.ReadAllBytes(a[0]), w, b, k, log));
            }
            Console.Write(log.ToString());
            return 0;
        }
        catch (Exception ex) { Console.Write(log.ToString()); Console.WriteLine("ERREUR : " + ex.Message); return 1; }
    }
}
