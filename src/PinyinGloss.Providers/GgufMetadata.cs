using System.Text;
namespace PinyinGloss.Providers;
public static class GgufMetadata
{
    public static void ValidateQ8(Stream stream)
        => ValidateQuantization(stream, "Q8_0");
    public static void ValidateQuantization(Stream stream, string quantization)
    {
        var expected = quantization switch { "Q8_0" => 7u, "Q6_K" => 18u, "Q5_K_M" => 17u, "Q4_K_M" => 15u,
            _ => throw new InvalidDataException("不支持的 LMT 量化选择") };
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "GGUF") throw new InvalidDataException("不是 GGUF 文件");
            var version = reader.ReadUInt32(); if (version is not (2 or 3)) throw new InvalidDataException("不支持的 GGUF 版本");
            _ = reader.ReadUInt64(); var count = reader.ReadUInt64(); if (count > 10000) throw new InvalidDataException("GGUF metadata 过大");
            for (ulong i = 0; i < count; i++)
            {
                var key = ReadString(reader); var type = reader.ReadUInt32();
                if (key == "general.file_type")
                {
                    if (type != 4 || reader.ReadUInt32() != expected) throw new InvalidDataException($"GGUF 量化与所选 {quantization} 不符，不自动换精度");
                    return;
                }
                Skip(reader, type, 0);
                if (stream.Position > 64 * 1024 * 1024) throw new InvalidDataException("GGUF metadata 超过 64 MiB");
            }
            throw new InvalidDataException("GGUF 缺少量化类型，不能确认所选精度");
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("GGUF header 损坏", ex); }
    }
    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64(); if (length > 1024 * 1024) throw new InvalidDataException("GGUF 字符串过长");
        var bytes = reader.ReadBytes((int)length); if (bytes.Length != (int)length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes);
    }
    private static void Skip(BinaryReader reader, uint type, int depth)
    {
        if (depth > 2) throw new InvalidDataException("GGUF array nesting invalid");
        if (type == 8) { _ = ReadString(reader); return; }
        if (type == 9)
        {
            var itemType = reader.ReadUInt32(); var count = reader.ReadUInt64(); if (count > 1_000_000) throw new InvalidDataException("GGUF array too large");
            for (ulong i = 0; i < count; i++) Skip(reader, itemType, depth + 1); return;
        }
        var size = type switch { 0 or 1 or 7 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => throw new InvalidDataException("Unknown GGUF metadata type") };
        if (reader.ReadBytes(size).Length != size) throw new EndOfStreamException();
    }
}
