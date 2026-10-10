using System.Text;
using PinyinGloss.Providers;
namespace PinyinGloss.Tests;
public class GgufTests
{
    [Theory]
    [InlineData(2u)]
    [InlineData(1u)]
    public void Lmt_refuses_non_q8_weight_metadata(uint type)
    {
        using var stream = Gguf(type);
        Assert.Throws<InvalidDataException>(() => GgufMetadata.ValidateQ8(stream));
    }
    [Fact]
    public void Q8_gguf_is_accepted()
    { using var stream = Gguf(7); GgufMetadata.ValidateQ8(stream); }
    private static MemoryStream Gguf(uint type)
    {
        var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Encoding.ASCII.GetBytes("GGUF")); writer.Write(3u); writer.Write(0ul); writer.Write(1ul);
        var key = Encoding.UTF8.GetBytes("general.file_type"); writer.Write((ulong)key.Length); writer.Write(key); writer.Write(4u); writer.Write(type);
        stream.Position = 0; return stream;
    }
}
