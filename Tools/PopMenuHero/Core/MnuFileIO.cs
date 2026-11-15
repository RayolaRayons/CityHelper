using System.Text;

namespace CityHelper.Tools.PopMenuHero.Core;

public static class MnuFileIO
{
    // Strict UTF-8 without BOM: invalid bytes throw rather than being silently replaced
    // (and then written back corrupted), matching Python's open(..., encoding='utf-8').
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static MnuFile Load(string path) =>
        new MnuParser().Parse(File.ReadAllText(path, Utf8));

    /// <summary>
    /// Writes with the platform line ending, as Python's text-mode write did
    /// (CRLF on Windows).
    /// </summary>
    public static void Save(MnuFile mnuFile, string path)
    {
        var text = new MnuWriter().Write(mnuFile).Replace("\n", Environment.NewLine);
        File.WriteAllText(path, text, Utf8);
    }
}
