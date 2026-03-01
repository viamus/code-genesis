using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CodeGenesis.Engine.Pipeline;

public sealed class CheckpointManager(ILogger<CheckpointManager> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetCheckpointPath(string pipelineFilePath)
    {
        var fullPath = Path.GetFullPath(pipelineFilePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var checkpointDir = Path.Combine(dir, ".codegenesis");
        return Path.Combine(checkpointDir, $"{stem}.checkpoint.json");
    }

    public PipelineCheckpoint? Load(string pipelineFilePath)
    {
        var path = GetCheckpointPath(pipelineFilePath);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PipelineCheckpoint>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load checkpoint from {Path}, ignoring", path);
            return null;
        }
    }

    public void Save(PipelineCheckpoint checkpoint, string pipelineFilePath)
    {
        var path = GetCheckpointPath(pipelineFilePath);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        var tmpPath = path + ".tmp";

        try
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
            logger.LogDebug("Checkpoint saved to {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save checkpoint to {Path}", path);
            // Clean up temp file if it exists
            try { File.Delete(tmpPath); } catch { /* best effort */ }
        }
    }

    public void Delete(string pipelineFilePath)
    {
        var path = GetCheckpointPath(pipelineFilePath);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                logger.LogDebug("Checkpoint deleted: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete checkpoint at {Path}", path);
        }
    }

    public string ComputeFileHash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
