using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Cleaning;

/// <summary>Windows Recycle Bin, queried and emptied through the Shell API.</summary>
[SupportedOSPlatform("windows")]
public sealed class RecycleBinModule : ICleanupModule
{
    public string Name => CleanCategory.Trash.DisplayName();
    public CleanCategory Category => CleanCategory.Trash;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI   = 0x2;
    private const uint SHERB_NOSOUND        = 0x4;

    public CategoryResult Scan(ScanContext ctx)
    {
        var result = new CategoryResult { Category = Category };
        var (size, count) = Query();
        if (size > 0)
        {
            result.Items.Add(new CleanItem
            {
                Path = Sentinels.RecycleBin,
                DisplayName = $"Recycle Bin — {count} item(s)",
                SizeBytes = size,
                Category = Category,
                IsDirectory = true,
                DefaultSelected = true
            });
        }
        return result;
    }

    private static (long size, long count) Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        try { return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0); }
        catch { return (0, 0); }
    }

    /// <summary>Empties the recycle bin; returns the bytes it held.</summary>
    public static long Empty()
    {
        var (size, _) = Query();
        try { SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND); }
        catch { }
        return size;
    }
}
