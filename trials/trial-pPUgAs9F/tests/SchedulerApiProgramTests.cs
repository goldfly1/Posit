using System;
using System.IO;
using Xunit;

namespace SchedulerApi.Tests
{
    public class SchedulerApiProgramTests
    {
        [Fact]
        public void Main_NoArgs_ReturnsOneAndPrintsUsage()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = SchedulerApi.Program.Main(Array.Empty<string>());
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
                var result = SchedulerApi.Program.Main(new[] { "start" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommandWithId_ReturnsZeroAndPrintsStartedWithId()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = SchedulerApi.Program.Main(new[] { "start", "wf-123" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow wf-123", sw.ToString());
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
                var result = SchedulerApi.Program.Main(new[] { "advance", "wf-123" });
                Assert.Equal(0, result);
                Assert.Contains("Advanced workflow wf-123", sw.ToString());
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
                var result = SchedulerApi.Program.Main(new[] { "status", "wf-123" });
                Assert.Equal(0, result);
                Assert.Contains("Workflow wf-123 status", sw.ToString());
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
                var result = SchedulerApi.Program.Main(new[] { "bogus" });
                Assert.Equal(1, result);
                Assert.Contains("Unknown command: bogus", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_CommandCaseInsensitive_ReturnsZero()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = SchedulerApi.Program.Main(new[] { "START" });
                Assert.Equal(0, result);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommandWithoutId_GeneratesGuid()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var result = SchedulerApi.Program.Main(new[] { "start" });
                Assert.Equal(0, result);
                var output = sw.ToString();
                Assert.Contains("Started workflow ", output);
                var id = output.Replace("Started workflow ", "").Trim();
                Assert.True(Guid.TryParseExact(id, "N", out _), $"Expected GUID in N format, got '{id}'");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}