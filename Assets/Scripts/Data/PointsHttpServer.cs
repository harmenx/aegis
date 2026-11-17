using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Aegis
{
    // Local HTTP intake. Requests are parsed and validated on the listener thread, so a
    // client gets a real status code; accepted points wait in a bounded queue for the main thread.
    //
    //   POST /points  -> 202 {"accepted":n,"rejected":n,"errors":[...]}, or 400 / 413 / 422 / 503
    //   GET  /health  -> 200 {"status":"ok",...}
    public sealed class PointsHttpServer : IDisposable
    {
        readonly AegisConfig config;
        readonly ConcurrentQueue<DataPoint> queue = new ConcurrentQueue<DataPoint>();
        int pending;
        volatile HttpListener listener;
        Thread thread;

        public PointsHttpServer(AegisConfig config)
        {
            this.config = config;
        }

        public int Pending => Volatile.Read(ref pending);
        public bool IsListening => listener != null && listener.IsListening;

        // Returns false with a reason when the port cannot be opened, e.g. it is already in use.
        public bool Start(out string problem)
        {
            problem = null;
            string host = config.bindAddress == "0.0.0.0" ? "*" : config.bindAddress;
            var opened = new HttpListener();
            try
            {
                opened.Prefixes.Add("http://" + host + ":" + config.port + "/");
                opened.Start();
            }
            catch (Exception error)
            {
                problem = Describe(error);
                try { opened.Close(); }
                catch (Exception) { }
                return false;
            }

            listener = opened;
            thread = new Thread(Run) { IsBackground = true, Name = "Aegis HTTP" };
            thread.Start();
            return true;
        }

        public bool TryDequeue(out DataPoint point)
        {
            if (!queue.TryDequeue(out point))
                return false;
            Interlocked.Decrement(ref pending);
            return true;
        }

        public void Dispose()
        {
            HttpListener open = listener;
            listener = null;
            if (open != null)
            {
                try { open.Stop(); }
                catch (Exception) { }
                try { open.Close(); }
                catch (Exception) { }
            }
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
                thread.Join(500);
            thread = null;
        }

        void Run()
        {
            while (true)
            {
                HttpListener open = listener;
                if (open == null || !open.IsListening)
                    return;

                HttpListenerContext context;
                try
                {
                    context = open.GetContext();
                }
                catch (Exception)
                {
                    // Stop() unblocks GetContext with an exception; any other failure ends the loop too.
                    return;
                }

                try
                {
                    Handle(context);
                }
                catch (Exception error)
                {
                    Log.WarnOnce("http-handler|" + error.GetType().Name, "HTTP request failed. " + error.Message);
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

            string path = request.Url.AbsolutePath.TrimEnd('/');
            string method = request.HttpMethod;

            if (method == "OPTIONS")
            {
                Send(response, 204, null);
                return;
            }

            if (path == "/health")
            {
                if (method != "GET")
                {
                    response.Headers["Allow"] = "GET, OPTIONS";
                    SendError(response, 405, "Use GET for /health.");
                    return;
                }
                Send(response, 200, Health());
                return;
            }

            if (path != "/points")
            {
                SendError(response, 404, "Unknown path. POST points to /points; GET /health for status.");
                return;
            }

            if (method != "POST")
            {
                response.Headers["Allow"] = "POST, OPTIONS";
                SendError(response, 405, "Use POST for /points.");
                return;
            }

            long limit = config.maxBodyKb * 1024L;
            if (request.ContentLength64 > limit)
            {
                FeedStatus.CountRejected(1);
                SendError(response, 413, "Body is larger than " + config.maxBodyKb + " KB. Split it or raise maxBodyKb.");
                return;
            }

            if (!TryReadBody(request, limit, out string body))
            {
                FeedStatus.CountRejected(1);
                SendError(response, 413, "Body is larger than " + config.maxBodyKb + " KB. Split it or raise maxBodyKb.");
                return;
            }

            PointParseResult result = PointParser.Parse(body, config.maxPointsPerRequest);
            if (!result.Ok)
            {
                FeedStatus.CountRejected(1);
                SendError(response, 400, result.FatalError);
                return;
            }

            if (Pending + result.Points.Count > config.maxPending)
            {
                FeedStatus.CountRejected(result.Points.Count);
                response.Headers["Retry-After"] = "1";
                SendError(response, 503, "Aegis is behind (" + Pending + " points waiting). Retry shortly.");
                return;
            }

            foreach (DataPoint point in result.Points)
            {
                queue.Enqueue(point);
                Interlocked.Increment(ref pending);
            }
            FeedStatus.CountRejected(result.Rejected);

            int status = result.Points.Count == 0 && result.Rejected > 0 ? 422 : 202;
            var json = new StringBuilder(64);
            json.Append("{\"accepted\":").Append(result.Points.Count)
                .Append(",\"rejected\":").Append(result.Rejected)
                .Append(",\"errors\":[");
            for (int i = 0; i < result.Errors.Count; i++)
            {
                if (i > 0)
                    json.Append(',');
                AppendString(json, result.Errors[i]);
            }
            json.Append("]}");
            Send(response, status, json.ToString());
        }

        string Health()
        {
            var json = new StringBuilder(160);
            json.Append("{\"status\":\"ok\"")
                .Append(",\"pending\":").Append(Pending)
                .Append(",\"accepted\":").Append(FeedStatus.Accepted)
                .Append(",\"rejected\":").Append(FeedStatus.Rejected)
                .Append(",\"demo\":").Append(EventSimulator.DemoEnabled ? "true" : "false")
                .Append('}');
            return json.ToString();
        }

        static bool TryReadBody(HttpListenerRequest request, long limit, out string body)
        {
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[8192];
                int read;
                while ((read = request.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > limit)
                    {
                        body = null;
                        return false;
                    }
                    buffer.Write(chunk, 0, read);
                }
                Encoding encoding = request.ContentEncoding ?? Encoding.UTF8;
                body = encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                return true;
            }
        }

        static void SendError(HttpListenerResponse response, int status, string message)
        {
            var json = new StringBuilder(64);
            json.Append("{\"error\":");
            AppendString(json, message);
            json.Append('}');
            Send(response, status, json.ToString());
        }

        static void Send(HttpListenerResponse response, int status, string json)
        {
            response.StatusCode = status;
            if (json != null)
            {
                byte[] payload = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = payload.Length;
                response.OutputStream.Write(payload, 0, payload.Length);
            }
            response.Close();
        }

        internal static void AppendString(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char character in value ?? "")
            {
                switch (character)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            json.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            json.Append(character);
                        break;
                }
            }
            json.Append('"');
        }

        static string Describe(Exception error)
        {
            string message = error.Message;
            if (error is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse
                || message.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0)
                return "port is already in use";
            if (error is HttpListenerException || error is SocketException)
                return message;
            return error.GetType().Name + ": " + message;
        }
    }
}
