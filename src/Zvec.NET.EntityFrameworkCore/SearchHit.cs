namespace Zvec.NET.EntityFrameworkCore;

/// <summary>向量检索命中（实体键 + 得分 + 原始 Doc 投影）。</summary>
public readonly record struct SearchHit(string Id, float Score, Doc Doc);

/// <summary>向量检索命中并回查到 EF 实体。</summary>
public readonly record struct SearchHit<TEntity>(TEntity Entity, float Score) where TEntity : class;
