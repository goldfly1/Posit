// PipelineContracts — Stream I/O portal caps
// Auto-bound to Dafny stub: stream-io
// DO NOT invent new structure. This file only inlays function behind pre-cut portals.

using System.IO;
using _module;

namespace PipelineContracts
{
    public static partial class StreamIO
    {
        private static readonly System.Collections.Generic.Dictionary<int, StreamReader> _streams = new();

        // Portal: OpenStream(path) returns (streamId: int)
        public static int OpenStream(string path)
        {
            var sr = new StreamReader(path);
            var id = _streams.Count;
            _streams[id] = sr;
            return id;
        }

        // Portal: ReadChunk(streamId, maxBytes) returns (chunk: string)
        public static string ReadChunk(int streamId, int maxBytes)
        {
            if (!_streams.TryGetValue(streamId, out var sr))
                return "";
            var buffer = new char[maxBytes];
            var read = sr.Read(buffer, 0, maxBytes);
            return new string(buffer, 0, read);
        }

        // Portal: CloseStream(streamId)
        public static void CloseStream(int streamId)
        {
            if (_streams.TryGetValue(streamId, out var sr))
            {
                sr.Close();
                _streams.Remove(streamId);
            }
        }

        // Portal: HasMore(streamId) returns (hasMore: bool)
        public static bool HasMore(int streamId)
        {
            if (!_streams.TryGetValue(streamId, out var sr))
                return false;
            return !sr.EndOfStream;
        }
    }
}