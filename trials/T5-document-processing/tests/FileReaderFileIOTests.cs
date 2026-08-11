using System;
using System.IO;
using Xunit;
using FileReader;

namespace FileReader.Tests
{
    public class FileIOReadFileTests
    {
        [Fact]
        public void ReadFile_ExistingFile_ReturnsContent()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var expected = "Hello, World!";
            File.WriteAllText(tempFile, expected);

            try
            {
                // Act
                var result = FileIO.ReadFile(tempFile);

                // Assert
                Assert.Equal(expected, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void ReadFile_EmptyFile_ReturnsEmptyString()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, string.Empty);

            try
            {
                // Act
                var result = FileIO.ReadFile(tempFile);

                // Assert
                Assert.Equal(string.Empty, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void ReadFile_MissingFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => FileIO.ReadFile(missingPath));
        }

        [Fact]
        public void ReadFile_NullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => FileIO.ReadFile(null));
        }

        [Fact]
        public void ReadFile_EmptyPath_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => FileIO.ReadFile(string.Empty));
        }
    }

    public class FileIOWriteFileTests
    {
        [Fact]
        public void WriteFile_ValidPathAndContent_CreatesFileWithContent()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var content = "Test content";

            try
            {
                // Act
                FileIO.WriteFile(tempFile, content);

                // Assert
                Assert.True(File.Exists(tempFile));
                Assert.Equal(content, File.ReadAllText(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteFile_EmptyContent_CreatesEmptyFile()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                // Act
                FileIO.WriteFile(tempFile, string.Empty);

                // Assert
                Assert.True(File.Exists(tempFile));
                Assert.Equal(string.Empty, File.ReadAllText(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteFile_NullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => FileIO.WriteFile(null, "content"));
        }

        [Fact]
        public void WriteFile_EmptyPath_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => FileIO.WriteFile(string.Empty, "content"));
        }
    }

    public class FileIOAppendFileTests
    {
        [Fact]
        public void AppendFile_ExistingFile_AppendsContent()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "Initial");

            try
            {
                // Act
                FileIO.AppendFile(tempFile, "Appended");

                // Assert
                Assert.Equal("InitialAppended", File.ReadAllText(tempFile));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void AppendFile_NonExistentFile_CreatesFileWithContent()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                // Act
                FileIO.AppendFile(tempFile, "New content");

                // Assert
                Assert.True(File.Exists(tempFile));
                Assert.Equal("New content", File.ReadAllText(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void AppendFile_NullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => FileIO.AppendFile(null, "content"));
        }

        [Fact]
        public void AppendFile_EmptyPath_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => FileIO.AppendFile(string.Empty, "content"));
        }
    }

    public class FileIOReadLinesTests
    {
        [Fact]
        public void ReadLines_ExistingFile_ReturnsLines()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var lines = new[] { "line1", "line2", "line3" };
            File.WriteAllLines(tempFile, lines);

            try
            {
                // Act
                var result = FileIO.ReadLines(tempFile);

                // Assert
                Assert.Equal(lines, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void ReadLines_EmptyFile_ReturnsEmptyArray()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, string.Empty);

            try
            {
                // Act
                var result = FileIO.ReadLines(tempFile);

                // Assert
                Assert.Empty(result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void ReadLines_MissingFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => FileIO.ReadLines(missingPath));
        }

        [Fact]
        public void ReadLines_NullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => FileIO.ReadLines(null));
        }

        [Fact]
        public void ReadLines_EmptyPath_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => FileIO.ReadLines(string.Empty));
        }
    }

    public class FileIOWriteLinesTests
    {
        [Fact]
        public void WriteLines_ValidLines_CreatesFileWithLines()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var lines = new[] { "a", "b", "c" };

            try
            {
                // Act
                FileIO.WriteLines(tempFile, lines);

                // Assert
                Assert.True(File.Exists(tempFile));
                Assert.Equal(lines, File.ReadAllLines(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteLines_EmptyLines_CreatesEmptyFile()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                // Act
                FileIO.WriteLines(tempFile, Array.Empty<string>());

                // Assert
                Assert.True(File.Exists(tempFile));
                Assert.Empty(File.ReadAllLines(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void WriteLines_NullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => FileIO.WriteLines(null, new[] { "a" }));
        }

        [Fact]
        public void WriteLines_EmptyPath_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => FileIO.WriteLines(string.Empty, new[] { "a" }));
        }

        [Fact]
        public void WriteLines_NullLines_ThrowsArgumentNullException()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();

            try
            {
                // Act & Assert
                Assert.Throws<ArgumentNullException>(() => FileIO.WriteLines(tempFile, null));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }

    public class FileIOFileExistsTests
    {
        [Fact]
        public void FileExists_ExistingFile_ReturnsTrue()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();

            try
            {
                // Act
                var result = FileIO.FileExists(tempFile);

                // Assert
                Assert.True(result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void FileExists_NonExistentFile_ReturnsFalse()
        {
            // Arrange
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

            // Act
            var result = FileIO.FileExists(missingPath);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void FileExists_NullPath_ReturnsFalse()
        {
            // Act
            var result = FileIO.FileExists(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void FileExists_EmptyPath_ReturnsFalse()
        {
            // Act
            var result = FileIO.FileExists(string.Empty);

            // Assert
            Assert.False(result);
        }
    }
}
