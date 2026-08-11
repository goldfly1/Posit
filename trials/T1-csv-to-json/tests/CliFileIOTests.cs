using System;
using System.IO;
using System.Linq;
using Xunit;
using Cli;

namespace Cli.Tests
{
    public class CliFileIOTests : IDisposable
    {
        private readonly string _tempDir;

        public CliFileIOTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CliFileIOTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Fact]
        public void ReadFile_ExistingFile_ReturnsContent()
        {
            string path = Path.Combine(_tempDir, "test.txt");
            File.WriteAllText(path, "hello world");
            Assert.Equal("hello world", FileIO.ReadFile(path));
        }

        [Fact]
        public void ReadFile_EmptyFile_ReturnsEmptyString()
        {
            string path = Path.Combine(_tempDir, "empty.txt");
            File.WriteAllText(path, "");
            Assert.Equal("", FileIO.ReadFile(path));
        }

        [Fact]
        public void ReadFile_MissingFile_ThrowsFileNotFoundException()
        {
            string path = Path.Combine(_tempDir, "missing.txt");
            Assert.Throws<FileNotFoundException>(() => FileIO.ReadFile(path));
        }

        [Fact]
        public void WriteFile_NewFile_CreatesFileWithContent()
        {
            string path = Path.Combine(_tempDir, "write.txt");
            FileIO.WriteFile(path, "content");
            Assert.True(File.Exists(path));
            Assert.Equal("content", File.ReadAllText(path));
        }

        [Fact]
        public void WriteFile_EmptyContent_CreatesEmptyFile()
        {
            string path = Path.Combine(_tempDir, "emptywrite.txt");
            FileIO.WriteFile(path, "");
            Assert.True(File.Exists(path));
            Assert.Equal(0, new FileInfo(path).Length);
        }

        [Fact]
        public void WriteFile_OverwritesExistingFile()
        {
            string path = Path.Combine(_tempDir, "overwrite.txt");
            File.WriteAllText(path, "old");
            FileIO.WriteFile(path, "new");
            Assert.Equal("new", File.ReadAllText(path));
        }

        [Fact]
        public void AppendFile_ExistingFile_AppendsContent()
        {
            string path = Path.Combine(_tempDir, "append.txt");
            File.WriteAllText(path, "base");
            FileIO.AppendFile(path, "-suffix");
            Assert.Equal("base-suffix", File.ReadAllText(path));
        }

        [Fact]
        public void AppendFile_NewFile_CreatesFileWithContent()
        {
            string path = Path.Combine(_tempDir, "appendnew.txt");
            FileIO.AppendFile(path, "content");
            Assert.True(File.Exists(path));
            Assert.Equal("content", File.ReadAllText(path));
        }

        [Fact]
        public void AppendFile_EmptyContent_DoesNotChangeFile()
        {
            string path = Path.Combine(_tempDir, "appendempty.txt");
            File.WriteAllText(path, "base");
            FileIO.AppendFile(path, "");
            Assert.Equal("base", File.ReadAllText(path));
        }

        [Fact]
        public void ReadLines_ExistingFile_ReturnsLines()
        {
            string path = Path.Combine(_tempDir, "lines.txt");
            File.WriteAllLines(path, new[] { "a", "b", "c" });
            string[] lines = FileIO.ReadLines(path);
            Assert.Equal(new[] { "a", "b", "c" }, lines);
        }

        [Fact]
        public void ReadLines_EmptyFile_ReturnsEmptyArray()
        {
            string path = Path.Combine(_tempDir, "emptylines.txt");
            File.WriteAllText(path, "");
            string[] lines = FileIO.ReadLines(path);
            Assert.Empty(lines);
        }

        [Fact]
        public void ReadLines_MissingFile_ThrowsFileNotFoundException()
        {
            string path = Path.Combine(_tempDir, "missinglines.txt");
            Assert.Throws<FileNotFoundException>(() => FileIO.ReadLines(path));
        }

        [Fact]
        public void WriteLines_NewFile_CreatesFileWithLines()
        {
            string path = Path.Combine(_tempDir, "writelines.txt");
            FileIO.WriteLines(path, new[] { "x", "y" });
            Assert.True(File.Exists(path));
            Assert.Equal(new[] { "x", "y" }, File.ReadAllLines(path));
        }

        [Fact]
        public void WriteLines_EmptyArray_CreatesEmptyFile()
        {
            string path = Path.Combine(_tempDir, "writelinesempty.txt");
            FileIO.WriteLines(path, Array.Empty<string>());
            Assert.True(File.Exists(path));
            Assert.Equal(0, new FileInfo(path).Length);
        }

        [Fact]
        public void WriteLines_OverwritesExistingFile()
        {
            string path = Path.Combine(_tempDir, "overwritelines.txt");
            File.WriteAllLines(path, new[] { "old" });
            FileIO.WriteLines(path, new[] { "new1", "new2" });
            Assert.Equal(new[] { "new1", "new2" }, File.ReadAllLines(path));
        }

        [Fact]
        public void FileExists_ExistingFile_ReturnsTrue()
        {
            string path = Path.Combine(_tempDir, "exists.txt");
            File.WriteAllText(path, "x");
            Assert.True(FileIO.FileExists(path));
        }

        [Fact]
        public void FileExists_MissingFile_ReturnsFalse()
        {
            string path = Path.Combine(_tempDir, "missingexists.txt");
            Assert.False(FileIO.FileExists(path));
        }

        [Fact]
        public void FileExists_EmptyPath_ReturnsFalse()
        {
            Assert.False(FileIO.FileExists(""));
        }

        [Fact]
        public void FileExists_DirectoryPath_ReturnsFalse()
        {
            Assert.False(FileIO.FileExists(_tempDir));
        }
    }
}