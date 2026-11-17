using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Aegis
{
    public enum DemoMode
    {
        // Demo runs until the first custom point arrives.
        Auto,
        // Demo always runs, mixed with custom points.
        On,
        // No demo and no seeded totals: the dashboard starts empty.
        Off
    }

    // Runtime settings. Defaults, then aegis.config.json, then AEGIS_* environment
    // variables, then -aegis-* command-line arguments; later sources win.
    [Serializable]
    public sealed class AegisConfig
    {
        public const string FileName = "aegis.config.json";
        public const int DefaultPort = 8787;

        public int port = DefaultPort;
        public string bindAddress = "127.0.0.1";
        public bool listen = true;
        public string demo = "auto";
        public bool githubFeed = true;
        public int maxBodyKb = 1024;
        public int maxPointsPerRequest = 5000;
        public int maxPending = 20000;
        public int maxPointsPerFrame = 400;
        public float pollSeconds = 0.4f;

        static AegisConfig current;

        public static AegisConfig Current
        {
            get
            {
                if (current == null)
                    current = Load();
                return current;
            }
        }

        public DemoMode Demo => ParseDemo(demo);

        public string Endpoint => "http://" + bindAddress + ":" + port + "/points";

        public static AegisConfig Load()
        {
            var config = new AegisConfig();
            string path = FindFile();
            if (path != null)
            {
                try
                {
                    config = FromJson(File.ReadAllText(path), out string error);
                    if (error != null)
                        Log.Warn(FileName + ": " + error + " Using defaults.");
                }
                catch (Exception exception)
                {
                    Log.Warn("Could not read " + path + ". " + exception.Message + " Using defaults.");
                }
            }

            var warnings = new List<string>();
            config.ApplyOverrides(Environment.GetEnvironmentVariables(), Environment.GetCommandLineArgs(), warnings);
            config.Validate(warnings);
            foreach (string warning in warnings)
                Log.Warn(warning);
            return config;
        }

        // Test hook: replaces the cached config for the rest of the session.
        internal static void Override(AegisConfig config)
        {
            current = config;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            current = null;
        }

        static string FindFile()
        {
            string[] candidates = { AegisPaths.Root(FileName), AegisPaths.Streaming(FileName) };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        internal static AegisConfig FromJson(string json, out string error)
        {
            error = null;
            var config = new AegisConfig();
            if (string.IsNullOrWhiteSpace(json))
                return config;
            try
            {
                JsonUtility.FromJsonOverwrite(json, config);
            }
            catch (Exception exception)
            {
                error = "invalid JSON (" + exception.Message + ").";
                return new AegisConfig();
            }
            return config;
        }

        internal void ApplyOverrides(IDictionary environment, string[] args, List<string> warnings)
        {
            if (environment != null)
            {
                Read(environment, "AEGIS_PORT", v => port = ParseInt(v, "AEGIS_PORT", port, warnings));
                Read(environment, "AEGIS_BIND", v => bindAddress = v);
                Read(environment, "AEGIS_LISTEN", v => listen = ParseBool(v, "AEGIS_LISTEN", listen, warnings));
                Read(environment, "AEGIS_DEMO", v => demo = v);
            }

            if (args == null)
                return;
            for (int i = 0; i < args.Length - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i])
                {
                    case "-aegis-port":
                        port = ParseInt(value, "-aegis-port", port, warnings);
                        break;
                    case "-aegis-bind":
                        bindAddress = value;
                        break;
                    case "-aegis-listen":
                        listen = ParseBool(value, "-aegis-listen", listen, warnings);
                        break;
                    case "-aegis-demo":
                        demo = value;
                        break;
                }
            }
        }

        internal void Validate(List<string> warnings)
        {
            if (port < 1 || port > 65535)
            {
                warnings.Add("port " + port + " is out of range (1-65535). Using " + DefaultPort + ".");
                port = DefaultPort;
            }
            if (string.IsNullOrWhiteSpace(bindAddress))
                bindAddress = "127.0.0.1";
            bindAddress = bindAddress.Trim();
            if (bindAddress != "127.0.0.1" && bindAddress != "localhost")
                warnings.Add("Listening on " + bindAddress + " accepts points from other machines. There is no authentication.");
            if (!TryParseDemo(demo, out _))
            {
                warnings.Add("demo \"" + demo + "\" is not auto, on, or off. Using auto.");
                demo = "auto";
            }
            maxBodyKb = Mathf.Clamp(maxBodyKb, 1, 64 * 1024);
            maxPointsPerRequest = Mathf.Clamp(maxPointsPerRequest, 1, 1000000);
            maxPending = Mathf.Clamp(maxPending, 100, 10000000);
            maxPointsPerFrame = Mathf.Clamp(maxPointsPerFrame, 1, 100000);
            pollSeconds = Mathf.Clamp(pollSeconds, 0.05f, 60f);
        }

        static DemoMode ParseDemo(string value)
        {
            return TryParseDemo(value, out DemoMode mode) ? mode : DemoMode.Auto;
        }

        static bool TryParseDemo(string value, out DemoMode mode)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "auto":
                    mode = DemoMode.Auto;
                    return true;
                case "on":
                case "true":
                case "always":
                    mode = DemoMode.On;
                    return true;
                case "off":
                case "false":
                case "never":
                    mode = DemoMode.Off;
                    return true;
                default:
                    mode = DemoMode.Auto;
                    return false;
            }
        }

        static void Read(IDictionary environment, string name, Action<string> apply)
        {
            if (environment[name] is string value && !string.IsNullOrWhiteSpace(value))
                apply(value.Trim());
        }

        static int ParseInt(string value, string name, int fallback, List<string> warnings)
        {
            if (int.TryParse(value, out int parsed))
                return parsed;
            warnings.Add(name + " \"" + value + "\" is not a number. Keeping " + fallback + ".");
            return fallback;
        }

        static bool ParseBool(string value, string name, bool fallback, List<string> warnings)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    warnings.Add(name + " \"" + value + "\" is not true or false. Keeping " + fallback + ".");
                    return fallback;
            }
        }
    }
}
