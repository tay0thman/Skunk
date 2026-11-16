using System;
using System.Collections.Generic;
using System.Linq;

namespace Skunk.Core
{
    /// <summary>Where command aliases live: Rhino's alias list (<see cref="RhinoAliasStore"/>), or a stand-in for tests.</summary>
    public interface IAliasStore
    {
        IEnumerable<string> Names();
        /// <summary>The macro an alias runs, or null when there is no such alias.</summary>
        string Macro(string alias);
        /// <summary>Add the alias, or change what it runs.</summary>
        void Set(string alias, string macro);
        void Delete(string alias);
    }

    /// <summary>Rhino's own alias list (Tools > Options > Aliases), which is application-wide, not per model.</summary>
    public sealed class RhinoAliasStore : IAliasStore
    {
        public IEnumerable<string> Names() => Rhino.ApplicationSettings.CommandAliasList.GetNames() ?? new string[0];

        public string Macro(string alias) =>
            Rhino.ApplicationSettings.CommandAliasList.IsAlias(alias) ? Rhino.ApplicationSettings.CommandAliasList.GetMacro(alias) : null;

        public void Set(string alias, string macro)
        {
            if (Rhino.ApplicationSettings.CommandAliasList.IsAlias(alias))
                Rhino.ApplicationSettings.CommandAliasList.SetMacro(alias, macro);
            else
                Rhino.ApplicationSettings.CommandAliasList.Add(alias, macro);
        }

        public void Delete(string alias) => Rhino.ApplicationSettings.CommandAliasList.Delete(alias);
    }

    /// <summary>An alias list in memory (tests, previews). Alias names compare without case, like Rhino's.</summary>
    public sealed class MemoryAliasStore : IAliasStore
    {
        public readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Names() => Aliases.Keys.ToList();

        public string Macro(string alias) => Aliases.TryGetValue(alias, out var m) ? m : null;

        public void Set(string alias, string macro) => Aliases[alias] = macro;

        public void Delete(string alias) => Aliases.Remove(alias);
    }

    /// <summary>
    /// Keyboard aliases for Skunk commands. Skunk suggests a few (<see cref="Defaults"/>); every command can
    /// get an alias of the user's own. Applying a table makes Rhino's aliases for those commands match it:
    /// a command's other plain aliases go, its alias is added, and an alias that already runs something else
    /// is replaced only when asked to.
    /// </summary>
    public static class KeyboardAliases
    {
        /// <summary>Skunk's suggested aliases.</summary>
        public static readonly (string Alias, string Command)[] Defaults =
        {
            ("LL", "SkLevel"), ("PI", "SkProperties"), ("SPE", "SkSpotElevation"), ("SPC", "SkSpotCoordinate"), ("SPS", "SkSpotSlope"),
        };

        /// <summary>Skunk's suggested alias for a command, or "".</summary>
        public static string DefaultFor(string command) =>
            Defaults.FirstOrDefault(d => string.Equals(d.Command, command, StringComparison.OrdinalIgnoreCase)).Alias ?? "";

        /// <summary>The command a macro runs when it is just that command ("SkLevel", "_SkLevel", "! _SkLevel"); otherwise null.</summary>
        public static string CommandOf(string macro)
        {
            var m = (macro ?? "").Trim();
            if (m.StartsWith("!", StringComparison.Ordinal))
                m = m.Substring(1).Trim();
            while (m.StartsWith("_", StringComparison.Ordinal) || m.StartsWith("-", StringComparison.Ordinal))
                m = m.Substring(1);
            return m.Length == 0 || m.Any(char.IsWhiteSpace) ? null : m;
        }

        /// <summary>An alias Rhino accepts: one word, no spaces, at most 32 characters.</summary>
        public static bool IsValid(string alias) =>
            !string.IsNullOrEmpty(alias) && alias.Length <= 32 && !alias.Any(char.IsWhiteSpace) && !alias.Any(char.IsControl);

