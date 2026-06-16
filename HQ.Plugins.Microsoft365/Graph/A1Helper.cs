namespace HQ.Plugins.Microsoft365.Graph;

/// <summary>Pure A1-notation helpers for Excel range math. No I/O — unit tested.</summary>
public static class A1Helper
{
    /// <summary>Zero-based column index → spreadsheet column letters (0→A, 25→Z, 26→AA).</summary>
    public static string ColumnLetter(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0) throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        var n = zeroBasedIndex;
        var letters = "";
        do
        {
            letters = (char)('A' + n % 26) + letters;
            n = n / 26 - 1;
        } while (n >= 0);
        return letters;
    }

    /// <summary>
    /// Given a used-range address (e.g. "Sheet1!A1:C10" or "A1:C10" or "" for empty),
    /// returns the 1-based row at which new rows should be appended.
    /// </summary>
    public static int NextRowFromUsedRange(string usedRangeAddress)
    {
        if (string.IsNullOrWhiteSpace(usedRangeAddress)) return 1;

        var address = usedRangeAddress;
        var bang = address.IndexOf('!');
        if (bang >= 0) address = address[(bang + 1)..];

        // Take the bottom-right cell of the range.
        var lastCell = address.Contains(':') ? address[(address.IndexOf(':') + 1)..] : address;
        var digits = new string(lastCell.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var lastRow) ? lastRow + 1 : 1;
    }

    /// <summary>Builds an A1 range for a block starting at row <paramref name="startRow"/> (1-based).</summary>
    public static string BuildRangeAddress(int startRow, int rows, int cols)
    {
        if (rows < 1) rows = 1;
        if (cols < 1) cols = 1;
        var endRow = startRow + rows - 1;
        var endCol = ColumnLetter(cols - 1);
        return $"A{startRow}:{endCol}{endRow}";
    }
}
