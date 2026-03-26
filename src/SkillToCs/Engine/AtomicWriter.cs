using System.Security.Cryptography;
using System.Text;

namespace SkillToCs.Engine;

public sealed record WriteResult(List<FileChange> Changes);

public static class AtomicWriter
{
    public static WriteResult WriteFragments(IReadOnlyList<CodeFragment> fragments, bool dryRun = false)
    {
        var changes = new List<FileChange>();
        var backups = new List<(string Original, string Backup)>();

        try
        {
            foreach (var fragment in fragments)
            {
                var change = dryRun
                    ? PreviewFragment(fragment)
                    : ApplyFragment(fragment, backups);

                changes.Add(change);
            }
        }
        catch
        {
            // Roll back all changes on failure
            if (!dryRun)
                Rollback(backups);

            throw;
        }
        finally
        {
            // Clean up backup files on success
            if (!dryRun)
            {
                foreach (var (_, backup) in backups)
                {
                    try { File.Delete(backup); } catch { /* best effort */ }
                }
            }
        }

        return new WriteResult(changes);
    }

    private static FileChange ApplyFragment(CodeFragment fragment, List<(string, string)> backups)
    {
        var targetFile = Path.GetFullPath(fragment.TargetFile);
        var lineEnding = DetectLineEnding(targetFile);

        return fragment.Type switch
        {
            FragmentType.NewFile => ApplyNewFile(fragment, targetFile, backups),
            FragmentType.InsertAfter => ApplyInsert(fragment, targetFile, lineEnding, after: true, backups),
            FragmentType.InsertBefore => ApplyInsert(fragment, targetFile, lineEnding, after: false, backups),
            FragmentType.Replace => ApplyReplace(fragment, targetFile, lineEnding, backups),
            _ => throw new ArgumentOutOfRangeException(nameof(fragment))
        };
    }

    private static FileChange ApplyNewFile(
        CodeFragment fragment, string targetFile, List<(string, string)> backups)
    {
        if (File.Exists(targetFile))
        {
            var existingHash = ComputeHash(File.ReadAllText(targetFile));
            var newHash = ComputeHash(fragment.Content);
            if (existingHash == newHash)
                return new FileChange(targetFile, FileChangeAction.Skipped, 0, 0, null);

            // Back up existing file before overwriting
            var backup = BackupFile(targetFile);
            backups.Add((targetFile, backup));
        }

        var dir = Path.GetDirectoryName(targetFile);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        WriteViaTempFile(targetFile, fragment.Content);

        var linesAdded = fragment.Content.Split('\n').Length;
        return new FileChange(targetFile, FileChangeAction.Created, linesAdded, 0, null);
    }

    private static FileChange ApplyInsert(
        CodeFragment fragment, string targetFile, string lineEnding,
        bool after, List<(string, string)> backups)
    {
        if (!File.Exists(targetFile))
            throw new FileNotFoundException($"Target file not found: {targetFile}");

        if (fragment.IdempotencyKey is not null)
        {
            var existing = File.ReadAllText(targetFile);
            if (existing.Contains(fragment.IdempotencyKey, StringComparison.Ordinal))
                return new FileChange(targetFile, FileChangeAction.Skipped, 0, 0, null);
        }

        var backup = BackupFile(targetFile);
        backups.Add((targetFile, backup));

        var lines = File.ReadAllText(targetFile).Split(lineEnding).ToList();
        var insertLine = fragment.InsertionPoint?.Line ?? lines.Count;

        // Clamp to valid range
        var index = Math.Clamp(after ? insertLine : insertLine - 1, 0, lines.Count);

        var newLines = fragment.Content.Split(lineEnding);
        lines.InsertRange(index, newLines);

        WriteViaTempFile(targetFile, string.Join(lineEnding, lines));

        return new FileChange(targetFile, FileChangeAction.Modified, newLines.Length, 0, null);
    }

