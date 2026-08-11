using System;
using System.IO;
using Xunit;

namespace MarketplaceApi.Tests
{
    public class ProgramTests
    {
        [Fact]
        public void Main_NoArgs_ReturnsOneAndPrintsUsage()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(Array.Empty<string>());
                Assert.Equal(1, result);
                Assert.Contains("Usage: MarketplaceApi", sw.ToString());
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
                var result = MarketplaceApi.Program.Main(new[] { "start" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommand_WithId_PrintsProvidedId()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "start", "abc123" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow abc123", sw.ToString());
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
                var result = MarketplaceApi.Program.Main(new[] { "advance", "wf-1" });
                Assert.Equal(0, result);
                Assert.Contains("Advanced workflow wf-1", sw.ToString());
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
                var result = MarketplaceApi.Program.Main(new[] { "status", "wf-2" });
                Assert.Equal(0, result);
                Assert.Contains("Workflow wf-2 status", sw.ToString());
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
                var result = MarketplaceApi.Program.Main(new[] { "bogus" });
                Assert.Equal(1, result);
                Assert.Contains("Unknown command: bogus", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_CommandCaseInsensitive_StartLowercase_ReturnsZero()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "START" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommand_NoId_GeneratesGuid()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "start" });
                Assert.Equal(0, result);
                var output = sw.ToString();
                Assert.Contains("Started workflow ", output);
                var id = output.Replace("Started workflow ", "").Trim();
                Assert.True(Guid.TryParseExact(id, "N", out _));
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_AdvanceCommand_NoId_GeneratesGuid()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "advance" });
                Assert.Equal(0, result);
                var output = sw.ToString();
                Assert.Contains("Advanced workflow ", output);
                var id = output.Replace("Advanced workflow ", "").Trim();
                Assert.True(Guid.TryParseExact(id, "N", out _));
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StatusCommand_NoId_GeneratesGuid()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "status" });
                Assert.Equal(0, result);
                var output = sw.ToString();
                Assert.Contains("Workflow ", output);
                Assert.Contains(" status", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_EmptyStringCommand_ReturnsOneAndPrintsUnknown()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "" });
                Assert.Equal(1, result);
                Assert.Contains("Unknown command: ", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_NullCommand_ReturnsOneAndPrintsUnknown()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { (string)null! });
                Assert.Equal(1, result);
                Assert.Contains("Unknown command: ", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_ExtraArguments_IgnoresThem()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "start", "id1", "extra" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow id1", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_WhitespaceCommand_ReturnsOneAndPrintsUnknown()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = MarketplaceApi.Program.Main(new[] { "   " });
                Assert.Equal(1, result);
                Assert.Contains("Unknown command:    ", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}