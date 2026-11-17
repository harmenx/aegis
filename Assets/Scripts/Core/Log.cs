using System.Collections.Generic;
using UnityEngine;

namespace Aegis
{
    // One place for console output, so every line carries the same prefix and
    // noisy warnings (a bad file polled every 400 ms) are reported once.
    public static class Log
    {
        const string Prefix = "[Aegis] ";

        static readonly HashSet<string> reported = new HashSet<string>();
        static readonly object gate = new object();

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        // Logs a warning the first time a key is seen; later calls with the same key are dropped.
        public static void WarnOnce(string key, string message)
        {
            lock (gate)
            {
                if (!reported.Add(key))
                    return;
            }
            Warn(message);
        }

        // Lets a source warn again once it has recovered, e.g. a file that was fixed and broke again.
        public static void Forget(string key)
        {
            lock (gate)
                reported.Remove(key);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            lock (gate)
                reported.Clear();
        }
    }
}
