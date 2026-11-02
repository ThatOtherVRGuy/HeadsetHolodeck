using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Holodeck.Save
{
    public enum WorldSortField
    {
        Modified = 0,
        Created = 1,
        Name = 2,
    }

    /// <summary>
    /// Ordering for saved-world lists.
    ///
    /// Newest-modified first is the default because the headset UI cannot always be scrolled — on the
    /// emulator there is no pointer at all — so the most recently touched world has to be reachable
    /// without navigation. The choice is persisted and exposed through <see cref="Field"/>,
    /// <see cref="Descending"/> and <see cref="Cycle"/>, so a voice or panel control can change it
    /// without touching the list code.
    /// </summary>
    public static class WorldSortSettings
    {
        const string FieldKey = "holodeck.worldSort.field";
        const string DescendingKey = "holodeck.worldSort.descending";

        public static WorldSortField Field
        {
            get => (WorldSortField)PlayerPrefs.GetInt(FieldKey, (int)WorldSortField.Modified);
            set => PlayerPrefs.SetInt(FieldKey, (int)value);
        }

        public static bool Descending
        {
            get => PlayerPrefs.GetInt(DescendingKey, 1) == 1;
            set => PlayerPrefs.SetInt(DescendingKey, value ? 1 : 0);
        }

        /// <summary>Moves to the next sort mode and returns a spoken description of it.</summary>
        public static string Cycle()
        {
            switch (Field)
            {
                case WorldSortField.Modified:
                    Field = WorldSortField.Name;
                    Descending = false;
                    break;
                case WorldSortField.Name:
                    Field = WorldSortField.Created;
                    Descending = true;
                    break;
                default:
                    Field = WorldSortField.Modified;
                    Descending = true;
                    break;
            }

            PlayerPrefs.Save();
            return Describe();
        }

        public static string Describe()
        {
            string field = Field switch
            {
                WorldSortField.Name => "name",
                WorldSortField.Created => "date created",
                _ => "date modified",
            };

            return $"Sorting worlds by {field}, {(Descending ? "newest" : "oldest")} first.";
        }

        /// <summary>
        /// Applies the current sort. Dates are ISO-8601 UTC strings, so ordinal comparison is already
        /// chronological; a missing date sorts last rather than first.
        /// </summary>
        public static List<WorldConfig> Apply(IEnumerable<WorldConfig> configs)
        {
            var list = configs?.Where(c => c != null).ToList() ?? new List<WorldConfig>();
            if (list.Count <= 1)
                return list;

            switch (Field)
            {
                case WorldSortField.Name:
                    list.Sort((a, b) => string.Compare(
                        a.display_name ?? string.Empty,
                        b.display_name ?? string.Empty,
                        StringComparison.CurrentCultureIgnoreCase));
                    break;
                case WorldSortField.Created:
                    list.Sort((a, b) => string.CompareOrdinal(
                        a.created_at ?? string.Empty,
                        b.created_at ?? string.Empty));
                    break;
                default:
                    list.Sort((a, b) => string.CompareOrdinal(
                        a.modified_at ?? string.Empty,
                        b.modified_at ?? string.Empty));
                    break;
            }

            if (Descending)
                list.Reverse();

            return list;
        }
    }
}
