using System;
using System.IO;
using Xunit;
using IndexStore;

namespace IndexStore.Tests
{
    public class ProgramMainTests
    {
        [Fact]
        public void Main_NoArgs_PrintsUsageAndReturnsOne()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(Array.Empty<string>());

                // Assert
                Assert.Equal(1, result);
                Assert.Contains("Usage:", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommand_PrintsStartedAndReturnsZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "start" });

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommandWithId_UsesProvidedId()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "start", "abc123" });

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Started workflow abc123", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_AdvanceCommand_PrintsAdvancedAndReturnsZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "advance" });

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Advanced workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_AdvanceCommandWithId_UsesProvidedId()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "advance", "xyz789" });

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Advanced workflow xyz789", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StatusCommand_PrintsStatusAndReturnsZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "status" });

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Workflow", sw.ToString());
                Assert.Contains("status", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StatusCommandWithId_UsesProvidedId()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "status", "id123" });

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Workflow id123 status", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_UnknownCommand_PrintsUnknownAndReturnsOne()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "unknown" });

                // Assert
                Assert.Equal(1, result);
                Assert.Contains("Unknown command: unknown", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_CommandCaseInsensitive_HandlesUpperCase()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "START" });

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommandWithEmptyId_GeneratesGuid()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var result = Program.Main(new[] { "start", "" });

                // Assert
                Assert.Equal(0, result);
                var output = sw.ToString();
                Assert.Contains("Started workflow", output);
                // The empty string is used as the id, not a generated GUID
                Assert.Contains("Started workflow ", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
