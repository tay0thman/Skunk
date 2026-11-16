using System;
using Rhino;

namespace Skunk.Core
{
    /// <summary>Command-line messages with the Skunk prefix; debug output only when enabled.</summary>
    public static class Log
    {
        public static bool Verbose { get; set; }

        /// <summary>Extra output (tests): receives every message.</summary>
        public static Action<string> Sink { get; set; }

        static void Write(string text)
        {
            RhinoApp.WriteLine(text);
            Sink?.Invoke(text);
        }

        public static void Info(string message) => Write("Skunk: " + message);

        public static void Warn(string message) => Write("Skunk warning: " + message);

        public static void Error(string message, Exception ex = null)
        {
            Write("Skunk error: " + message + (ex != null ? " (" + ex.GetType().Name + ": " + ex.Message + ")" : ""));
            if (ex != null && (Verbose || Sink != null))
                Write(ex.ToString());
        }

        public static void Debug(string message)
        {
            if (Verbose)
                Write("Skunk debug: " + message);
        }
    }
}
