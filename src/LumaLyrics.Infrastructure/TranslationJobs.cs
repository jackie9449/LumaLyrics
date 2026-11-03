using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;
public enum TranslationJobMode { FillMissing, ReplaceExpected }
public enum TranslationJobState
{
    Exporting, AwaitingResult, Applied, AppliedPartial, PartialReady,
    Rejected, Stale, Conflict, Cancelled
}
public sealed record TranslationJobGroup(int GroupIndex, string Original, bool NeedsTranslation);
public sealed record TranslationJobTarget(Track Track, string BindingId,
    string SourceHash, string OriginalRaw, string? LibrarySource,
    string? PersistentId, IReadOnlyList<TranslationJobGroup> Groups);
public sealed record TranslationJobRequest(int SchemaVersion, string JobId,
    TranslationJobMode Mode, string ProfileRoot, TranslationJobTarget Target,
    string? ExpectedRevision, DateTimeOffset CreatedUtc, string OriginalSource,
    string? UserInstructions, IReadOnlyList<TranslationLine>? ExistingTranslation);
public sealed record TranslationJobResultLine(int GroupIndex, string Original,
    string? Chinese, string? UntranslatedReason);
public sealed record TranslationJobResult(string Format, int SchemaVersion,
    string JobId, string RequestSha256, string SourceHash, string DraftEngine,
    IReadOnlyList<TranslationJobResultLine> Lines);
public sealed record TranslationJobTicket(TranslationJobRequest Request,
    string RequestSha256, string TaskPath, string ResultPath);
public sealed record TranslationJobSkip(Track Track, string Code, string Message);
public sealed record TranslationBatchSelection(Track Track, TranslationJobMode Mode,
    string? UserInstructions = null, bool IncludeExistingTranslation = false, LibraryPreparationTarget? Target = null);
public sealed record TranslationBatchItem(string JobId, string RequestSha256,
    string TaskPath, string ResultPath, string ReceiptPath);
public sealed record TranslationBatchChunk(string Format, int SchemaVersion,
    string BatchId, int ChunkIndex, IReadOnlyList<TranslationBatchItem> Items);
public sealed record TranslationBatchChunkReference(int ChunkIndex, string Path,
    string Sha256, int Count);
public sealed record TranslationBatchManifest(string Format, int SchemaVersion,
    string BatchId, string ProfileRoot, DateTimeOffset CreatedUtc, int Total,
    int ChunkSize, IReadOnlyList<TranslationBatchChunkReference> Chunks);
public sealed record TranslationBatchTicket(string BatchId, string EntryPath,
    string ManifestPath, string ProgressPath, IReadOnlyList<string> ChunkPaths);
public sealed record TranslationJobBatchResult(TranslationBatchTicket? Batch,
    IReadOnlyList<TranslationJobTicket> Tickets,
    IReadOnlyList<TranslationJobSkip> Skipped);
public sealed record TranslationJobValidation(TranslationJobRequest Request,
    TranslationJobResult Result, string ResultSha256,
    int TranslatedCount, int RequiredCount);
public sealed record TranslationJobReceipt(string JobId, TranslationJobState State,
    string Code, string Message, int TranslatedCount, int RequiredCount,
    string? AppliedRevision, DateTimeOffset UpdatedUtc);
public sealed record TranslationJobSnapshot(TranslationJobTicket Ticket,
    TranslationJobReceipt Receipt);
public sealed record ActiveTranslationRevision(string Revision, StoredTranslation Record);
public sealed record TranslationHistoryEntry(string Revision, StoredTranslation Record,
    string Reason, DateTimeOffset ReplacedUtc, string? JobId);