        /// <summary>The aliases in the store that run each of <paramref name="commands"/> on its own.</summary>
        public static Dictionary<string, List<string>> Current(IAliasStore store, IEnumerable<string> commands)
        {
            var result = commands.ToDictionary(c => c, c => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var alias in store.Names())
            {
                var command = CommandOf(store.Macro(alias));
                if (command != null && result.TryGetValue(command, out var list))
                    list.Add(alias);
            }
            foreach (var list in result.Values)
                list.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>What an apply did.</summary>
        public sealed class Outcome
        {
            public readonly List<string> Added = new List<string>();
            public readonly List<string> Removed = new List<string>();
            public readonly List<string> Replaced = new List<string>();
            public readonly List<string> Skipped = new List<string>();
            public readonly List<string> Invalid = new List<string>();

            public bool Changed => Added.Count + Removed.Count + Replaced.Count > 0;

            public string Summary()
            {
                var parts = new List<string>();
                if (Added.Count > 0) parts.Add("Set: " + string.Join(", ", Added) + ".");
                if (Replaced.Count > 0) parts.Add("Replaced: " + string.Join(", ", Replaced) + ".");
                if (Removed.Count > 0) parts.Add("Removed: " + string.Join(", ", Removed) + ".");
                if (Skipped.Count > 0) parts.Add("Left alone (they run something else; tick \"Replace\" to take them over): " + string.Join(", ", Skipped) + ".");
                if (Invalid.Count > 0) parts.Add("Not used: " + string.Join(", ", Invalid) + ".");
                return parts.Count == 0 ? "Nothing to change: Rhino's aliases already match." : string.Join("\n", parts);
            }
        }

        /// <summary>
        /// Make the store's aliases for the commands in <paramref name="wanted"/> (command -> alias; empty = no alias)
        /// match it. Plain aliases that run one of those commands but are not its wanted alias are removed, so an
        /// alias can move between Skunk commands. A wanted alias that already runs something else is replaced only
        /// when <paramref name="replaceOthers"/> is set; otherwise it is left alone and reported.
        /// </summary>
        public static Outcome Apply(IAliasStore store, IDictionary<string, string> wanted, bool replaceOthers)
        {
            var outcome = new Outcome();
            // what each command should get, checked: one word, and each alias used once
            var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in wanted)
            {
                var alias = (kv.Value ?? "").Trim();
                if (alias.Length > 0 && !IsValid(alias))
                {
                    outcome.Invalid.Add("\"" + alias + "\" for " + kv.Key + " (one word, no spaces)");
                    alias = null;
                }
                else if (alias.Length > 0 && claimed.TryGetValue(alias, out var first))
                {
                    outcome.Invalid.Add(alias + " for " + kv.Key + " (already chosen for " + first + ")");
                    alias = null;
                }
                if (alias == null)
                    continue;   // left as it is
                if (alias.Length > 0)
                    claimed[alias] = kv.Key;
                target[kv.Key] = alias;
            }

            // plain aliases of these commands that are not wanted any more
            foreach (var alias in store.Names().ToList())
            {
                var command = CommandOf(store.Macro(alias));
                if (command == null || !target.TryGetValue(command, out var keep))
                    continue;
                if (!string.Equals(keep, alias, StringComparison.OrdinalIgnoreCase))
                {
                    store.Delete(alias);
                    outcome.Removed.Add(alias + " (" + command + ")");
                }
            }

            // the wanted ones
            foreach (var kv in target)
            {
                if (kv.Value.Length == 0)
                    continue;
                var existing = store.Macro(kv.Value);
                if (existing == null)
                {
                    store.Set(kv.Value, kv.Key);
                    outcome.Added.Add(kv.Value + " = " + kv.Key);
                }
                else if (string.Equals(CommandOf(existing), kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    // already runs it
                }
                else if (replaceOthers)
                {
                    store.Set(kv.Value, kv.Key);
                    outcome.Replaced.Add(kv.Value + " = " + kv.Key + " (was " + existing + ")");
                }
                else
                    outcome.Skipped.Add(kv.Value + " (runs " + existing + ")");
            }
            return outcome;
        }

        /// <summary>Skunk's suggested aliases as a table for <see cref="Apply"/>.</summary>
        public static Dictionary<string, string> DefaultTable() =>
            Defaults.ToDictionary(d => d.Command, d => d.Alias, StringComparer.OrdinalIgnoreCase);

        /// <summary>"LL Level, PI Properties, ..." for the Skunk aliases Rhino has now.</summary>
        public static string Describe(IAliasStore store)
        {
            var current = Current(store, CommandList.All.Select(c => c.Name));
            var parts = new List<string>();
            foreach (var c in CommandList.All)
                if (current.TryGetValue(c.Name, out var list) && list.Count > 0)
                    parts.Add(string.Join(" / ", list) + " " + c.Label);
            return parts.Count == 0 ? "none yet" : string.Join(", ", parts);
        }
    }
}