    private static FileChange ApplyReplace(
        CodeFragment fragment, string targetFile, string lineEnding,
        List<(string, string)> backups)
    {
        if (!File.Exists(targetFile))
            throw new FileNotFoundException($"Target file not found: {targetFile}");

        if (fragment.IdempotencyKey is not null)
        {
            var existing = File.ReadAllText(targetFile);
            if (existing.Contains(fragment.IdempotencyKey, StringComparison.Ordinal)
                && !existing.Contains(fragment.Content, StringComparison.Ordinal))
            {
                // Key exists but content differs -- proceed with replace
            }
            else if (existing.Contains(fragment.Content, StringComparison.Ordinal))
            {
                return new FileChange(targetFile, FileChangeAction.Skipped, 0, 0, null);
            }
        }

        var backup = BackupFile(targetFile);
        backups.Add((targetFile, backup));

        var text = File.ReadAllText(targetFile);
        var linesRemoved = 0;

        // Replace using markers from InsertionPoint
        if (fragment.InsertionPoint is { AfterMarker: not null } ip)
        {
            var markerIdx = text.IndexOf(ip.AfterMarker, StringComparison.Ordinal);
            if (markerIdx >= 0)
            {
                var insertIdx = markerIdx + ip.AfterMarker.Length;
                var beforeEnd = ip.BeforeMarker is not null
                    ? text.IndexOf(ip.BeforeMarker, insertIdx, StringComparison.Ordinal)
                    : -1;

                if (beforeEnd >= 0)
                {
                    var removed = text[insertIdx..beforeEnd];
                    linesRemoved = removed.Split(lineEnding).Length;
                    text = string.Concat(text.AsSpan(0, insertIdx), fragment.Content, text.AsSpan(beforeEnd));
                }
                else
                {
                    text = string.Concat(text.AsSpan(0, insertIdx), fragment.Content, text.AsSpan(insertIdx));
                }
            }
        }
        else
        {
            // Line-based replace: replace lines starting at InsertionPoint.Line
            var lines = text.Split(lineEnding).ToList();
            var startLine = (fragment.InsertionPoint?.Line ?? 1) - 1;
            var newLines = fragment.Content.Split(lineEnding);

            if (startLine >= 0 && startLine < lines.Count)
            {
                var removeCount = Math.Min(newLines.Length, lines.Count - startLine);
                linesRemoved = removeCount;
                lines.RemoveRange(startLine, removeCount);
                lines.InsertRange(startLine, newLines);
            }

            text = string.Join(lineEnding, lines);
        }

        WriteViaTempFile(targetFile, text);

        var linesAdded = fragment.Content.Split(lineEnding).Length;
        return new FileChange(targetFile, FileChangeAction.Modified, linesAdded, linesRemoved, null);
    }

    private static FileChange PreviewFragment(CodeFragment fragment)
    {
        var targetFile = Path.GetFullPath(fragment.TargetFile);

        return fragment.Type switch
        {
            FragmentType.NewFile => new FileChange(
                targetFile,
                File.Exists(targetFile) ? FileChangeAction.WouldModify : FileChangeAction.WouldCreate,
                fragment.Content.Split('\n').Length,
                0,
                $"+++ {targetFile}\n{PrefixLines(fragment.Content, "+")}"),

            FragmentType.InsertAfter or FragmentType.InsertBefore => new FileChange(
                targetFile,
                FileChangeAction.WouldModify,
                fragment.Content.Split('\n').Length,
                0,
                $"@@ line {fragment.InsertionPoint?.Line ?? 0} @@\n{PrefixLines(fragment.Content, "+")}"),

            FragmentType.Replace => new FileChange(
                targetFile,
                FileChangeAction.WouldModify,
                fragment.Content.Split('\n').Length,
                0,
                $"@@ replace at line {fragment.InsertionPoint?.Line ?? 0} @@\n{PrefixLines(fragment.Content, "~")}"),

            _ => new FileChange(targetFile, FileChangeAction.Skipped, 0, 0, null)
        };
    }

    private static string DetectLineEnding(string filePath)
    {
        if (!File.Exists(filePath))
            return Environment.NewLine;

        var text = File.ReadAllText(filePath);
        return text.Contains("\r\n") ? "\r\n" : "\n";
    }

    private static void WriteViaTempFile(string targetFile, string content)
    {
        var dir = Path.GetDirectoryName(targetFile)!;
        var tempFile = Path.Combine(dir, $".tmp_{Path.GetFileName(targetFile)}_{Guid.NewGuid():N}");

        File.WriteAllText(tempFile, content);
        File.Move(tempFile, targetFile, overwrite: true);
    }

    private static string BackupFile(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath)!;
        var backupPath = Path.Combine(dir, $".backup_{Path.GetFileName(filePath)}_{Guid.NewGuid():N}");
        File.Copy(filePath, backupPath, overwrite: true);
        return backupPath;
    }

    private static void Rollback(List<(string Original, string Backup)> backups)
    {
        // Restore in reverse order
        for (var i = backups.Count - 1; i >= 0; i--)
        {
            var (original, backup) = backups[i];
            try
            {
                File.Move(backup, original, overwrite: true);
            }
            catch { /* best effort rollback */ }
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string PrefixLines(string content, string prefix) =>
        string.Join('\n', content.Split('\n').Select(l => $"{prefix} {l}"));
}
