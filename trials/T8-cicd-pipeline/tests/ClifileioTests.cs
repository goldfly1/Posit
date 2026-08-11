using System;
using System.IO;
using System.Linq;
using Xunit;
using Cli;

namespace Cli.Tests
{
    public class ClifileioTests
    {
        [Fact]
        public void ReadFile_ExistingFile_ReturnsContent()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "hello");
                var result = FileIO.ReadFile(path);
                Assert.Equal("hello", result);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadFile_EmptyFile_ReturnsEmptyString()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "");
                var result = FileIO.ReadFile(path);
                Assert.Equal("", result);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadFile_MissingFile_ThrowsFileNotFoundException()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            Assert.Throws<FileNotFoundException>(() => FileIO.ReadFile(path));
        }

        [Fact]
        public void WriteFile_NewFile_CreatesFileWithContent()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                FileIO.WriteFile(path, "data");
                Assert.True(File.Exists(path));
                Assert.Equal("data", File.ReadAllText(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void WriteFile_EmptyContent_CreatesEmptyFile()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                FileIO.WriteFile(path, "");
                Assert.True(File.Exists(path));
                Assert.Equal("", File.ReadAllText(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void WriteFile_OverwritesExistingFile()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "old");
                FileIO.WriteFile(path, "new");
                Assert.Equal("new", File.ReadAllText(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void AppendFile_ExistingFile_AppendsContent()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "a");
                FileIO.AppendFile(path, "b");
                Assert.Equal("ab", File.ReadAllText(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void AppendFile_NewFile_CreatesFileWithContent()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                FileIO.AppendFile(path, "x");
                Assert.True(File.Exists(path));
                Assert.Equal("x", File.ReadAllText(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadLines_ExistingFile_ReturnsLines()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[] { "a", "b", "c" });
                var result = FileIO.ReadLines(path);
                Assert.Equal(new[] { "a", "b", "c" }, result);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadLines_EmptyFile_ReturnsEmptyArray()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "");
                var result = FileIO.ReadLines(path);
                Assert.Empty(result);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadLines_MissingFile_ThrowsFileNotFoundException()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            Assert.Throws<FileNotFoundException>(() => FileIO.ReadLines(path));
        }

        [Fact]
        public void WriteLines_NewFile_CreatesFileWithLines()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                FileIO.WriteLines(path, new[] { "x", "y" });
                Assert.True(File.Exists(path));
                Assert.Equal(new[] { "x", "y" }, File.ReadAllLines(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void WriteLines_EmptyArray_CreatesEmptyFile()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                FileIO.WriteLines(path, Array.Empty<string>());
                Assert.True(File.Exists(path));
                Assert.Empty(File.ReadAllLines(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void FileExists_ExistingFile_ReturnsTrue()
        {
            var path = Path.GetTempFileName();
            try
            {
                Assert.True(FileIO.FileExists(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void FileExists_MissingFile_ReturnsFalse()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            Assert.False(FileIO.FileExists(path));
        }

        [Fact]
        public void FileExists_EmptyPath_ReturnsFalse()
        {
            Assert.False(FileIO.FileExists(""));
        }
    }
}
