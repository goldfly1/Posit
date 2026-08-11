using System;
using System.IO;
using Xunit;
using SchedulerCli;

namespace SchedulerCli.Tests
{
    public class SchedulerCliProgramTests
    {
        [Fact]
        public void Main_NoArgs_PrintsUsageAndReturnsNonZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var exitCode = Program.Main(Array.Empty<string>());

                // Assert
                Assert.NotEqual(0, exitCode);
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
                var exitCode = Program.Main(new[] { "start" });

                // Assert
                Assert.Equal(0, exitCode);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommandWithId_PrintsStartedWithIdAndReturnsZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            var workflowId = "wf-123";

            try
            {
                // Act
                var exitCode = Program.Main(new[] { "start", workflowId });

                // Assert
                Assert.Equal(0, exitCode);
                Assert.Contains($"Started workflow {workflowId}", sw.ToString());
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
                var exitCode = Program.Main(new[] { "advance" });

                // Assert
                Assert.Equal(0, exitCode);
                Assert.Contains("Advanced workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_AdvanceCommandWithId_PrintsAdvancedWithIdAndReturnsZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            var workflowId = "wf-456";

            try
            {
                // Act
                var exitCode = Program.Main(new[] { "advance", workflowId });

                // Assert
                Assert.Equal(0, exitCode);
                Assert.Contains($"Advanced workflow {workflowId}", sw.ToString());
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
                var exitCode = Program.Main(new[] { "status" });

                // Assert
                Assert.Equal(0, exitCode);
                Assert.Contains("Workflow", sw.ToString());
                Assert.Contains("status", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StatusCommandWithId_PrintsStatusWithIdAndReturnsZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            var workflowId = "wf-789";

            try
            {
                // Act
                var exitCode = Program.Main(new[] { "status", workflowId });

                // Assert
                Assert.Equal(0, exitCode);
                Assert.Contains($"Workflow {workflowId} status", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_UnknownCommand_PrintsErrorAndReturnsNonZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var exitCode = Program.Main(new[] { "unknown" });

                // Assert
                Assert.NotEqual(0, exitCode);
                Assert.Contains("Unknown command", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_CommandCaseInsensitive_ReturnsZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var exitCode = Program.Main(new[] { "START" });

                // Assert
                Assert.Equal(0, exitCode);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_EmptyCommand_PrintsUsageAndReturnsNonZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var exitCode = Program.Main(new[] { "" });

                // Assert
                Assert.NotEqual(0, exitCode);
                Assert.Contains("Unknown command", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_NullCommand_PrintsUsageAndReturnsNonZero()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                var exitCode = Program.Main(new[] { (string)null });

                // Assert
                Assert.NotEqual(0, exitCode);
                Assert.Contains("Unknown command", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}