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
    [Theory]
    [InlineData("Q8_0", 7u)]
    [InlineData("Q6_K", 18u)]
    [InlineData("Q5_K_M", 17u)]
    [InlineData("Q4_K_M", 15u)]
    public void Selected_quantization_matches_metadata_and_rejects_mislabeled_file(string quantization, uint type)
    {
        using var valid = Gguf(type); GgufMetadata.ValidateQuantization(valid, quantization);
        using var invalid = Gguf(type == 7 ? 15u : 7u);
        Assert.Throws<InvalidDataException>(() => GgufMetadata.ValidateQuantization(invalid, quantization));
    }
    private static MemoryStream Gguf(uint type)
    {
        var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Encoding.ASCII.GetBytes("GGUF")); writer.Write(3u); writer.Write(0ul); writer.Write(1ul);
        var key = Encoding.UTF8.GetBytes("general.file_type"); writer.Write((ulong)key.Length); writer.Write(key); writer.Write(4u); writer.Write(type);
        stream.Position = 0; return stream;
    }
}
