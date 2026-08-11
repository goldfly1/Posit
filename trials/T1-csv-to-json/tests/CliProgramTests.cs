using System;
using System.IO;
using Xunit;
using Cli;

namespace Cli.Tests
{
    public class CliProgramTests
    {
        [Fact]
        public void Main_NoArgs_ReturnsOneAndPrintsUsage()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(Array.Empty<string>());
                Assert.Equal(1, result);
                Assert.Contains("Usage:", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommand_ReturnsZeroAndPrintsStarted()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { "start" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommandWithId_PrintsProvidedId()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { "start", "abc123" });
                Assert.Equal(0, result);
                Assert.Contains("abc123", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_AdvanceCommand_ReturnsZeroAndPrintsAdvanced()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { "advance", "id1" });
                Assert.Equal(0, result);
                Assert.Contains("Advanced workflow id1", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StatusCommand_ReturnsZeroAndPrintsStatus()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { "status", "id1" });
                Assert.Equal(0, result);
                Assert.Contains("Workflow id1 status", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Theory]
        [InlineData("START")]
        [InlineData("Start")]
        [InlineData("sTaRt")]
        public void Main_CommandCaseInsensitive_ReturnsZero(string command)
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { command });
                Assert.Equal(0, result);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_UnknownCommand_ReturnsOneAndPrintsUnknown()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { "bogus" });
                Assert.Equal(1, result);
                Assert.Contains("Unknown command: bogus", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_EmptyStringCommand_ReturnsOne()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { "" });
                Assert.Equal(1, result);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartWithExtraArgs_IgnoresExtraArgs()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                int result = Program.Main(new[] { "start", "id1", "extra" });
                Assert.Equal(0, result);
                Assert.Contains("id1", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}