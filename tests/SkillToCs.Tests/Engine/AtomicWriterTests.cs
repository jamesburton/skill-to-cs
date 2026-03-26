using SkillToCs.Engine;

namespace SkillToCs.Tests.Engine;

public class AtomicWriterTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void NewFile_CreatesFile()
    {
        var targetFile = Path.Combine(_tempDir, "NewClass.cs");
        var content = "public class NewClass { }";

        var fragments = new List<CodeFragment>
        {
            new(targetFile, null, content, FragmentType.NewFile)
        };

        var result = AtomicWriter.WriteFragments(fragments);

        Assert.Single(result.Changes);
        Assert.Equal(FileChangeAction.Created, result.Changes[0].Action);
        Assert.True(File.Exists(targetFile));
        Assert.Equal(content, File.ReadAllText(targetFile));
    }

    [Fact]
    public void NewFile_SkipsIfIdenticalContent()
    {
        var targetFile = Path.Combine(_tempDir, "Existing.cs");
        var content = "public class Existing { }";
        File.WriteAllText(targetFile, content);

        var fragments = new List<CodeFragment>
        {
            new(targetFile, null, content, FragmentType.NewFile)
        };

        var result = AtomicWriter.WriteFragments(fragments);

        Assert.Single(result.Changes);
        Assert.Equal(FileChangeAction.Skipped, result.Changes[0].Action);
    }

    [Fact]
    public void InsertAfter_InsertsAtCorrectLine()
    {
        var targetFile = Path.Combine(_tempDir, "Insert.cs");
        File.WriteAllText(targetFile, "line1\nline2\nline3\n");

        var insertion = new InsertionPoint(targetFile, 2, 0);
        var fragments = new List<CodeFragment>
        {
            new(targetFile, insertion, "inserted", FragmentType.InsertAfter)
        };

        var result = AtomicWriter.WriteFragments(fragments);

        Assert.Equal(FileChangeAction.Modified, result.Changes[0].Action);
        var lines = File.ReadAllText(targetFile).Split('\n');
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("inserted", lines[2]);
        Assert.Equal("line3", lines[3]);
    }

    [Fact]
    public void IdempotencyKey_SkipsIfPresent()
    {
        var targetFile = Path.Combine(_tempDir, "Idempotent.cs");
        var existingContent = "line1\n// KEY_ABC\nline3\n";
        File.WriteAllText(targetFile, existingContent);

        var insertion = new InsertionPoint(targetFile, 3, 0);
        var fragments = new List<CodeFragment>
        {
            new(targetFile, insertion, "new content", FragmentType.InsertAfter, "KEY_ABC")
        };

        var result = AtomicWriter.WriteFragments(fragments);

        Assert.Single(result.Changes);
        Assert.Equal(FileChangeAction.Skipped, result.Changes[0].Action);
        Assert.Equal(existingContent, File.ReadAllText(targetFile));
    }

    [Fact]
    public void DryRun_DoesNotWrite()
    {
        var targetFile = Path.Combine(_tempDir, "DryRun.cs");
        var content = "public class DryRun { }";

        var fragments = new List<CodeFragment>
        {
            new(targetFile, null, content, FragmentType.NewFile)
        };

        var result = AtomicWriter.WriteFragments(fragments, dryRun: true);

        Assert.Single(result.Changes);
        Assert.Equal(FileChangeAction.WouldCreate, result.Changes[0].Action);
        Assert.False(File.Exists(targetFile));
    }

    [Fact]
    public void Rollback_OnFailure()
    {
        // Create first file that will be overwritten
        var file1 = Path.Combine(_tempDir, "File1.cs");
        File.WriteAllText(file1, "original content");

        // Second fragment targets a non-existent file for InsertAfter, which will throw
        var file2 = Path.Combine(_tempDir, "nonexistent", "deep", "File2.cs");
        var insertion = new InsertionPoint(file2, 1, 0);

        var fragments = new List<CodeFragment>
        {
            new(file1, null, "modified content", FragmentType.NewFile),
            new(file2, insertion, "insert this", FragmentType.InsertAfter)
        };

        Assert.Throws<FileNotFoundException>(() => AtomicWriter.WriteFragments(fragments));

        // First file should have been rolled back to original content
        Assert.Equal("original content", File.ReadAllText(file1));
    }
}
