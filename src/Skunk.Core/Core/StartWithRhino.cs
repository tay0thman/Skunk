using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Skunk.Core
{
    /// <summary>Rhino's "run these commands every time Rhino starts" list (Options > General > Command Lists).</summary>
    public interface IStartupStore
    {
        string Get();
        void Set(string commands);
    }

    /// <summary>
    /// The list in Rhino's own settings (Options / General / StartupCommands). Rhino's application settings, not the
    /// model's: tests use <see cref="MemoryStartupStore"/> and must never touch this one (headless Rhino runs on the
    /// user's own settings).
    /// </summary>
    public sealed class RhinoStartupStore : IStartupStore
    {
        const string Key = "StartupCommands";

        static Rhino.PersistentSettings General(bool create)
        {
            var root = Rhino.PersistentSettings.RhinoAppSettings;
            if (root == null)
                return null;
            if (!root.TryGetChild("Options", out var options))
            {
                if (!create)
                    return null;
                options = root.AddChild("Options");
            }
            if (!options.TryGetChild("General", out var general))
            {
                if (!create)
                    return null;
                general = options.AddChild("General");
            }
            return general;
        }

        public string Get() => General(false) is Rhino.PersistentSettings g && g.TryGetString(Key, out var s) ? s ?? "" : "";

        public void Set(string commands) => General(true)?.SetString(Key, commands ?? "");
    }

    public sealed class MemoryStartupStore : IStartupStore
    {
        public string Value = "";

        public string Get() => Value;

        public void Set(string commands) => Value = commands ?? "";
    }

    /// <summary>
    /// Start Skunk with Rhino: the Skunk command in Rhino's startup command list. Skunk then loads when Rhino starts:
    /// live updates from the first moment, and its toolbar opened from wherever the newest Skunk package is (after a
    /// package update Rhino still names the old version's toolbar file until Skunk loads). Other startup commands stay
    /// exactly as they are.
    /// </summary>
    public static class StartWithRhino
    {
        const string Command = "_Skunk";

        static readonly Regex Token = new Regex(@"(?<=^|\s)[!_\-]*Skunk(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsOn(IStartupStore store) => Token.IsMatch(store.Get() ?? "");

        /// <summary>Add or remove Skunk; the rest of the list stays as it was. True when the list changed.</summary>
        public static bool Set(IStartupStore store, bool on)
        {
            string current = store.Get() ?? "";
            if (IsOn(store) == on)
                return false;
            string next;
            if (on)
                next = current.Trim().Length == 0 ? Command : current.TrimEnd() + "\n" + Command;
            else
            {
                var lines = Token.Replace(current, "").Replace("\r\n", "\n").Split('\n')
                    .Select(l => Regex.Replace(l, @"[ \t]{2,}", " ").Trim()).Where(l => l.Length > 0);
                next = string.Join("\n", lines);
            }
            store.Set(next);
            return true;
        }
    }
}
