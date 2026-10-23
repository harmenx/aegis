using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Gitmos
{
    // Reads points.json, appended points.jsonl lines, and POST http://127.0.0.1:8787/points.
    public class PointsFeed : MonoBehaviour
    {
        [SerializeField] int port = 8787;
        [SerializeField] bool listen = true;

        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly Dictionary<string, string> jsonStamps = new Dictionary<string, string>();
        readonly Dictionary<string, long> lineOffsets = new Dictionary<string, long>();
        readonly Dictionary<string, StringBuilder> lineBuffers = new Dictionary<string, StringBuilder>();

        Thread thread;
        HttpListener listener;
        float pollIn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PauseDemoWhenAFileIsWaiting()
        {
            if (FileHasPoints(Root("points.json")) || FileHasPoints(Root("points.jsonl")) || FileHasPoints(Streaming("points.json")))
                EventSimulator.DemoEnabled = false;
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
            if (!listen)
                return;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Start();
                thread = new Thread(Listen) { IsBackground = true, Name = "AegisPoints" };
                thread.Start();
                Debug.Log("Aegis points listening on http://127.0.0.1:" + port + "/points");
            }
            catch (Exception error)
            {
                Debug.Log("Aegis points file feed is active. The local port could not be opened. " + error.Message);
                listener = null;
            }
        }

        void OnDisable()
        {
            HttpListener open = listener;
            listener = null;
            if (open != null)
            {
                try { open.Stop(); }
                catch (Exception) { }
                open.Close();
            }
        }

        void Update()
        {
            while (inbox.TryDequeue(out string json))
                AegisFeed.SubmitJson(json);

            pollIn -= Time.deltaTime;
            if (pollIn > 0f)
                return;
            pollIn = 0.4f;
            PollJson(Root("points.json"));
            PollJson(Streaming("points.json"));
            PollLines(Root("points.jsonl"));
            PollLines(Streaming("points.jsonl"));
        }

        void PollJson(string path)
        {
            if (!File.Exists(path))
            {
                jsonStamps.Remove(path);
                return;
            }

            string stamp;
            try
            {
                FileInfo info = new FileInfo(path);
                stamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }
            catch (Exception)
            {
                return;
            }

            if (jsonStamps.TryGetValue(path, out string previous) && previous == stamp)
                return;
            jsonStamps[path] = stamp;

            try
            {
                AegisFeed.SubmitJson(File.ReadAllText(path), seen);
            }
            catch (Exception error)
            {
                Debug.LogWarning("Aegis could not read " + path + ". " + error.Message);
            }
        }

        void PollLines(string path)
        {
            if (!File.Exists(path))
            {
                lineOffsets.Remove(path);
                lineBuffers.Remove(path);
                return;
            }

            if (!lineBuffers.TryGetValue(path, out StringBuilder pending))
            {
                pending = new StringBuilder();
                lineBuffers[path] = pending;
            }
            lineOffsets.TryGetValue(path, out long offset);

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length < offset)
                    {
                        offset = 0;
                        pending.Length = 0;
                    }
                    stream.Seek(offset, SeekOrigin.Begin);
                    var buffer = new byte[4096];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        offset += read;
                        AcceptLines(pending, Encoding.UTF8.GetString(buffer, 0, read));
                    }
                }
                lineOffsets[path] = offset;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Aegis could not read " + path + ". " + error.Message);
            }
        }

        static void AcceptLines(StringBuilder pending, string chunk)
        {
            for (int i = 0; i < chunk.Length; i++)
            {
                char character = chunk[i];
                if (character == '\n')
                {
                    string line = pending.ToString().Trim();
                    pending.Length = 0;
                    if (line.Length > 0)
                        AegisFeed.SubmitJson(line);
                    continue;
                }
                if (character != '\r')
                    pending.Append(character);
            }
        }

        void Listen()
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch (Exception)
                {
                    break;
                }

                try
                {
                    Handle(context);
                }
                catch (Exception)
                {
                    try { context.Response.Abort(); }
                    catch (Exception) { }
                }
            }
        }

        void Handle(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            response.Headers["Access-Control-Allow-Methods"] = "POST, GET, OPTIONS";

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (request.HttpMethod == "POST" && request.Url.AbsolutePath.StartsWith("/points"))
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
                inbox.Enqueue(body);
            }

            byte[] payload = Encoding.UTF8.GetBytes("{\"ok\":true}");
            response.StatusCode = 200;
            response.ContentType = "application/json";
            response.ContentLength64 = payload.Length;
            response.OutputStream.Write(payload, 0, payload.Length);
            response.Close();
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

        static string Root(string name)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", name));
        }

        static string Streaming(string name)
        {
            return Path.Combine(Application.streamingAssetsPath, name);
        }
    }
}
