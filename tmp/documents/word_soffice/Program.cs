using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string format = null;
        string outDir = null;
        string inputPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--convert-to" && i + 1 < args.Length)
            {
                format = args[++i];
                continue;
            }

            if (args[i] == "--outdir" && i + 1 < args.Length)
            {
                outDir = args[++i];
                continue;
            }

            if (!args[i].StartsWith("-", StringComparison.Ordinal))
            {
                inputPath = args[i];
            }
        }

        if (!string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(outDir) || string.IsNullOrWhiteSpace(inputPath))
        {
            Console.Error.WriteLine("This compatibility wrapper supports DOCX-to-PDF conversion only.");
            return 2;
        }

        inputPath = Path.GetFullPath(inputPath);
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");

        object wordObject = null;
        object documentObject = null;
        try
        {
            Type wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null)
            {
                Console.Error.WriteLine("Microsoft Word is not installed.");
                return 3;
            }

            wordObject = Activator.CreateInstance(wordType);
            dynamic word = wordObject;
            word.Visible = false;
            word.DisplayAlerts = 0;
            documentObject = word.Documents.Open(inputPath, false, true);
            dynamic document = documentObject;
            document.ExportAsFixedFormat(outputPath, 17);
            Console.WriteLine("Converted " + inputPath + " -> " + outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 4;
        }
        finally
        {
            if (documentObject != null)
            {
                try { ((dynamic)documentObject).Close(0); } catch { }
                try { Marshal.FinalReleaseComObject(documentObject); } catch { }
            }
            if (wordObject != null)
            {
                try { ((dynamic)wordObject).Quit(); } catch { }
                try { Marshal.FinalReleaseComObject(wordObject); } catch { }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
