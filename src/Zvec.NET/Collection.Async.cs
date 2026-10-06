using System.Threading.Channels;

namespace Zvec.NET;

/// <summary>Collection 的异步入口（Task.Run 薄包装；原生库本身支持并发读写）。</summary>
public sealed partial class Collection
{
    public Task<WriteResult> InsertAsync(Doc doc, CancellationToken cancellationToken = default) =>
        Task.Run(() => Insert(doc), cancellationToken);

    public Task<WriteResult[]> InsertAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default) =>
        Task.Run(() => Insert(docs), cancellationToken);

    public Task<WriteResult> UpdateAsync(Doc doc, CancellationToken cancellationToken = default) =>
        Task.Run(() => Update(doc), cancellationToken);

    public Task<WriteResult[]> UpdateAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default) =>
        Task.Run(() => Update(docs), cancellationToken);

    public Task<WriteResult> UpsertAsync(Doc doc, CancellationToken cancellationToken = default) =>
        Task.Run(() => Upsert(doc), cancellationToken);

    public Task<WriteResult[]> UpsertAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default) =>
        Task.Run(() => Upsert(docs), cancellationToken);

    public Task<WriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Task.Run(() => Delete(id), cancellationToken);

    public Task<WriteResult[]> DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default) =>
        Task.Run(() => Delete(ids), cancellationToken);

    public Task DeleteByFilterAsync(string filter, CancellationToken cancellationToken = default) =>
        Task.Run(() => DeleteByFilter(filter), cancellationToken);

    public Task<IReadOnlyList<Doc>> QueryAsync(Query? query = null, int topk = 10, string? filter = null,
        bool includeVector = false, IReadOnlyList<string>? outputFields = null, IReRanker? reranker = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Query(query, topk, filter, includeVector, outputFields, reranker), cancellationToken);

    public Task<IReadOnlyList<Doc>> QueryAsync(IReadOnlyList<Query> queries, int topk = 10, string? filter = null,
        bool includeVector = false, IReadOnlyList<string>? outputFields = null, IReRanker? reranker = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Query(queries, topk, filter, includeVector, outputFields, reranker), cancellationToken);

    public Task<Dictionary<string, Doc>> FetchAsync(string id, IReadOnlyList<string>? outputFields = null,
        bool includeVector = true, CancellationToken cancellationToken = default) =>
        Task.Run(() => Fetch(id, outputFields, includeVector), cancellationToken);

    public Task<Dictionary<string, Doc>> FetchAsync(IEnumerable<string> ids, IReadOnlyList<string>? outputFields = null,
        bool includeVector = true, CancellationToken cancellationToken = default) =>
        Task.Run(() => Fetch(ids, outputFields, includeVector), cancellationToken);

    /// <summary>异步全量迭代：后台线程执行原生迭代，经有界 Channel 逐文档流式产出（满时对生产端反压）。</summary>
    public async IAsyncEnumerable<Doc> IterateDocsAsync(IReadOnlyList<string>? outputFields = null,
        bool includeVector = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<Doc> channel = System.Threading.Channels.Channel.CreateBounded<Doc>(
            new System.Threading.Channels.BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });

        Task producer = Task.Run(async () =>
        {
            try
            {
                foreach (Doc doc in IterateDocs(outputFields, includeVector))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await channel.Writer.WriteAsync(doc, cancellationToken).ConfigureAwait(false);
                }

                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, cancellationToken);

        await foreach (Doc doc in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return doc;
        }

        await producer.ConfigureAwait(false);
    }

    public Task<IReadOnlyList<GroupResult>> GroupByQueryAsync(Query query, string groupByFieldName, int groupCount = 2,
        int topkPerGroup = 3, string? filter = null, bool includeVector = false,
        IReadOnlyList<string>? outputFields = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => GroupByQuery(query, groupByFieldName, groupCount, topkPerGroup, filter, includeVector, outputFields),
            cancellationToken);

    public Task CreateIndexAsync(string fieldName, IndexParam indexParam, CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateIndex(fieldName, indexParam), cancellationToken);

    public Task DropIndexAsync(string fieldName, CancellationToken cancellationToken = default) =>
        Task.Run(() => DropIndex(fieldName), cancellationToken);

    public Task OptimizeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Optimize, cancellationToken);

    public Task AddColumnAsync(FieldSchema fieldSchema, string expression = "", CancellationToken cancellationToken = default) =>
        Task.Run(() => AddColumn(fieldSchema, expression), cancellationToken);

    public Task DropColumnAsync(string fieldName, CancellationToken cancellationToken = default) =>
        Task.Run(() => DropColumn(fieldName), cancellationToken);

    public Task AlterColumnAsync(string oldName, string? newName = null, FieldSchema? fieldSchema = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => AlterColumn(oldName, newName, fieldSchema), cancellationToken);

    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Flush, cancellationToken);
}
