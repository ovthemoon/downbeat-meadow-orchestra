namespace Downbeat.Core
{
    /// <summary>Windows Notepad and PowerShell's <c>Set-Content -Encoding UTF8</c> put a byte order mark (U+FEFF) in front of a text file; a JSON reader does not accept it, so it is taken off before parsing.</summary>
    public static class TextBom
    {
        const char ByteOrderMark = '\uFEFF';

        public static string Strip(string text) => !string.IsNullOrEmpty(text) && text[0] == ByteOrderMark ? text.Substring(1) : text;
    }
}
