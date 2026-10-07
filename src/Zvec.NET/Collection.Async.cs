using System.Threading.Channels;

// CA1711（类型名不宜以 Collection 结尾）：类名对齐 Python SDK zvec.Collection，属有意保留。
#pragma warning disable CA1711

namespace Zvec.NET;

/// <summary>Collection 的异步入口（Task.Run 薄包装；原生库本身支持并发读写）。</summary>
public sealed partial class Collection
{
    /// <inheritdoc cref="Insert(Doc)"/>
    public Task<WriteResult> InsertAsync(Doc doc, CancellationToken cancellationToken = default) =>
        Task.Run(() => Insert(doc), cancellationToken);

    /// <inheritdoc cref="Insert(IEnumerable{Doc})"/>
    public Task<WriteResult[]> InsertAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default) =>
        Task.Run(() => Insert(docs), cancellationToken);

    /// <inheritdoc cref="Update(Doc)"/>
    public Task<WriteResult> UpdateAsync(Doc doc, CancellationToken cancellationToken = default) =>
        Task.Run(() => Update(doc), cancellationToken);

    /// <inheritdoc cref="Update(IEnumerable{Doc})"/>
    public Task<WriteResult[]> UpdateAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default) =>
        Task.Run(() => Update(docs), cancellationToken);

    /// <inheritdoc cref="Upsert(Doc)"/>
    public Task<WriteResult> UpsertAsync(Doc doc, CancellationToken cancellationToken = default) =>
        Task.Run(() => Upsert(doc), cancellationToken);

    /// <inheritdoc cref="Upsert(IEnumerable{Doc})"/>
    public Task<WriteResult[]> UpsertAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default) =>
        Task.Run(() => Upsert(docs), cancellationToken);

    /// <inheritdoc cref="Delete(string)"/>
    public Task<WriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Task.Run(() => Delete(id), cancellationToken);

    /// <inheritdoc cref="Delete(IEnumerable{string})"/>
    public Task<WriteResult[]> DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default) =>
        Task.Run(() => Delete(ids), cancellationToken);

    /// <inheritdoc cref="DeleteByFilter(string)"/>
    public Task DeleteByFilterAsync(string filter, CancellationToken cancellationToken = default) =>
        Task.Run(() => DeleteByFilter(filter), cancellationToken);

    /// <inheritdoc cref="Query(Query, int, string?, bool, IReadOnlyList{string}?, IReRanker?)"/>
    public Task<IReadOnlyList<Doc>> QueryAsync(Query? query = null, int topk = 10, string? filter = null,
        bool includeVector = false, IReadOnlyList<string>? outputFields = null, IReRanker? reranker = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Query(query, topk, filter, includeVector, outputFields, reranker), cancellationToken);

    /// <inheritdoc cref="Query(IReadOnlyList{Query}, int, string?, bool, IReadOnlyList{string}?, IReRanker?)"/>
    public Task<IReadOnlyList<Doc>> QueryAsync(IReadOnlyList<Query> queries, int topk = 10, string? filter = null,
        bool includeVector = false, IReadOnlyList<string>? outputFields = null, IReRanker? reranker = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Query(queries, topk, filter, includeVector, outputFields, reranker), cancellationToken);

    /// <inheritdoc cref="Fetch(string, IReadOnlyList{string}?, bool)"/>
    public Task<Dictionary<string, Doc>> FetchAsync(string id, IReadOnlyList<string>? outputFields = null,
        bool includeVector = true, CancellationToken cancellationToken = default) =>
        Task.Run(() => Fetch(id, outputFields, includeVector), cancellationToken);

    /// <inheritdoc cref="Fetch(IEnumerable{string}, IReadOnlyList{string}?, bool)"/>
    public Task<Dictionary<string, Doc>> FetchAsync(IEnumerable<string> ids, IReadOnlyList<string>? outputFields = null,
        bool includeVector = true, CancellationToken cancellationToken = default) =>
        Task.Run(() => Fetch(ids, outputFields, includeVector), cancellationToken);

    /// <summary>
    /// 异步全量迭代：后台线程执行原生迭代，经有界 Channel 逐文档流式产出（满时对生产端反压）。
    /// 消费端提前放弃枚举（break/Dispose）时经 linked CTS 唤醒阻塞中的生产者并回收任务。
    /// </summary>
    /// <param name="outputFields">仅取回的标量字段列表；null = 全部。</param>
    /// <param name="includeVector">是否取回向量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async IAsyncEnumerable<Doc> IterateDocsAsync(IReadOnlyList<string>? outputFields = null,
        bool includeVector = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<Doc> channel = System.Threading.Channels.Channel.CreateBounded<Doc>(
            new System.Threading.Channels.BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken producerToken = linkedCts.Token;

        // 启动任务本身不绑定外部 token：取消与提前放弃统一由 producerToken 管理，
        // 避免任务未启动时无人完成 Channel 而悬挂读端。
        Task producer = Task.Run(async () =>
        {
            try
            {
                foreach (Doc doc in IterateDocs(outputFields, includeVector))
                {
                    producerToken.ThrowIfCancellationRequested();
                    await channel.Writer.WriteAsync(doc, producerToken).ConfigureAwait(false);
                }

                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                // 消费端已离开（取消或提前放弃），正常收尾。
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (Doc doc in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return doc;
            }
        }
        finally
        {
            // 迭代器被提前释放（break）时在此唤醒生产者并等待其退出，防止任务泄漏。
            await linkedCts.CancelAsync().ConfigureAwait(false);
            await producer.ConfigureAwait(false);
        }
    }

    /// <inheritdoc cref="GroupByQuery(Query, string, int, int, string?, bool, IReadOnlyList{string}?)"/>
    public Task<IReadOnlyList<GroupResult>> GroupByQueryAsync(Query query, string groupByFieldName, int groupCount = 2,
        int topkPerGroup = 3, string? filter = null, bool includeVector = false,
        IReadOnlyList<string>? outputFields = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => GroupByQuery(query, groupByFieldName, groupCount, topkPerGroup, filter, includeVector, outputFields),
            cancellationToken);

    /// <inheritdoc cref="CreateIndex(string, IndexParam)"/>
    public Task CreateIndexAsync(string fieldName, IndexParam indexParam, CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateIndex(fieldName, indexParam), cancellationToken);

    /// <inheritdoc cref="DropIndex(string)"/>
    public Task DropIndexAsync(string fieldName, CancellationToken cancellationToken = default) =>
        Task.Run(() => DropIndex(fieldName), cancellationToken);

    /// <inheritdoc cref="Optimize"/>
    public Task OptimizeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Optimize, cancellationToken);

    /// <inheritdoc cref="AddColumn(FieldSchema, string)"/>
    public Task AddColumnAsync(FieldSchema fieldSchema, string expression = "", CancellationToken cancellationToken = default) =>
        Task.Run(() => AddColumn(fieldSchema, expression), cancellationToken);

    /// <inheritdoc cref="DropColumn(string)"/>
    public Task DropColumnAsync(string fieldName, CancellationToken cancellationToken = default) =>
        Task.Run(() => DropColumn(fieldName), cancellationToken);

    /// <inheritdoc cref="AlterColumn(string, string?, FieldSchema?)"/>
    public Task AlterColumnAsync(string oldName, string? newName = null, FieldSchema? fieldSchema = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => AlterColumn(oldName, newName, fieldSchema), cancellationToken);

    /// <inheritdoc cref="Flush"/>
    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Flush, cancellationToken);
}

#pragma warning restore CA1711
