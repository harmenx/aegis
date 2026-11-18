using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Aegis
{
    // Brings custom points into the scene from three places:
    //   points.json   rewritten freely; only points not in the previous version are drawn
    //   points.jsonl  appended one JSON object per line
    //   HTTP          POST /points on the configured port (see PointsHttpServer)
    public class PointsFeed : MonoBehaviour
    {
        const long MaxJsonFileBytes = 32L * 1024 * 1024;

        readonly Dictionary<string, string> jsonStamps = new Dictionary<string, string>();
        readonly Dictionary<string, HashSet<string>> jsonContents = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, JsonLinesReader> lineReaders = new Dictionary<string, JsonLinesReader>();
        readonly List<string> lines = new List<string>();
        readonly byte[] readBuffer = new byte[16384];

        AegisConfig config;
        PointsHttpServer server;
        float pollIn;

        public int Pending => server != null ? server.Pending : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyDemoMode()
        {
            EventSimulator.DemoEnabled = true;
            switch (AegisConfig.Current.Demo)
            {
                case DemoMode.On:
                    AegisFeed.UseDemo(true);
                    break;
                case DemoMode.Off:
                    EventSimulator.DemoEnabled = false;
                    break;
                default:
                    // Start straight on custom data when a file is already waiting.
                    if (FileHasPoints(AegisPaths.Root("points.json")) || FileHasPoints(AegisPaths.Root("points.jsonl"))
                        || FileHasPoints(AegisPaths.Streaming("points.json")) || FileHasPoints(AegisPaths.Streaming("points.jsonl")))
                        EventSimulator.DemoEnabled = false;
                    break;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<PointsFeed>() != null)
                return;
            var host = new GameObject("Points");
            host.AddComponent<PointsFeed>();
        }

        void OnEnable()
        {
            config = AegisConfig.Current;
            if (!config.listen)
            {
                FeedStatus.Listen = ListenState.Off;
                FeedStatus.ListenDetail = "HTTP off";
                Log.Info("HTTP intake is off (listen=false). Watching points.json and points.jsonl.");
                return;
            }

            server = new PointsHttpServer(config);
            if (server.Start(out string problem))
            {
                FeedStatus.Listen = ListenState.Listening;
                FeedStatus.ListenDetail = ":" + config.port;
                Log.Info("Listening for points on " + config.Endpoint);
                return;
            }

            server.Dispose();
            server = null;
            FeedStatus.Listen = ListenState.Failed;
            FeedStatus.ListenDetail = problem;
            Log.Warn("Could not listen on " + config.Endpoint + " (" + problem + "). Points files still work. "
                + "Set a different port in " + AegisConfig.FileName + ", AEGIS_PORT, or -aegis-port.");
        }

        void OnDisable()
        {
            if (server != null)
            {
                server.Dispose();
                server = null;
            }
            if (FeedStatus.Listen == ListenState.Listening)
                FeedStatus.Listen = ListenState.Off;
        }

        void Update()
        {
            DrainServer();

            pollIn -= Time.unscaledDeltaTime;
            if (pollIn > 0f)
                return;
            pollIn = config.pollSeconds;
            PollJson(AegisPaths.Root("points.json"));
            PollJson(AegisPaths.Streaming("points.json"));
            PollLines(AegisPaths.Root("points.jsonl"));
            PollLines(AegisPaths.Streaming("points.jsonl"));
        }

        void DrainServer()
        {
            if (server == null)
                return;
            // A burst is spread across frames instead of stalling one.
            int budget = config.maxPointsPerFrame;
            while (budget-- > 0 && server.TryDequeue(out DataPoint point))
                AegisFeed.Submit(point);
        }

        void PollJson(string path)
        {
            if (!File.Exists(path))
            {
                jsonStamps.Remove(path);
                jsonContents.Remove(path);
                return;
            }

            string stamp;
            long length;
            try
            {
                var info = new FileInfo(path);
                length = info.Length;
                stamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }
            catch (Exception)
            {
                return;
            }

            if (jsonStamps.TryGetValue(path, out string previous) && previous == stamp)
                return;
            jsonStamps[path] = stamp;

            string key = "file|" + path;
            if (length > MaxJsonFileBytes)
            {
                Log.WarnOnce(key + "|size", Path.GetFileName(path) + " is over " + (MaxJsonFileBytes >> 20) + " MB and was skipped. Use points.jsonl for large streams.");
                return;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception error)
            {
                // Usually a writer still holds the file; the next poll will retry.
                jsonStamps.Remove(path);
                Log.WarnOnce(key + "|read", "Could not read " + path + ". " + error.Message);
                return;
            }

            // An empty or half-written file is not an error worth a warning.
            if (string.IsNullOrWhiteSpace(text))
                return;

            PointParseResult result = PointParser.Parse(text);
            AegisFeed.Report(result, Path.GetFileName(path));
            if (!result.Ok)
                return;
            Log.Forget(key + "|read");

            jsonContents.TryGetValue(path, out HashSet<string> before);
            List<DataPoint> fresh = NewPoints(before, result.Points, out HashSet<string> after);
            jsonContents[path] = after;
            foreach (DataPoint point in fresh)
                AegisFeed.Submit(point);
        }

        // Points in the file that were not in its previous version. Memory stays bounded by the file.
        internal static List<DataPoint> NewPoints(HashSet<string> before, List<DataPoint> points, out HashSet<string> after)
        {
            after = new HashSet<string>();
            var fresh = new List<DataPoint>();
            foreach (DataPoint point in points)
            {
                string fingerprint = PointParser.Fingerprint(point);
                if (!after.Add(fingerprint))
                    continue;
                if (before == null || !before.Contains(fingerprint))
                    fresh.Add(point);
            }
            return fresh;
        }

        void PollLines(string path)
        {
            if (!File.Exists(path))
            {
                lineReaders.Remove(path);
                return;
            }

            if (!lineReaders.TryGetValue(path, out JsonLinesReader reader))
            {
                reader = new JsonLinesReader();
                lineReaders[path] = reader;
            }

            lines.Clear();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // A shorter file was truncated or replaced: start over from the top.
                    if (stream.Length < reader.Offset)
                        reader.Reset();
                    stream.Seek(reader.Offset, SeekOrigin.Begin);
                    int read;
                    while ((read = stream.Read(readBuffer, 0, readBuffer.Length)) > 0)
                        reader.Feed(readBuffer, read, lines);
                }
            }
            catch (Exception error)
            {
                Log.WarnOnce("file|" + path + "|read", "Could not read " + path + ". " + error.Message);
                return;
            }

            string name = Path.GetFileName(path);
            foreach (string line in lines)
            {
                PointParseResult result = PointParser.Parse(line);
                AegisFeed.Report(result, name);
                foreach (DataPoint point in result.Points)
                    AegisFeed.Submit(point);
            }
            if (reader.DroppedLines > 0)
                Log.WarnOnce("file|" + path + "|long", name + ": skipped " + reader.DroppedLines + " line(s) over 1 MB.");
        }

        static bool FileHasPoints(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 2;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
