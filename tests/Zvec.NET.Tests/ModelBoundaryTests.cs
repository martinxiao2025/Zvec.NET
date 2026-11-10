using Xunit;
using Zvec.NET;

namespace Zvec.NET.Tests;

/// <summary>公开类型的边界/异常路径用例：SparseVector 与 Schema 定义的参数校验。</summary>
public sealed class ModelBoundaryTests
{
    // ---- SparseVector ----

    [Fact]
    public void SparseVectorRejectsMismatchedLengths()
    {
        // 绑定层契约：indices 与 values 长度必须一致。
        Assert.Throws<ArgumentException>(() => new SparseVector([1u, 2u], [0.5f]));
    }

    [Fact]
    public void SparseVectorFromDictionaryRejectsNegativeKey()
    {
        // FromDictionary 经 checked 转换 uint，负 key 应抛 OverflowException 而非静默回绕。
        Assert.Throws<OverflowException>(
            () => SparseVector.FromDictionary(new Dictionary<long, float> { [-1] = 0.5f }));
    }

    [Fact]
    public void SparseVectorRoundTripsThroughDictionary()
    {
        // 空字典 round-trip 不抛异常。
        Assert.Empty(SparseVector.FromDictionary(new Dictionary<long, float>()).ToDictionary());

        // 非空：键值保持一致，索引按升序排列。
        var source = new Dictionary<long, float> { [5] = 0.5f, [1] = 1.5f };
        SparseVector sparse = SparseVector.FromDictionary(source);
        IReadOnlyDictionary<long, float> round = sparse.ToDictionary();

        Assert.Equal(2, round.Count);
        Assert.Equal(1.5f, round[1]);
        Assert.Equal(0.5f, round[5]);
        Assert.Equal(new uint[] { 1u, 5u }, sparse.Indices);
    }

    // ---- Schema 定义校验 ----

    [Theory]
    [InlineData(DataType.VectorFp32)]
    [InlineData(DataType.SparseVectorFp32)]
    public void FieldSchemaRejectsVectorDataTypes(DataType dataType)
    {
        // 向量类型必须走 VectorSchema，误用 FieldSchema 应被拒绝。
        Assert.Throws<ArgumentException>(() => new FieldSchema("t", dataType));
    }

    [Fact]
    public void VectorSchemaRejectsScalarDataType()
    {
        Assert.Throws<ArgumentException>(() => new VectorSchema("v", DataType.String, dimension: 4));
    }

    [Fact]
    public void SparseVectorSchemaRejectsNonZeroDimension()
    {
        Assert.Throws<ArgumentException>(() => new VectorSchema("v", DataType.SparseVectorFp32, dimension: 4));
    }
}
