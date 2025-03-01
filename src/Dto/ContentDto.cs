using System;

namespace haworks.Dto
{
   public record ChunkSessionRequest(
        Guid EntityId,
        string FileName,
        int TotalChunks,
        long TotalSize);

    public record StorageInfo(long FileSize)
    {
        public string BucketName { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string ETag { get; set; } = string.Empty;
        public string StorageDetails { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public record ChunkSessionDto(
        Guid SessionId,
        DateTime ExpiresAt,
        int TotalChunks);

    public record ContentDto(
        Guid Id,
        Guid EntityId,
        string EntityType,
        string Url,
        string ContentType,
        long FileSize);



   public record ContentUploadResult(
        string BucketName,
        string ObjectName,
        string ContentType,
        long FileSize,
        string VersionId,
        string StorageDetails,
        string Path
    );



}
