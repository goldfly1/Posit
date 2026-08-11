using System;
using System.IO;
using Xunit;
using Cli;

namespace Cli.Tests
{
    public class CliioconsoleprogramTests
    {
        [Fact]
        public void Main_NoArgs_PrintsUsageAndReturns1()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(Array.Empty<string>());
                Assert.Equal(1, exitCode);
                Assert.Contains("Usage: Cli start | advance <id> | status <id>", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartCommand_PrintsStartedAndReturns0()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "start" });
                Assert.Equal(0, exitCode);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StartWithId_PrintsStartedWithIdAndReturns0()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "start", "abc123" });
                Assert.Equal(0, exitCode);
                Assert.Contains("Started workflow abc123", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_AdvanceCommand_PrintsAdvancedAndReturns0()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "advance" });
                Assert.Equal(0, exitCode);
                Assert.Contains("Advanced workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_AdvanceWithId_PrintsAdvancedWithIdAndReturns0()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "advance", "wf-42" });
                Assert.Equal(0, exitCode);
                Assert.Contains("Advanced workflow wf-42", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_StatusCommand_PrintsStatusAndReturns0()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "status" });
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
        public void Main_StatusWithId_PrintsStatusWithIdAndReturns0()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "status", "wf-7" });
                Assert.Equal(0, exitCode);
                Assert.Contains("Workflow wf-7 status", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_UnknownCommand_PrintsUnknownAndReturns1()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "bogus" });
                Assert.Equal(1, exitCode);
                Assert.Contains("Unknown command: bogus", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_CommandCaseInsensitive_AcceptsUpperCase()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "START" });
                Assert.Equal(0, exitCode);
                Assert.Contains("Started workflow", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Main_EmptyCommandString_Returns1()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exitCode = Program.Main(new[] { "" });
                Assert.Equal(1, exitCode);
                Assert.Contains("Unknown command: ", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
