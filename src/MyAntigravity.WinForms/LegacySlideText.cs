using System.Buffers.Binary;
using System.Text;
using DotnetPoi.HSLF.Record;
using DotnetPoi.HSLF.UserModel;

namespace MyAntigravity;

internal static class LegacySlideText
{
    private const ushort Document = 1000;
    private const ushort Slide = 1006;
    private const ushort SlidePersist = 1011;
    private const ushort Drawing = 1036;
    private const ushort TextChars = 4000;
    private const ushort TextBytes = 4008;
    private const ushort SlideListWithText = 4080;
    private const ushort UserEdit = 4085;
    private const ushort PersistDirectory = 6002;
    private const ushort ClientTextbox = 0xF00D;

    internal static void Read(Stream stream, Action<string> add)
    {
        using var deck = new HSLFSlideShow(stream);
        var records = deck.getTopLevelRecords().ToDictionary(record => (uint)record.Offset);
        var currentUser = deck.getStreamBytes("Current User");
        if (currentUser is not { Length: >= 20 } || UInt(currentUser, 12) == 0xF3D1C4DF)
            throw new IOException("无法读取此加密或损坏的 PPT。");

        // MS-PPT: walk the edit chain newest first so deleted or superseded slides are not included.
        uint editOffset = UInt(currentUser, 16);
        uint? documentId = null;
        var offsets = new Dictionary<uint, uint>();
        while (editOffset != 0)
        {
            if (!records.TryGetValue(editOffset, out var edit) || edit.RecType != UserEdit || edit.Body.Length < 20)
                throw InvalidPpt();
            documentId ??= UInt(edit.Body, 16);
            if (!records.TryGetValue(UInt(edit.Body, 12), out var directory) || directory.RecType != PersistDirectory)
                throw InvalidPpt();
            ReadOffsets(directory.Body, offsets);
            uint earlierEdit = UInt(edit.Body, 8);
            if (earlierEdit >= editOffset)
                throw InvalidPpt();
            editOffset = earlierEdit;
        }

        if (documentId is null || !offsets.TryGetValue(documentId.Value, out var documentOffset) ||
            !records.TryGetValue(documentOffset, out var document) || document.RecType != Document)
            throw InvalidPpt();
        var slides = document.Children?.FirstOrDefault(record =>
            record.RecType == SlideListWithText && record.VerAndInstance >> 4 == 0)?.Children;
        if (slides is null)
            return;

        var text = new List<string>();
        int number = 0;
        foreach (var record in slides)
        {
            if (record.RecType == SlidePersist)
            {
                Flush();
                number++;
                if (record.Body.Length < 4 || !offsets.TryGetValue(UInt(record.Body, 0), out var slideOffset) ||
                    !records.TryGetValue(slideOffset, out var slide) || slide.RecType != Slide)
                    throw InvalidPpt();
                foreach (var drawing in slide.Children ?? [])
                    if (drawing.RecType == Drawing)
                        ReadDrawing(drawing.Body, text, 0, insideTextbox: false);
            }
            else if (number != 0)
                ReadText(record.RecType, record.Body, text);
        }
        Flush();

        void Flush()
        {
            if (number > 0 && text.Count > 0)
                add($"第 {number} 张幻灯片\n" + string.Join("\n", text));
            text.Clear();
        }
    }

    private static void ReadOffsets(ReadOnlySpan<byte> body, Dictionary<uint, uint> offsets)
    {
        while (!body.IsEmpty)
        {
            if (body.Length < 4)
                throw InvalidPpt();
            uint descriptor = UInt(body, 0);
            uint firstId = descriptor & 0x000F_FFFF;
            int count = (int)(descriptor >> 20);
            body = body[4..];
            if (count == 0 || count > body.Length / 4)
                throw InvalidPpt();
            for (int index = 0; index < count; index++)
                offsets.TryAdd(firstId + (uint)index, UInt(body, index * 4));
            body = body[(count * 4)..];
        }
    }

    private static void ReadDrawing(ReadOnlySpan<byte> data, List<string> text, int depth, bool insideTextbox)
    {
        // OfficeArt client textboxes contain PPT text atoms, while other drawing records can contain artwork data.
        if (depth >= 32)
            throw InvalidPpt();
        while (!data.IsEmpty)
        {
            if (data.Length < 8)
                throw InvalidPpt();
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            uint size = UInt(data, 4);
            if (size > data.Length - 8)
                throw InvalidPpt();
            var payload = data.Slice(8, (int)size);
            if (insideTextbox)
                ReadText(type, payload, text);
            else if (type == ClientTextbox || (version & 15) == 15)
                ReadDrawing(payload, text, depth + 1, insideTextbox: type == ClientTextbox);
            data = data[(8 + (int)size)..];
        }
    }

    private static void ReadText(ushort type, ReadOnlySpan<byte> body, List<string> text)
    {
        var value = type switch
        {
            TextChars when body.Length % 2 == 0 => Encoding.Unicode.GetString(body),
            TextBytes => Encoding.Latin1.GetString(body),
            _ => ""
        };
        value = value.Trim('\0', '\r', '\n', ' ');
        if (value.Length != 0)
            text.Add(value);
    }

    private static uint UInt(ReadOnlySpan<byte> value, int start) => BinaryPrimitives.ReadUInt32LittleEndian(value[start..]);

    private static IOException InvalidPpt() => new("无法读取 PPT 的当前幻灯片记录。请检查文件是否损坏。");
}
