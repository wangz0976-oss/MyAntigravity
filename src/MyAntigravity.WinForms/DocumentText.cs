using System.Globalization;
using System.Text;
using DotnetPoi.HWPF.UserModel;
using DotnetPoi.SS.UserModel;
using DotnetPoi.XSSF.UserModel;
using DotnetPoi.XWPF.UserModel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MyAntigravity;

internal static class DocumentText
{
    private const int MaxSourceBytes = 64 * 1024 * 1024;

    internal static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".docx" or ".doc" or ".pdf" or ".xlsx" or ".ppt";

    internal static async Task<PromptTextFile> LoadAsync(string path, int remaining, CancellationToken cancellation)
    {
        int limit = Math.Min(remaining, PromptTextFile.MaxFileBytes);
        if (limit <= 0)
            throw new IOException("文件附件的总大小已达到上限。");

        var executable = Environment.ProcessPath ?? throw new IOException("无法定位 MyAntigravity 的文档读取进程。");
        string[] arguments = ["--read-document-text", Path.GetFullPath(path), limit.ToString(CultureInfo.InvariantCulture)];
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments = [Path.Combine(AppContext.BaseDirectory, "MyAntigravity.dll"), .. arguments];
        var start = Cli.StartInfo(executable, AppContext.BaseDirectory, true, arguments);
        try
        {
            var (output, _) = await Cli.ReadOutputAsync(start, "提取文档文字", cancellation,
                TimeSpan.FromSeconds(20), showError: true).ConfigureAwait(false);
            return new PromptTextFile(Path.GetFileName(path), output);
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new IOException("无法启动 Windows 文档读取进程。", error);
        }
    }

    internal static int RunReader(string path, string requestedLimit)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (!int.TryParse(requestedLimit, NumberStyles.None, CultureInfo.InvariantCulture, out int limit) ||
            limit is <= 0 or > PromptTextFile.MaxFileBytes || !Path.IsPathFullyQualified(path) || !Supports(path))
            return 2;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxSourceBytes)
                throw new IOException("单个文档不能超过 64 MiB。");
            var content = new StringBuilder();
            void AddText(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                if (content.Length > 0)
                    content.AppendLine();
                AppendText(content, value);
                if (content.Length > limit)
                    throw TooLong();
            }
            var extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".docx": ReadWord(stream, AddText); break;
                case ".doc": AddText(new HWPFDocument(stream).getRange().text()); break;
                case ".xlsx": ReadWorkbook(stream, AddText); break;
                case ".ppt": LegacySlideText.Read(stream, AddText); break;
                case ".pdf": ReadPdf(stream, AddText); break;
            }
            string text = content.ToString().Trim();
            if (text.Length == 0)
                throw new IOException(extension == ".pdf" ? "没有提取到可发送的文字。扫描版 PDF 可先用文字识别转成文本。"
                    : "没有提取到可发送的文字。");
            if (Encoding.UTF8.GetByteCount(text) > limit)
                throw TooLong();
            Console.Write(text);
            return 0;
        }
        catch (IOException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine("无法提取这个文档的文字。请核对文件格式、完整性及是否需要密码。");
            return 1;
        }
    }

    private static void ReadWord(Stream stream, Action<string> add)
    {
        using var document = new XWPFDocument(stream);
        add(document.getHeaderText() ?? "");
        foreach (var paragraph in document.getParagraphs())
            add(paragraph.getText());
        foreach (var table in document.getTables())
            foreach (var row in table.getRows())
                add(string.Join("\t", row.getCells().Select(cell =>
                    string.Join(" ", cell.getParagraphs().Select(paragraph => paragraph.getText())))));
        add(document.getFooterText() ?? "");
    }

    private static void ReadWorkbook(Stream stream, Action<string> add)
    {
        using var book = new XSSFWorkbook(stream);
        for (int index = 0; index < book.getNumberOfSheets(); index++)
        {
            var sheet = book.getSheetAt(index);
            int lastRow = sheet.getLastRowNum();
            if (lastRow >= 1_048_576)
                throw new InvalidDataException("工作表行号超出 XLSX 格式的有效范围。");
            bool named = false;
            for (int rowIndex = 0; rowIndex <= lastRow; rowIndex++)
            {
                if (sheet.getRow(rowIndex) is not { } row)
                    continue;
                int lastCell = row.getLastCellNum();
                if (lastCell > 16_384)
                    throw new InvalidDataException("工作表列号超出 XLSX 格式的有效范围。");
                var cells = new List<string>();
                for (int column = 0; column < lastCell; column++)
                {
                    if (row.getCell(column) is not { } cell)
                        continue;
                    var value = CellValue(cell);
                    if (!string.IsNullOrWhiteSpace(value))
                        cells.Add($"{ColumnName(column)}：{value}");
                }
                if (cells.Count == 0)
                    continue;
                if (!named)
                {
                    add($"工作表 {index + 1}");
                    named = true;
                }
                add($"第 {rowIndex + 1} 行\t{string.Join("\t", cells)}");
            }
        }
    }

    private static string ColumnName(int zeroBased)
    {
        string label = "";
        for (int value = zeroBased + 1; value > 0; value = (value - 1) / 26)
            label = (char)('A' + (value - 1) % 26) + label;
        return label;
    }

    private static string CellValue(XSSFCell cell)
    {
        var type = cell.getCellType();
        if (type == CellType.Formula)
            type = cell.getCachedFormulaResultType();
        return type switch
        {
            CellType.String => cell.getStringCellValue(),
            CellType.Numeric => cell.getNumericCellValue().ToString("G", CultureInfo.InvariantCulture),
            CellType.Boolean => cell.getBooleanCellValue() ? "TRUE" : "FALSE",
            CellType.Error => cell.getErrorCellString(),
            _ => ""
        };
    }

    private static void ReadPdf(Stream stream, Action<string> add)
    {
        using var document = PdfDocument.Open(stream);
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(text))
                add($"第 {page.Number} 页\n{text}");
        }
    }

    private static void AppendText(StringBuilder destination, string part)
    {
        foreach (char value in part)
        {
            if (value is '\u2028' or '\u2029' or '\f')
                destination.Append('\n');
            else if (value is '\u0001' or '\u0007')
                destination.Append(' ');
            else if (!char.IsControl(value) || value is '\t' or '\r' or '\n')
                destination.Append(value);
        }
    }

    private static IOException TooLong() =>
        new("文档提取后的文字超过单文件或此条消息的剩余额度。请先拆分文档。");
}
